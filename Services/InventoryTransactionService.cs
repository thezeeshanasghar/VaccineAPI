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
            long sourceId, long? createdByPaId = null)
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
                CreatedByPaId = createdByPaId
            });
        }

        // ----- Purchase (BillController.Create) -----
        // Mirrors the existing same-bill-only consolidation: a line only merges into a Stock
        // row that already belongs to THIS bill (never another bill's row).
        public async Task<Stock> PostPurchaseLine(long doctorId, long clinicId, int billId, long brandId,
            int quantity, decimal stockAmount, string? batchLot, DateTime? expiry)
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
                InventoryTransactionType.Purchase, billId);

            return stock;
        }

        // ----- Bill edit reversal of old lines (BillController.Update) -----
        public async Task ReverseBillLine(long doctorId, long clinicId, Stock stock, int billId)
        {
            var ba = await GetOrNoOpBrandAmount(stock.BrandId, doctorId, clinicId);
            if (ba != null) ba.Count = Math.Max(0, ba.Count - stock.Quantity);

            Log(doctorId, clinicId, stock.BrandId, stock.Id, stock.BatchLot, stock.Expiry,
                -stock.Quantity, stock.StockAmount, InventoryTransactionType.BillEdit, billId);

            _db.Stocks.Remove(stock);
        }

        // ----- Split-consumed (BillController.SplitConsumed) -----
        // Shrinks the original line to its unconsumed remainder and logs the consumed portion
        // as moved to the new bill. No live Stock.Quantity/BrandAmount.Count change — the
        // consumption itself was already logged by whatever Administer/DirectSale/etc. call
        // originally deducted it; this only re-labels which bill the cost history belongs to.
        public void LogSplitConsumed(long doctorId, long clinicId, long brandId, int originalStockId,
            int newStockId, string? batchLot, DateTime? expiry, int consumedQuantity, decimal unitCost,
            int originalBillId, int newBillId)
        {
            Log(doctorId, clinicId, brandId, originalStockId, batchLot, expiry, -consumedQuantity,
                unitCost, InventoryTransactionType.SplitConsumed, originalBillId);
            Log(doctorId, clinicId, brandId, newStockId, batchLot, expiry, consumedQuantity,
                unitCost, InventoryTransactionType.SplitConsumed, newBillId);
        }

        // ----- Bill reverse (BillController.Reverse) -----
        public async Task ReverseBillStock(long doctorId, long clinicId, Stock stock, int billId)
        {
            var ba = await GetOrNoOpBrandAmount(stock.BrandId, doctorId, clinicId);
            if (ba != null) ba.Count = Math.Max(0, ba.Count - stock.Quantity);

            Log(doctorId, clinicId, stock.BrandId, stock.Id, stock.BatchLot, stock.Expiry,
                -stock.Quantity, stock.StockAmount, InventoryTransactionType.BillReverse, billId);

            _db.Stocks.Remove(stock);
        }

        // ----- Adjust Stock: Increase (AdjustStockController.Create, Type == "Increase") -----
        // Matches existing behavior exactly: brand-level only, no Stock row created, so
        // increase-adjusted stock has no batch/FEFO tracking (StockId stays null in the ledger).
        public async Task<InventoryOperationResult> AdjustIncrease(long doctorId, long clinicId, long brandId,
            int quantity, decimal price, long adjustStockId, string? batchLot, DateTime? expiry)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null || ba.Count == 0)
                return InventoryOperationResult.Fail("No stock available for this brand at this clinic");

            ba.Count += quantity;
            Log(doctorId, clinicId, brandId, null, batchLot, expiry, quantity, price,
                InventoryTransactionType.AdjustIncrease, adjustStockId);
            return InventoryOperationResult.Ok();
        }

        // ----- Adjust Stock: Loss (AdjustStockController.Create, Type == "Loss") -----
        public async Task<InventoryOperationResult> AdjustLoss(long doctorId, long clinicId, long brandId,
            int quantity, long adjustStockId, string batchLot)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null || ba.Count == 0)
                return InventoryOperationResult.Fail("No stock available for this brand at this clinic");

            var stockRow = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.BatchLot == batchLot && s.Quantity > 0)
                .FirstOrDefaultAsync();
            if (stockRow == null)
                return InventoryOperationResult.Fail("Batch not found or has no remaining stock");
            if (quantity > stockRow.Quantity)
                return InventoryOperationResult.Fail($"Cannot reduce more than available quantity ({stockRow.Quantity}) in this batch");

            stockRow.Quantity -= quantity;
            ba.Count = Math.Max(0, ba.Count - quantity);

            Log(doctorId, clinicId, brandId, stockRow.Id, batchLot, stockRow.Expiry, -quantity,
                stockRow.StockAmount, InventoryTransactionType.AdjustLoss, adjustStockId);
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
            if (adjustment < 0 && !string.IsNullOrEmpty(batchLot))
            {
                var stockRow = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.BatchLot == batchLot)
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
                InventoryTransactionType.AdjustReverse, adjustStockId);
        }

        // ----- Stock Transfer: out (source) + in (destination) (StockTransferController.Create) -----
        // sourceStock must already be validated/loaded by the caller (quantity check etc. stays
        // in the controller, same as today — this service performs the mutation only).
        public async Task<Stock> TransferOut(long doctorId, long fromClinicId, Stock sourceStock,
            BrandAmount sourceBa, int quantity, long stockTransferId)
        {
            sourceStock.Quantity -= quantity;
            sourceBa.Count = Math.Max(0, sourceBa.Count - quantity);

            Log(doctorId, fromClinicId, sourceStock.BrandId, sourceStock.Id, sourceStock.BatchLot,
                sourceStock.Expiry, -quantity, sourceStock.StockAmount, InventoryTransactionType.TransferOut,
                stockTransferId);

            if (sourceStock.Quantity == 0 && sourceStock.BillId == null)
                _db.Stocks.Remove(sourceStock);
            else
                _db.Entry(sourceStock).State = EntityState.Modified;

            return sourceStock;
        }

        public async Task<Stock> TransferIn(long doctorId, long toClinicId, long brandId, int billId,
            int quantity, decimal unitPrice, string batchLot, DateTime? expiry, long stockTransferId,
            decimal sourceSalePrice)
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
                InventoryTransactionType.TransferIn, stockTransferId);

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
                .Where(s => s.BrandId == brandId && s.BillId != null && s.BatchLot == batchLot && s.Bill.ClinicId == fromClinicId)
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
                InventoryTransactionType.TransferReverse, stockTransferId);
        }

        public async Task ReverseTransferIn(long doctorId, long toClinicId, long brandId, int quantity,
            long stockTransferId)
        {
            var destBa = await GetOrNoOpBrandAmount(brandId, doctorId, toClinicId);
            if (destBa != null) destBa.Count = Math.Max(0, destBa.Count - quantity);

            Log(doctorId, toClinicId, brandId, null, null, null, -quantity, null,
                InventoryTransactionType.TransferReverse, stockTransferId);
        }

        // ----- Direct Sale (DirectSaleController.Create) -----
        public async Task<Stock> SellDirect(long doctorId, long clinicId, Stock sourceStock, BrandAmount sourceBa,
            int quantity, long directSaleId)
        {
            sourceStock.Quantity -= quantity;
            sourceBa.Count = Math.Max(0, sourceBa.Count - quantity);

            Log(doctorId, clinicId, sourceStock.BrandId, sourceStock.Id, sourceStock.BatchLot,
                sourceStock.Expiry, -quantity, sourceStock.StockAmount, InventoryTransactionType.DirectSale,
                directSaleId);

            if (sourceStock.Quantity == 0 && sourceStock.BillId == null)
                _db.Stocks.Remove(sourceStock);
            else
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
                .Where(s => s.BrandId == brandId && s.BillId != null && s.BatchLot == batchLot && s.Bill.ClinicId == clinicId)
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
                InventoryTransactionType.DirectSaleReverse, directSaleId);
        }

        // ----- Vaccine Administration: give (ScheduleController, FEFO) -----
        // Returns false (with Message set) if there isn't enough stock; caller must treat this
        // exactly like today's `Count <= 0` / fill-remaining-not-reaching-zero checks.
        public async Task<InventoryOperationResult> Administer(long doctorId, long clinicId, long brandId,
            long scheduleId, long? createdByPaId = null)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(b => b.BrandId == brandId && b.DoctorId == doctorId && b.ClinicId == clinicId);
            if (ba == null)
                return InventoryOperationResult.Fail("Inventory row not found for brand");
            if (ba.Count <= 0)
                return InventoryOperationResult.Fail("Insufficient inventory for brand");

            ba.Count -= 1;

            var fillStocks = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity > 0)
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id)
                .ToListAsync();

            int remaining = 1;
            foreach (var src in fillStocks)
            {
                if (remaining <= 0) break;
                int deduct = Math.Min(src.Quantity, remaining);
                src.Quantity -= deduct;
                remaining -= deduct;

                Log(doctorId, clinicId, brandId, src.Id, src.BatchLot, src.Expiry, -deduct, src.StockAmount,
                    InventoryTransactionType.Administer, scheduleId, createdByPaId);

                if (src.Quantity == 0 && src.BillId == null) _db.Stocks.Remove(src);
                else _db.Entry(src).State = EntityState.Modified;
            }

            // Matches existing bulk-path behavior: if FEFO couldn't fully satisfy the deduction
            // (batches summed to less than 1 unit — stale Count vs. actual Stock rows), roll the
            // Count decrement back rather than leave it silently wrong.
            if (remaining > 0) ba.Count += 1;

            return InventoryOperationResult.Ok();
        }

        // ----- Vaccine Administration: ungive (ScheduleController rollback, FEFO restore) -----
        public async Task Unadminister(long doctorId, long clinicId, long brandId, long scheduleId,
            long? createdByPaId = null)
        {
            var ba = await GetOrNoOpBrandAmount(brandId, doctorId, clinicId);
            if (ba != null) ba.Count += 1;

            // s.Quantity >= 0 so a row at zero (fully consumed but not deleted) is still a
            // valid restore target — mirrors the exact FEFO restore order used on give.
            var restoreStock = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity >= 0)
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id)
                .FirstOrDefaultAsync();

            if (restoreStock != null)
            {
                restoreStock.Quantity += 1;
                _db.Entry(restoreStock).State = EntityState.Modified;
                Log(doctorId, clinicId, brandId, restoreStock.Id, restoreStock.BatchLot, restoreStock.Expiry,
                    1, restoreStock.StockAmount, InventoryTransactionType.Unadminister, scheduleId, createdByPaId);
            }
            else
            {
                // No live stock row exists at all — batch row was hard-deleted after hitting 0.
                // Previously this fabricated a new Stock row anchored to the clinic's most
                // recent unrelated posted bill (BillId = anchorBill.Id), misattributing the
                // restored unit's provenance. Removed per explicit decision: a posted bill is
                // immutable and has nothing to do with this restore. Matches
                // UnadministerBulkSync's existing behavior in this exact case — restore
                // BrandAmount.Count only, log the ledger entry with StockId = null.
                decimal? unitCost = ba != null ? ba.PurchasedAmt : (decimal?)null;
                Log(doctorId, clinicId, brandId, null, null, null, 1, unitCost,
                    InventoryTransactionType.Unadminister, scheduleId, createdByPaId);
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
        public void AdministerSync(BrandAmount ba, long clinicId, long scheduleId, long? createdByPaId = null)
        {
            long doctorId = ba.DoctorId;
            long brandId = ba.BrandId;
            ba.Count -= 1;

            var fillStocks = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity > 0)
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id)
                .ToList();

            int remaining = 1;
            foreach (var src in fillStocks)
            {
                if (remaining <= 0) break;
                int deduct = Math.Min(src.Quantity, remaining);
                src.Quantity -= deduct;
                remaining -= deduct;

                Log(doctorId, clinicId, brandId, src.Id, src.BatchLot, src.Expiry, -deduct, src.StockAmount,
                    InventoryTransactionType.Administer, scheduleId, createdByPaId);

                if (src.Quantity == 0 && src.BillId == null) _db.Stocks.Remove(src);
                else _db.Entry(src).State = EntityState.Modified;
            }

            if (remaining > 0) ba.Count += 1;
        }

        public void UnadministerSync(long doctorId, long clinicId, long brandId, long scheduleId,
            long? createdByPaId = null)
        {
            var ba = _db.BrandAmounts.FirstOrDefault(x => x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba != null) ba.Count += 1;

            var restoreStock = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity >= 0)
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id)
                .FirstOrDefault();

            if (restoreStock != null)
            {
                restoreStock.Quantity += 1;
                _db.Entry(restoreStock).State = EntityState.Modified;
                Log(doctorId, clinicId, brandId, restoreStock.Id, restoreStock.BatchLot, restoreStock.Expiry,
                    1, restoreStock.StockAmount, InventoryTransactionType.Unadminister, scheduleId, createdByPaId);
            }
            else
            {
                // No live stock row exists at all — batch row was hard-deleted after hitting 0.
                // Previously this fabricated a new Stock row anchored to the clinic's most
                // recent unrelated posted bill (BillId = anchorBill.Id), misattributing the
                // restored unit's provenance. Removed per explicit decision: a posted bill is
                // immutable and has nothing to do with this restore. Matches
                // UnadministerBulkSync's existing behavior in this exact case — restore
                // BrandAmount.Count only, log the ledger entry with StockId = null.
                decimal? unitCost = ba != null ? ba.PurchasedAmt : (decimal?)null;
                Log(doctorId, clinicId, brandId, null, null, null, 1, unitCost,
                    InventoryTransactionType.Unadminister, scheduleId, createdByPaId);
            }
        }

        // Bulk-ungive restore path (ScheduleController.UpdateBulkInjection). Same no-live-row
        // behavior as UnadministerSync above — restores BrandAmount.Count only, no Stock row
        // fabricated, ledger logs the event with StockId = null.
        public void UnadministerBulkSync(BrandAmount ba, long clinicId, long brandId, long scheduleId, long? createdByPaId = null)
        {
            ba.Count++;

            var restoreStock = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity >= 0)
                .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id)
                .FirstOrDefault();

            if (restoreStock != null)
            {
                restoreStock.Quantity++;
                _db.Entry(restoreStock).State = EntityState.Modified;
                Log(ba.DoctorId, clinicId, brandId, restoreStock.Id, restoreStock.BatchLot, restoreStock.Expiry,
                    1, restoreStock.StockAmount, InventoryTransactionType.Unadminister, scheduleId, createdByPaId);
            }
            else
            {
                Log(ba.DoctorId, clinicId, brandId, null, null, null, 1, null,
                    InventoryTransactionType.Unadminister, scheduleId, createdByPaId);
            }
        }

        private async Task<BrandAmount?> GetOrNoOpBrandAmount(long brandId, long doctorId, long clinicId)
        {
            return await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
        }
    }
}
