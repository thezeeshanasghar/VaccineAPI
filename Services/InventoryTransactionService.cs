using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    public class InventoryOperationResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = "";
        public static InventoryOperationResult Ok() => new InventoryOperationResult { IsSuccess = true };
        public static InventoryOperationResult Fail(string message) => new InventoryOperationResult { IsSuccess = false, Message = message };
    }

    // The only code allowed to mutate Stock.Quantity/OriginalQuantity or BrandAmount.Count.
    // Every mutation here writes a matching, append-only InventoryTransaction row first —
    // nothing in this service ever updates or deletes a row in that table.
    //
    // Callers are still responsible for their own outer transaction (BeginTransactionAsync)
    // and SaveChangesAsync — this service only stages changes via the EF change tracker plus
    // _db.InventoryTransactions.Add(...); it does not call SaveChangesAsync itself, so a
    // caller's existing transaction-then-SaveChanges-then-commit pattern (and its
    // DbUpdateConcurrencyException handling) is unchanged.
    public class InventoryTransactionService
    {
        private readonly Context _db;
        public InventoryTransactionService(Context db) { _db = db; }

        private void Log(long doctorId, long clinicId, long brandId, int? stockId, string? batchLot,
            DateTime? expiry, int quantityDelta, decimal? unitCost, InventoryTransactionType sourceType,
            long sourceId, DateTime eventDate, long? createdByPaId = null,
            bool consumesStock = true, string? decisionReason = null)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                DoctorId = doctorId,
                ClinicId = clinicId,
                BrandId = brandId,
                StockId = stockId,
                BatchLot = batchLot,
                Expiry = expiry,
                QuantityDelta = quantityDelta,
                UnitCost = unitCost,
                SourceType = sourceType,
                SourceId = sourceId,
                EventDate = eventDate.Date,
                CreatedByPaId = createdByPaId,
                ConsumesStock = consumesStock,
                DecisionReason = decisionReason
            });
        }

        // v2 FEFO batch source: prefer the authoritative Stock.ClinicId; fall back to the bill's
        // clinic for pre-v2 rows where Stock.ClinicId is still null (nothing backfilled it yet).
        // Once all live rows carry ClinicId (post-reset), the Bill fallback becomes dead weight
        // and can be dropped. Only rows with Quantity > 0 and not closed are eligible.
        private IQueryable<Stock> FefoFillCandidates(long brandId, long clinicId)
        {
            return _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId
                    && !s.IsClosed
                    && s.Quantity > 0
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id);
        }

        // v2: read-only dry-run for the give-time unbatched-stock confirmation prompt. Callers
        // must only invoke this when the give would actually consume stock (decision.ConsumesStock
        // == true) — a pre-period/OHF/historical give never needed a batch, so asking here would
        // be a spurious prompt for a give that was never going to touch stock at all.
        public bool HasFillableBatch(long brandId, long clinicId)
        {
            return FefoFillCandidates(brandId, clinicId).Any();
        }

        // Restore target for ungive: same clinic resolution as FEFO, but includes zero-quantity
        // rows (a fully-consumed batch is a valid restore target) — reopens it if it was closed.
        private IQueryable<Stock> FefoRestoreCandidates(long brandId, long clinicId)
        {
            return _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId
                    && s.Quantity >= 0
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id);
        }

        // ----- Purchase (BillController.Create) -----
        // Mirrors the existing same-bill-only consolidation: a line only merges into a Stock
        // row that already belongs to THIS bill (never another bill's row).
        public async Task<Stock> PostPurchaseLine(long doctorId, long clinicId, int billId, long brandId,
            int quantity, decimal stockAmount, string? batchLot, DateTime? expiry, DateTime billDate)
        {
            var existingStock = await _db.Stocks
                .Where(s => s.BrandId == brandId && s.BillId == billId && s.BatchLot == batchLot && s.Expiry == expiry)
                .FirstOrDefaultAsync();

            Stock stock;
            if (existingStock != null)
            {
                existingStock.Quantity += quantity;
                existingStock.OriginalQuantity += quantity;
                stock = existingStock;
            }
            else
            {
                stock = new Stock
                {
                    BrandId = brandId,
                    BillId = billId,
                    Quantity = quantity,
                    OriginalQuantity = quantity,
                    StockAmount = stockAmount,
                    BatchLot = batchLot,
                    Expiry = expiry
                };
                _db.Stocks.Add(stock);
                await _db.SaveChangesAsync(); // need stock.Id for the ledger row below
            }

            var ba = await GetOrNoOpBrandAmount(brandId, doctorId, clinicId);
            if (ba != null) ba.Count += quantity;

            Log(doctorId, clinicId, brandId, stock.Id, batchLot, expiry, quantity, stockAmount,
                InventoryTransactionType.Purchase, billId, billDate);

            return stock;
        }

        // ----- Bill edit reversal of old lines (BillController.Update) -----
        public async Task ReverseBillLine(long doctorId, long clinicId, Stock stock, int billId, DateTime billDate)
        {
            var ba = await GetOrNoOpBrandAmount(stock.BrandId, doctorId, clinicId);
            if (ba != null) ba.Count = Math.Max(0, ba.Count - stock.Quantity);

            Log(doctorId, clinicId, stock.BrandId, stock.Id, stock.BatchLot, stock.Expiry,
                -stock.Quantity, stock.StockAmount, InventoryTransactionType.BillEdit, billId, billDate);

            // v2: close, never hard-delete (Models/Stock.cs:29-31) — unconditionally deleting here
            // threw the same "row still referenced" error as the SellDirect/TransferOut bug the
            // instant any sale/give/transfer had already logged a ledger row against this batch.
            stock.Quantity = 0;
            stock.IsClosed = true;
            _db.Entry(stock).State = EntityState.Modified;
        }

        // ----- Split-consumed (BillController.SplitConsumed) -----
        // Shrinks the original line to its unconsumed remainder and logs the consumed portion
        // as moved to the new bill. No live Stock.Quantity/BrandAmount.Count change — the
        // consumption itself was already logged by whatever Administer/DirectSale/etc. call
        // originally deducted it; this only re-labels which bill the cost history belongs to.
        public void LogSplitConsumed(long doctorId, long clinicId, long brandId, int originalStockId,
            int newStockId, string? batchLot, DateTime? expiry, int consumedQuantity, decimal unitCost,
            int originalBillId, int newBillId, DateTime billDate)
        {
            Log(doctorId, clinicId, brandId, originalStockId, batchLot, expiry, -consumedQuantity,
                unitCost, InventoryTransactionType.SplitConsumed, originalBillId, billDate);
            Log(doctorId, clinicId, brandId, newStockId, batchLot, expiry, consumedQuantity,
                unitCost, InventoryTransactionType.SplitConsumed, newBillId, billDate);
        }

        // ----- §6.3a Batch correction (ScheduleController.CorrectBatch) -----
        // Label-only audit row for a manual edit of a given dose's batch/expiry. Moves NO stock
        // (QuantityDelta = 0, ConsumesStock = false); it only records who/when set the NEW
        // BatchLot/Expiry on schedule `scheduleId`. The previous values live on the dose's prior
        // Administer row (same SourceId), so old→new is fully recoverable without a new column.
        public void LogBatchCorrection(long doctorId, long clinicId, long brandId, int? stockId,
            string? newBatchLot, DateTime? newExpiry, long scheduleId, DateTime eventDate, long? createdByPaId)
        {
            Log(doctorId, clinicId, brandId, stockId, newBatchLot, newExpiry, 0, null,
                InventoryTransactionType.BatchCorrection, scheduleId, eventDate, createdByPaId,
                consumesStock: false, decisionReason: null);
        }

        // ----- Bill reverse (BillController.Reverse) -----
        public async Task ReverseBillStock(long doctorId, long clinicId, Stock stock, int billId, DateTime billDate)
        {
            var ba = await GetOrNoOpBrandAmount(stock.BrandId, doctorId, clinicId);
            if (ba != null) ba.Count = Math.Max(0, ba.Count - stock.Quantity);

            Log(doctorId, clinicId, stock.BrandId, stock.Id, stock.BatchLot, stock.Expiry,
                -stock.Quantity, stock.StockAmount, InventoryTransactionType.BillReverse, billId, billDate);

            // v2: close, never hard-delete (Models/Stock.cs:29-31) — same fix as ReverseBillLine
            // above: unconditional delete crashes the instant any other row already references
            // this batch, and can also destroy stock that was never even consumed yet.
            stock.Quantity = 0;
            stock.IsClosed = true;
            _db.Entry(stock).State = EntityState.Modified;
        }

        // ----- Adjust Stock: Increase (AdjustStockController.Create, Type == "Increase") -----
        // Matches existing behavior exactly: brand-level only, no Stock row created, so
        // increase-adjusted stock has no batch/FEFO tracking (StockId stays null in the ledger).
        public async Task<InventoryOperationResult> AdjustIncrease(long doctorId, long clinicId, long brandId,
            int quantity, decimal price, long adjustStockId, string? batchLot, DateTime? expiry, DateTime eventDate)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null)
                return InventoryOperationResult.Fail("Brand not configured at this clinic");

            // Find or create a Stock row for this batch so FEFO can deduct from it when doses are given.
            // AdjustIncrease used to be brand-level only (no Stock row), causing FEFO to miss these units
            // and roll back the ba.Count decrement silently while the dose was still physically given.
            //
            // v2: match by identity (BrandId+ClinicId+BatchLot+Expiry), not by Quantity > 0 — a batch
            // drawn down to zero (or negative, pre-fix) by gives is still the SAME real batch and must
            // be topped up, not forked into a second row. Matching on Quantity > 0 was the bug behind
            // a purchase creating a duplicate "same batch" row while the real one sat empty/negative.
            var existingStock = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId
                         && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId))
                         && s.BatchLot == batchLot && s.Expiry == expiry)
                .FirstOrDefaultAsync();

            int? stockId = null;
            if (existingStock != null)
            {
                existingStock.Quantity += quantity;
                existingStock.OriginalQuantity += quantity;
                if (existingStock.IsClosed) existingStock.IsClosed = false; // reopen if a purchase revives it
                stockId = existingStock.Id;
            }
            else
            {
                // Anchor to the most recent non-XFER bill so FEFO joins (Stock->Bill->ClinicId) can find it
                var anchorBill = await _db.Bills
                    .Where(b => b.ClinicId == clinicId && b.DoctorId == doctorId && !b.BillNo.StartsWith("XFER-"))
                    .OrderByDescending(b => b.BillDate).ThenByDescending(b => b.Id)
                    .FirstOrDefaultAsync();

                if (anchorBill != null)
                {
                    var newStock = new Stock
                    {
                        BrandId = brandId,
                        BillId = anchorBill.Id,
                        Quantity = quantity,
                        OriginalQuantity = quantity,
                        StockAmount = price,
                        BatchLot = batchLot,
                        Expiry = expiry
                    };
                    _db.Stocks.Add(newStock);
                    await _db.SaveChangesAsync(); // need Id for the ledger row
                    stockId = newStock.Id;
                }
            }

            ba.Count += quantity;
            Log(doctorId, clinicId, brandId, stockId, batchLot, expiry, quantity, price,
                InventoryTransactionType.AdjustIncrease, adjustStockId, eventDate);
            return InventoryOperationResult.Ok();
        }

        // ----- §Opening Balance (StockController.PostOpeningBalance) -----
        // Records physical stock already on the shelf at the reset as a real batch + ledger row,
        // so the ledger equals reality from day one. Unlike AdjustIncrease this sets Stock.ClinicId
        // directly and needs NO anchor bill — right after a reset a clinic may have zero bills.
        // eventDate is the clinic's StockPeriodStart so it lands at (not before) the reset floor.
        public async Task<InventoryOperationResult> PostOpeningBalance(long doctorId, long clinicId,
            long brandId, int quantity, decimal unitCost, string? batchLot, DateTime? expiry, DateTime eventDate)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null)
                return InventoryOperationResult.Fail("Brand not configured at this clinic");

            // Merge into an existing open batch of the same lot/expiry at this clinic, else new row.
            var existingStock = await _db.Stocks
                .Where(s => s.BrandId == brandId && s.ClinicId == clinicId
                         && s.BatchLot == batchLot && s.Expiry == expiry && !s.IsClosed)
                .FirstOrDefaultAsync();

            int stockId;
            if (existingStock != null)
            {
                existingStock.Quantity += quantity;
                existingStock.OriginalQuantity += quantity;
                stockId = existingStock.Id;
            }
            else
            {
                var newStock = new Stock
                {
                    BrandId = brandId,
                    ClinicId = clinicId,   // v2: FEFO reads Stock.ClinicId directly — no bill needed
                    Quantity = quantity,
                    OriginalQuantity = quantity,
                    StockAmount = unitCost,
                    BatchLot = batchLot,
                    Expiry = expiry
                };
                _db.Stocks.Add(newStock);
                await _db.SaveChangesAsync(); // need Id for the ledger row
                stockId = newStock.Id;
            }

            ba.Count += quantity;
            ba.NeedsReconcile = false;
            Log(doctorId, clinicId, brandId, stockId, batchLot, expiry, quantity, unitCost,
                InventoryTransactionType.OpeningBalance, stockId, eventDate);
            return InventoryOperationResult.Ok();
        }

        // ----- Adjust Stock: Loss (AdjustStockController.Create, Type == "Loss") -----
        public async Task<InventoryOperationResult> AdjustLoss(long doctorId, long clinicId, long brandId,
            int quantity, long adjustStockId, string batchLot, DateTime eventDate)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null || ba.Count == 0)
                return InventoryOperationResult.Fail("No stock available for this brand at this clinic");

            var stockRow = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId
                         && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId))
                         && s.BatchLot == batchLot && s.Quantity > 0)
                .FirstOrDefaultAsync();
            if (stockRow == null)
                return InventoryOperationResult.Fail("Batch not found or has no remaining stock");
            if (quantity > stockRow.Quantity)
                return InventoryOperationResult.Fail($"Cannot reduce more than available quantity ({stockRow.Quantity}) in this batch");

            stockRow.Quantity -= quantity;
            ba.Count = Math.Max(0, ba.Count - quantity);

            Log(doctorId, clinicId, brandId, stockRow.Id, batchLot, stockRow.Expiry, -quantity,
                stockRow.StockAmount, InventoryTransactionType.AdjustLoss, adjustStockId, eventDate);
            return InventoryOperationResult.Ok();
        }

        // ----- Adjust Stock: reverse (AdjustStockController.Delete) -----
        public async Task ReverseAdjustment(long doctorId, long clinicId, long brandId, int adjustment,
            string? batchLot, long adjustStockId)
        {
            var ba = await GetOrNoOpBrandAmount(brandId, doctorId, clinicId);
            if (ba != null)
            {
                if (adjustment > 0) ba.Count = Math.Max(0, ba.Count - adjustment);
                else ba.Count += Math.Abs(adjustment);
            }

            int? affectedStockId = null;
            decimal? unitCost = null;
            DateTime? expiry = null;
            // For Increase reversal: undo the Stock row that AdjustIncrease now creates
            if (adjustment > 0 && !string.IsNullOrEmpty(batchLot))
            {
                var stockRow = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == brandId
                             && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId))
                             && s.BatchLot == batchLot && s.Quantity > 0)
                    .FirstOrDefaultAsync();
                if (stockRow != null)
                {
                    int restoreQty = Math.Min(adjustment, stockRow.Quantity);
                    stockRow.Quantity -= restoreQty;
                    stockRow.OriginalQuantity -= restoreQty;
                    affectedStockId = stockRow.Id;
                    unitCost = stockRow.StockAmount;
                    expiry = stockRow.Expiry;
                }
            }
            // For Loss reversal: restore the Stock row that AdjustLoss decremented
            else if (adjustment < 0 && !string.IsNullOrEmpty(batchLot))
            {
                var stockRow = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == brandId
                             && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId))
                             && s.BatchLot == batchLot)
                    .FirstOrDefaultAsync();
                if (stockRow != null)
                {
                    stockRow.Quantity += Math.Abs(adjustment);
                    affectedStockId = stockRow.Id;
                    unitCost = stockRow.StockAmount;
                    expiry = stockRow.Expiry;
                }
            }

            Log(doctorId, clinicId, brandId, affectedStockId, batchLot, expiry, -adjustment, unitCost,
                InventoryTransactionType.AdjustReverse, adjustStockId, DateTime.Today);
        }

        // ----- Stock Transfer: out (source) + in (destination) (StockTransferController.Create) -----
        // sourceStock must already be validated/loaded by the caller (quantity check etc. stays
        // in the controller, same as today — this service performs the mutation only).
        public async Task<Stock> TransferOut(long doctorId, long fromClinicId, Stock sourceStock,
            BrandAmount sourceBa, int quantity, long stockTransferId, DateTime eventDate)
        {
            sourceStock.Quantity -= quantity;
            sourceBa.Count = Math.Max(0, sourceBa.Count - quantity);

            Log(doctorId, fromClinicId, sourceStock.BrandId, sourceStock.Id, sourceStock.BatchLot,
                sourceStock.Expiry, -quantity, sourceStock.StockAmount, InventoryTransactionType.TransferOut,
                stockTransferId, eventDate);

            // v2: close, never hard-delete (Models/Stock.cs:29-31) — deleting a depleted batch
            // throws once any other table (this batch's own prior ledger rows, or Schedule.StockId)
            // already references its Id. See SellDirect below for the same fix.
            if (sourceStock.Quantity == 0)
                sourceStock.IsClosed = true;

            _db.Entry(sourceStock).State = EntityState.Modified;

            return sourceStock;
        }

        public async Task<Stock> TransferIn(long doctorId, long toClinicId, long brandId, int billId,
            int quantity, decimal unitPrice, string batchLot, DateTime? expiry, long stockTransferId,
            decimal sourceSalePrice, DateTime eventDate)
        {
            var destStock = new Stock
            {
                BrandId = brandId,
                BillId = billId,
                Quantity = quantity,
                OriginalQuantity = quantity,
                StockAmount = unitPrice,
                BatchLot = batchLot,
                Expiry = expiry
            };
            _db.Stocks.Add(destStock);
            await _db.SaveChangesAsync(); // need destStock.Id for the ledger row

            // Matches existing behavior exactly: auto-create the destination BrandAmount row if
            // it doesn't exist yet, seeded from the SOURCE CLINIC's sale price (Amount) — the
            // caller passes the already-loaded source BrandAmount.Amount rather than this
            // service re-querying by BrandId+DoctorId with no ClinicId filter, which could
            // silently match a different clinic's BrandAmount if more than one exists.
            var destBa = await _db.BrandAmounts.FirstOrDefaultAsync(b =>
                b.BrandId == brandId && b.DoctorId == doctorId && b.ClinicId == toClinicId);
            if (destBa == null)
            {
                destBa = new BrandAmount
                {
                    BrandId = brandId,
                    DoctorId = doctorId,
                    ClinicId = toClinicId,
                    Count = 0,
                    Amount = sourceSalePrice,
                    PurchasedAmt = 0
                };
                _db.BrandAmounts.Add(destBa);
                await _db.SaveChangesAsync();
            }
            destBa.Count += quantity;

            Log(doctorId, toClinicId, brandId, destStock.Id, batchLot, expiry, quantity, unitPrice,
                InventoryTransactionType.TransferIn, stockTransferId, eventDate);

            return destStock;
        }

        // ----- Stock Transfer: reverse (StockTransferController.Delete) -----
        // Restores the source side. If the original Stock row still exists, restores it
        // directly (and logs against its real StockId — no anchor-bill fabrication needed).
        // If it was hard-deleted at zero, logs the TransferReverse with StockId = null rather
        // than inventing a fake anchor bill; the restored Stock row created below still needs
        // *some* BillId for existing FEFO/sale/loss queries (they inner-join Stock->Bill), so it
        // is anchored the same way as before — but the ledger itself records the truth (no
        // BillId claim), keeping this fixable later without re-deriving from scratch.
        public async Task ReverseTransferOut(long doctorId, long fromClinicId, long brandId, int quantity,
            string batchLot, decimal unitPrice, DateTime? expiry, long stockTransferId)
        {
            var sourceBa = await GetOrNoOpBrandAmount(brandId, doctorId, fromClinicId);
            if (sourceBa != null) sourceBa.Count += quantity;

            var sourceStock = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.BatchLot == batchLot
                         && (s.ClinicId == fromClinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == fromClinicId)))
                .FirstOrDefaultAsync();

            int? affectedStockId;
            if (sourceStock != null)
            {
                sourceStock.Quantity += quantity;
                _db.Entry(sourceStock).State = EntityState.Modified;
                affectedStockId = sourceStock.Id;
            }
            else
            {
                var anchorBill = await _db.Bills
                    .Where(b => b.ClinicId == fromClinicId && !b.BillNo.StartsWith("XFER-"))
                    .OrderByDescending(b => b.BillDate).ThenByDescending(b => b.Id)
                    .FirstOrDefaultAsync();
                affectedStockId = null;
                if (anchorBill != null)
                {
                    var recreated = new Stock
                    {
                        BrandId = brandId,
                        BillId = anchorBill.Id,
                        Quantity = quantity,
                        OriginalQuantity = 0,
                        StockAmount = unitPrice,
                        BatchLot = batchLot,
                        Expiry = expiry
                    };
                    _db.Stocks.Add(recreated);
                    await _db.SaveChangesAsync();
                    affectedStockId = recreated.Id;
                }
            }

            Log(doctorId, fromClinicId, brandId, affectedStockId, batchLot, expiry, quantity, unitPrice,
                InventoryTransactionType.TransferReverse, stockTransferId, DateTime.Today);
        }

        public async Task ReverseTransferIn(long doctorId, long toClinicId, long brandId, int quantity,
            long stockTransferId)
        {
            var destBa = await GetOrNoOpBrandAmount(brandId, doctorId, toClinicId);
            if (destBa != null) destBa.Count = Math.Max(0, destBa.Count - quantity);

            Log(doctorId, toClinicId, brandId, null, null, null, -quantity, null,
                InventoryTransactionType.TransferReverse, stockTransferId, DateTime.Today);
        }

        // ----- Direct Sale (DirectSaleController.Create) -----
        public async Task<Stock> SellDirect(long doctorId, long clinicId, Stock sourceStock, BrandAmount sourceBa,
            int quantity, long directSaleId, DateTime eventDate)
        {
            sourceStock.Quantity -= quantity;
            sourceBa.Count = Math.Max(0, sourceBa.Count - quantity);

            Log(doctorId, clinicId, sourceStock.BrandId, sourceStock.Id, sourceStock.BatchLot,
                sourceStock.Expiry, -quantity, sourceStock.StockAmount, InventoryTransactionType.DirectSale,
                directSaleId, eventDate);

            // v2: close, never hard-delete (Models/Stock.cs:29-31) — this was the one path still
            // removing the row outright, which throws once any other table (ledger rows from this
            // batch's own prior sales/gives, or Schedule.StockId) already references this Id.
            if (sourceStock.Quantity == 0)
                sourceStock.IsClosed = true;

            _db.Entry(sourceStock).State = EntityState.Modified;

            return sourceStock;
        }

        // ----- Direct Sale: reverse (DirectSaleController.Delete) -----
        public async Task ReverseDirectSale(long doctorId, long clinicId, long brandId, int quantity,
            string batchLot, decimal unitPrice, DateTime? expiry, long directSaleId)
        {
            var sourceBa = await GetOrNoOpBrandAmount(brandId, doctorId, clinicId);
            if (sourceBa != null) sourceBa.Count += quantity;

            var sourceStock = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.BatchLot == batchLot
                         && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
                .FirstOrDefaultAsync();

            int? affectedStockId;
            if (sourceStock != null)
            {
                sourceStock.Quantity += quantity;
                _db.Entry(sourceStock).State = EntityState.Modified;
                affectedStockId = sourceStock.Id;
            }
            else
            {
                var anchorBill = await _db.Bills
                    .Where(b => b.ClinicId == clinicId && !b.BillNo.StartsWith("XFER-"))
                    .OrderByDescending(b => b.BillDate).ThenByDescending(b => b.Id)
                    .FirstOrDefaultAsync();
                affectedStockId = null;
                if (anchorBill != null)
                {
                    var recreated = new Stock
                    {
                        BrandId = brandId,
                        BillId = anchorBill.Id,
                        Quantity = quantity,
                        OriginalQuantity = 0,
                        StockAmount = unitPrice,
                        BatchLot = batchLot,
                        Expiry = expiry
                    };
                    _db.Stocks.Add(recreated);
                    await _db.SaveChangesAsync();
                    affectedStockId = recreated.Id;
                }
            }

            Log(doctorId, clinicId, brandId, affectedStockId, batchLot, expiry, quantity, unitPrice,
                InventoryTransactionType.DirectSaleReverse, directSaleId, DateTime.Today);
        }

        // ----- Vaccine Administration: give (ScheduleController, FEFO) -----
        // Returns false (with Message set) if there isn't enough stock; caller must treat this
        // exactly like today's `Count <= 0` / fill-remaining-not-reaching-zero checks.
        public async Task<InventoryOperationResult> Administer(long doctorId, long clinicId, long brandId,
            long scheduleId, DateTime eventDate, long? createdByPaId = null)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(b => b.BrandId == brandId && b.DoctorId == doctorId && b.ClinicId == clinicId);
            if (ba == null)
                return InventoryOperationResult.Fail("Inventory row not found for brand");
            if (ba.Count <= 0)
                return InventoryOperationResult.Fail("Insufficient inventory for brand");

            ba.Count -= 1;

            var fillStocks = await FefoFillCandidates(brandId, clinicId).ToListAsync();

            int remaining = 1;
            foreach (var src in fillStocks)
            {
                if (remaining <= 0) break;
                int deduct = Math.Min(src.Quantity, remaining);
                src.Quantity -= deduct;
                remaining -= deduct;

                Log(doctorId, clinicId, brandId, src.Id, src.BatchLot, src.Expiry, -deduct, src.StockAmount,
                    InventoryTransactionType.Administer, scheduleId, eventDate, createdByPaId,
                    consumesStock: true, decisionReason: InventoryDecisionReason.Normal);

                // v2: never hard-delete a batch — a depleted row stays and is marked closed.
                if (src.Quantity == 0) src.IsClosed = true;
                _db.Entry(src).State = EntityState.Modified;
            }

            // Matches existing bulk-path behavior: if FEFO couldn't fully satisfy the deduction
            // (batches summed to less than 1 unit — stale Count vs. actual Stock rows), roll the
            // Count decrement back rather than leave it silently wrong.
            if (remaining > 0) ba.Count += 1;

            return InventoryOperationResult.Ok();
        }

        // ----- Vaccine Administration: ungive (ScheduleController rollback, FEFO restore) -----
        public async Task Unadminister(long doctorId, long clinicId, long brandId, long scheduleId,
            DateTime eventDate, long? createdByPaId = null)
        {
            var ba = await GetOrNoOpBrandAmount(brandId, doctorId, clinicId);
            if (ba != null) ba.Count += 1;

            // Restore to the FEFO-first eligible batch (includes zero-qty rows; reopens if closed).
            var restoreStock = await FefoRestoreCandidates(brandId, clinicId).FirstOrDefaultAsync();

            if (restoreStock != null)
            {
                restoreStock.Quantity += 1;
                if (restoreStock.IsClosed) restoreStock.IsClosed = false;
                _db.Entry(restoreStock).State = EntityState.Modified;
                Log(doctorId, clinicId, brandId, restoreStock.Id, restoreStock.BatchLot, restoreStock.Expiry,
                    1, restoreStock.StockAmount, InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
            }
            else
            {
                decimal? unitCost = ba != null ? ba.PurchasedAmt : (decimal?)null;
                Log(doctorId, clinicId, brandId, null, null, null, 1, unitCost,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
            }
        }

        // ----- Synchronous mirrors of Administer/Unadminister -----
        // ScheduleController's give/ungive paths are synchronous (use _db.SaveChanges(), not
        // SaveChangesAsync()) — calling the async versions above and blocking on them risks
        // deadlocking in an ASP.NET Core request context. These are identical logic, just
        // using sync EF calls so they compose safely with that controller's existing sync code.
        // Caller (ScheduleController) has already validated ba != null and ba.Count > 0 with
        // its own richer, context-specific error messages (BuildInventoryContextMessage) before
        // calling this — this overload trusts that and performs only the deduction, so we don't
        // end up with two different error strings for the same condition.
        // ----- Give deduction-decision model (§6.2a) -----
        // The single source of truth for "does this give move stock, and why." Both single and
        // bulk give resolve their decision here so the two paths can never diverge.
        //
        //   brandId 0/null (OHF)                      -> no deduct, reason OHF
        //   givenDate < stockPeriodStart (pre-reset)  -> no deduct, reason PRE_PERIOD
        //   givenDate == today (PKT)                  -> deduct,    reason NORMAL   (no prompt)
        //   givenDate < today, in-period, brand set   -> AMBIGUOUS: the caller must supply the
        //                                                operator's choice via reRecordHistorical:
        //                                                  null  -> caller must prompt first
        //                                                  false -> deduct, reason LATE_RECORDING
        //                                                  true  -> no deduct, reason HISTORICAL
        public sealed class GiveDecision
        {
            public bool ConsumesStock { get; init; }
            public string Reason { get; init; } = InventoryDecisionReason.Normal;
            public bool NeedsPrompt { get; init; }   // true = caller must ask before proceeding
        }

        public static GiveDecision ResolveGiveDecision(long? brandId, DateTime givenDate,
            DateTime? stockPeriodStart, bool? reRecordHistorical)
        {
            if (!brandId.HasValue || brandId.Value <= 0)
                return new GiveDecision { ConsumesStock = false, Reason = InventoryDecisionReason.Ohf };

            var giveDay = givenDate.Date;
            if (stockPeriodStart.HasValue && giveDay < stockPeriodStart.Value.Date)
                return new GiveDecision { ConsumesStock = false, Reason = InventoryDecisionReason.PrePeriod };

            if (giveDay == ClinicClock.TodayPkt())
                return new GiveDecision { ConsumesStock = true, Reason = InventoryDecisionReason.Normal };

            // Backdated, in-period, brand tagged — the one ambiguous case.
            if (reRecordHistorical == null)
                return new GiveDecision { ConsumesStock = false, Reason = InventoryDecisionReason.Historical, NeedsPrompt = true };

            return reRecordHistorical.Value
                ? new GiveDecision { ConsumesStock = false, Reason = InventoryDecisionReason.Historical }
                : new GiveDecision { ConsumesStock = true, Reason = InventoryDecisionReason.LateRecording };
        }

        // Legacy signature — a normal, stock-consuming give. Kept so existing callers compile
        // unchanged; delegates to the v2 overload with consumesStock=true / reason NORMAL.
        public void AdministerSync(BrandAmount ba, long clinicId, long scheduleId, DateTime eventDate, long? createdByPaId = null)
        {
            AdministerSync(ba, clinicId, scheduleId, eventDate, createdByPaId,
                consumesStock: true, decisionReason: InventoryDecisionReason.Normal, outStockId: out _);
        }

        // v2 give. `consumesStock` comes from the §6.2a decision model (resolved by the caller):
        //   true  → deduct 1 via FEFO, log Administer(delta=-1) rows, set BrandAmount.Count-1.
        //           If FEFO can't fully satisfy (stock shows 0 for a physically-given dose), we
        //           still record the give, drive stock to 0, and flag the brand NeedsReconcile —
        //           a real vaccination is always recordable (invariant §2.8 exception).
        //   false → OHF / pre-period / historical: no stock touched, log a zero-delta Administer
        //           row carrying the reason, so the dose is a recorded clinical fact only.
        // `outStockId` returns the batch the dose consumed (for Schedule.StockId), or null.
        public void AdministerSync(BrandAmount ba, long clinicId, long scheduleId, DateTime eventDate,
            long? createdByPaId, bool consumesStock, string? decisionReason, out int? outStockId)
        {
            long doctorId = ba.DoctorId;
            long brandId = ba.BrandId;
            outStockId = null;

            if (!consumesStock)
            {
                // Recorded clinical fact only — no stock movement. Zero-delta ledger row keeps
                // the decision auditable (reports sum QuantityDelta, so 0 is inert for stock math).
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Administer, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: decisionReason);
                return;
            }

            ba.Count -= 1;

            var fillStocks = FefoFillCandidates(brandId, clinicId).ToList();

            int remaining = 1;
            foreach (var src in fillStocks)
            {
                if (remaining <= 0) break;
                int deduct = Math.Min(src.Quantity, remaining);
                src.Quantity -= deduct;
                remaining -= deduct;
                outStockId = src.Id;

                Log(doctorId, clinicId, brandId, src.Id, src.BatchLot, src.Expiry, -deduct, src.StockAmount,
                    InventoryTransactionType.Administer, scheduleId, eventDate, createdByPaId,
                    consumesStock: true, decisionReason: decisionReason);

                // v2: never hard-delete a batch. A depleted row stays and is marked closed.
                if (src.Quantity == 0) src.IsClosed = true;
                _db.Entry(src).State = EntityState.Modified;
            }

            if (remaining > 0)
            {
                // FEFO couldn't fully satisfy — stock shows short for a dose that was physically
                // given. Do NOT block or roll back: keep Count at its clamped floor, flag the
                // brand for a physical recount, and log the shortfall on the ledger.
                if (ba.Count < 0) ba.Count = 0;
                ba.NeedsReconcile = true;
                if (outStockId == null)
                {
                    // Nothing to deduct from at all — record a consuming give with no batch.
                    Log(doctorId, clinicId, brandId, null, null, null, -1, ba.PurchasedAmt,
                        InventoryTransactionType.Administer, scheduleId, eventDate, createdByPaId,
                        consumesStock: true, decisionReason: decisionReason);
                }
            }
        }

        // v2 ungive — MIRRORS what the give did (§6.5). Reads the schedule's original Administer
        // ledger row: if that give consumed stock, restore +1 (to the exact StockId if known,
        // else FEFO-first, reopening a closed batch); if it did NOT (OHF / pre-period / historical),
        // restore nothing. Ungive never prompts and never invents a unit an OHF dose didn't take.
        // A pre-reset dose's Administer row is consumesStock=false, so this correctly restores 0 —
        // the doctor-only warning/guard for that case lives in the controller (§6.5a), not here.
        public void UnadministerSync(long doctorId, long clinicId, long brandId, long scheduleId,
            DateTime eventDate, long? createdByPaId = null)
        {
            // What did the original give do? Look at the most recent Administer row for this schedule.
            var giveRow = _db.InventoryTransactions
                .Where(t => t.SourceType == InventoryTransactionType.Administer && t.SourceId == scheduleId)
                .OrderByDescending(t => t.Id)
                .FirstOrDefault();

            // If the give recorded no stock movement, mirror it: no restore.
            if (giveRow != null && !giveRow.ConsumesStock)
            {
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: giveRow.DecisionReason);
                return;
            }

            var ba = _db.BrandAmounts.FirstOrDefault(x => x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);

            // v2: the give consumed stock (ConsumesStock=true) but never touched a real batch
            // (StockId=null) — an unbatched give. Restoring via FEFO here would donate a phantom
            // unit into an unrelated real batch that this give never took from. Restore the brand
            // counter only, unclaim any purchase that had marked this dose reconciled, and stop.
            if (giveRow != null && giveRow.StockId == null)
            {
                if (ba != null) ba.Count += 1;
                if (giveRow.ReconciledByTransactionId != null) giveRow.ReconciledByTransactionId = null;
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
                return;
            }

            if (ba != null) ba.Count += 1;

            // Prefer restoring to the exact batch the give consumed (giveRow.StockId).
            Stock? restoreStock = null;
            if (giveRow?.StockId != null)
                restoreStock = _db.Stocks.FirstOrDefault(s => s.Id == giveRow.StockId.Value);
            if (restoreStock == null)
                restoreStock = FefoRestoreCandidates(brandId, clinicId).FirstOrDefault();

            if (restoreStock != null)
            {
                restoreStock.Quantity += 1;
                if (restoreStock.IsClosed) restoreStock.IsClosed = false; // reopen an emptied batch
                _db.Entry(restoreStock).State = EntityState.Modified;
                Log(doctorId, clinicId, brandId, restoreStock.Id, restoreStock.BatchLot, restoreStock.Expiry,
                    1, restoreStock.StockAmount, InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
            }
            else
            {
                decimal? unitCost = ba != null ? ba.PurchasedAmt : (decimal?)null;
                Log(doctorId, clinicId, brandId, null, null, null, 1, unitCost,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
            }
        }

        // Bulk-ungive restore path (ScheduleController.UpdateBulkInjection). v2: mirrors the give
        // exactly like UnadministerSync — reads the original Administer row's ConsumesStock and
        // restores only if the give actually deducted; restores to the exact StockId when known.
        public void UnadministerBulkSync(BrandAmount ba, long clinicId, long brandId, long scheduleId, DateTime eventDate, long? createdByPaId = null)
        {
            var giveRow = _db.InventoryTransactions
                .Where(t => t.SourceType == InventoryTransactionType.Administer && t.SourceId == scheduleId)
                .OrderByDescending(t => t.Id)
                .FirstOrDefault();

            if (giveRow != null && !giveRow.ConsumesStock)
            {
                Log(ba.DoctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: giveRow.DecisionReason);
                return;
            }

            // v2: unbatched give (ConsumesStock=true, StockId=null) — same reasoning as
            // UnadministerSync above. Restore the brand counter only, unclaim any purchase
            // that had marked this dose reconciled, and stop before touching any Stock row.
            if (giveRow != null && giveRow.StockId == null)
            {
                ba.Count++;
                if (giveRow.ReconciledByTransactionId != null) giveRow.ReconciledByTransactionId = null;
                Log(ba.DoctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
                return;
            }

            ba.Count++;

            Stock? restoreStock = null;
            if (giveRow?.StockId != null)
                restoreStock = _db.Stocks.FirstOrDefault(s => s.Id == giveRow.StockId.Value);
            if (restoreStock == null)
                restoreStock = FefoRestoreCandidates(brandId, clinicId).FirstOrDefault();

            if (restoreStock != null)
            {
                restoreStock.Quantity++;
                if (restoreStock.IsClosed) restoreStock.IsClosed = false;
                _db.Entry(restoreStock).State = EntityState.Modified;
                Log(ba.DoctorId, clinicId, brandId, restoreStock.Id, restoreStock.BatchLot, restoreStock.Expiry,
                    1, restoreStock.StockAmount, InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
            }
            else
            {
                Log(ba.DoctorId, clinicId, brandId, null, null, null, 1, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId);
            }
        }

        private async Task<BrandAmount?> GetOrNoOpBrandAmount(long brandId, long doctorId, long clinicId)
        {
            return await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
        }
    }
}
