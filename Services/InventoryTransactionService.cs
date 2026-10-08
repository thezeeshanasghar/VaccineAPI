using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    public class InventoryOperationResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = "";
        // The batch an operation created (AdjustIncrease / PostOpeningBalance), when there is one.
        public Stock? Batch { get; set; }
        public static InventoryOperationResult Ok() => new InventoryOperationResult { IsSuccess = true };
        public static InventoryOperationResult Fail(string message) => new InventoryOperationResult { IsSuccess = false, Message = message };
    }

    // Thrown when an operation would break a stock invariant (e.g. a batch going negative).
    // Callers run inside a transaction, so the exception rolls the whole operation back.
    public class InventoryInvariantException : Exception
    {
        public InventoryInvariantException(string message) : base(message) { }
    }

    // THE ONLY CODE ALLOWED TO CHANGE INVENTORY STATE.
    //
    //   Authoritative record : inventorytransactions (append-only ledger, one row per movement)
    //   Batch state          : Stock.Quantity / OriginalQuantity / IsClosed   -> written ONLY by ApplyDelta
    //   Projection           : BrandAmount.Quantity                           -> written ONLY by ProjectTouched
    //
    // Every public operation follows the same shape: validate -> ApplyDelta (per batch) + Log (one
    // ledger row per movement) -> ProjectTouched. A failed validation returns before any mutation.
    // Reversals post the exact opposite of the original ledger row on the SAME batch and reference
    // it (ReversesTransactionId); they never search for a batch by lot text.
    //
    // Callers still own the outer transaction and SaveChanges (unchanged contract). Call
    // SaveAndAssertAsync/SaveAndAssert instead of SaveChanges to also verify the invariants on
    // every batch this request touched.
    public class InventoryTransactionService
    {
        private readonly Context _db;
        public InventoryTransactionService(Context db) { _db = db; }

        // When true, a failed invariant check throws (tests, and prod once legacy drift is cleaned).
        // When false it is only reported through OnInvariantViolation.
        public static bool StrictInvariants { get; set; } = false;
        public static Action<string>? OnInvariantViolation { get; set; }

        // Expired-stock exclusion from FEFO. ON (owner decision 2026-10-08): an expired batch is never
        // given. Expiry is the LAST usable day, judged against the dose's given date.
        public static bool ExcludeExpiredFromFefo { get; set; } = true;

        private readonly List<Stock> _touchedStocks = new List<Stock>();
        private readonly Dictionary<(long brand, long doctor, long clinic), int> _touchedPairs =
            new Dictionary<(long, long, long), int>();

        // ------------------------------------------------------------------------------------
        // CORE PRIMITIVES
        // ------------------------------------------------------------------------------------

        // The single writer of Stock.Quantity / OriginalQuantity / IsClosed.
        //   receipt = true  : the movement is a receipt (purchase, transfer in, opening balance,
        //                     adjust increase, bill quantity edit and their reversals): it also
        //                     moves OriginalQuantity so OriginalQuantity - Quantity == consumed.
        //   IsClosed is derived here and nowhere else: closed exactly when Quantity == 0.
        // Only a NEGATIVE delta that would drive the batch below zero is refused, so a repair
        // (positive delta) can always heal a legacy negative batch.
        private void ApplyDelta(Stock s, int delta, bool receipt, long doctorId, long clinicId)
        {
            if (delta < 0 && s.Quantity + delta < 0)
                throw new InventoryInvariantException(
                    $"Batch {s.Id} ({s.BatchLot}) holds {s.Quantity} unit(s); cannot remove {-delta}.");
            s.Quantity += delta;
            if (receipt) s.OriginalQuantity += delta;
            // Units can never exceed what was purchased: a unit restored onto a batch whose purchased
            // quantity was re-based (split-consumed) raises it back, so "consumed" never goes negative.
            if (s.OriginalQuantity < s.Quantity) s.OriginalQuantity = s.Quantity;
            s.IsClosed = s.Quantity == 0;
            _db.MarkInventoryWrite(s);
            if (s.Id != 0 && _db.Entry(s).State == EntityState.Unchanged)
                _db.Entry(s).State = EntityState.Modified;
            if (!_touchedStocks.Contains(s)) _touchedStocks.Add(s);
            var key = (s.BrandId, doctorId, clinicId);
            _touchedPairs[key] = (_touchedPairs.TryGetValue(key, out var d) ? d : 0) + delta;
        }

        private static bool BelongsToClinic(Stock s, long clinicId) =>
            s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId);

        // BrandAmount is a projection: after an operation it is recomputed from the batch rows.
        // If the recomputed value differs from (old value + this operation's movement), the
        // difference is legacy drift being corrected; it is recorded as a zero-delta ledger note
        // so the correction is explainable.
        private void ProjectTouched()
        {
            foreach (var kv in _touchedPairs.ToList())
            {
                var (brandId, doctorId, clinicId) = kv.Key;
                var ba = _db.BrandAmounts.FirstOrDefault(x =>
                    x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
                // A counter must exist for every (brand, doctor, clinic) that holds stock: create it (at
                // the sum of the batches) rather than silently skipping the projection.
                if (ba == null) ba = BrandAmountProvisioner.Ensure(_db, brandId, doctorId, clinicId);

                var rows = _db.Stocks.Include(s => s.Bill)
                    .Where(s => s.BrandId == brandId
                        && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
                    .ToList();
                var added = _db.ChangeTracker.Entries<Stock>()
                    .Where(e => e.State == EntityState.Added && e.Entity.BrandId == brandId && BelongsToClinic(e.Entity, clinicId))
                    .Select(e => e.Entity).Where(e => !rows.Contains(e));
                int sum = rows.Sum(r => r.Quantity) + added.Sum(r => r.Quantity);

                int before = ba.Quantity;
                if (before != sum)
                {
                    ba.Quantity = sum;
                    _db.MarkInventoryWrite(ba);
                    if (sum - before != kv.Value)
                        Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                            InventoryTransactionType.MigrationCorrection, 0, ClinicClock.TodayPkt(),
                            consumesStock: false,
                            decisionReason: $"BA_REPROJECTED {before}->{sum} (movement {kv.Value})");
                }
            }
            _touchedPairs.Clear();
        }

        // Flush + verify. Use in place of SaveChanges. Absolute check per touched batch:
        // Stock.Quantity must equal the sum of that batch's ledger rows.
        public async Task SaveAndAssertAsync()
        {
            await _db.SaveChangesAsync();
            AssertTouchedBatches();
        }

        public void SaveAndAssert()
        {
            _db.SaveChanges();
            AssertTouchedBatches();
        }

        private void AssertTouchedBatches()
        {
            var ids = _touchedStocks.Where(s => s.Id != 0).Select(s => s.Id).Distinct().ToList();
            _touchedStocks.Clear();
            if (ids.Count == 0) return;
            var sums = _db.InventoryTransactions.Where(t => t.StockId != null && ids.Contains(t.StockId.Value))
                .GroupBy(t => t.StockId).Select(g => new { Id = g.Key, Sum = g.Sum(x => x.QuantityDelta) })
                .ToList().ToDictionary(x => x.Id!.Value, x => x.Sum);
            foreach (var s in _db.Stocks.AsNoTracking().Where(s => ids.Contains(s.Id)).ToList())
            {
                int ledger = sums.TryGetValue(s.Id, out var v) ? v : 0;
                if (ledger != s.Quantity)
                {
                    var msg = $"INV1 batch {s.Id} ({s.BatchLot}): Stock.Quantity={s.Quantity} but its ledger rows sum to {ledger}.";
                    OnInvariantViolation?.Invoke(msg);
                    if (StrictInvariants) throw new InventoryInvariantException(msg);
                }
            }
        }

        private InventoryTransaction Log(long doctorId, long clinicId, long brandId, int? stockId, string? batchLot,
            DateTime? expiry, int quantityDelta, decimal? unitCost, InventoryTransactionType sourceType,
            long sourceId, DateTime eventDate, long? createdByPaId = null,
            bool consumesStock = true, string? decisionReason = null, long? reversesTransactionId = null)
        {
            var row = new InventoryTransaction
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
                DecisionReason = decisionReason,
                ReversesTransactionId = reversesTransactionId
            };
            _db.InventoryTransactions.Add(row);
            _db.MarkInventoryWrite(row);
            return row;
        }

        // ------------------------------------------------------------------------------------
        // BATCH SELECTION (FEFO)
        // ------------------------------------------------------------------------------------

        // Usable = positive quantity (read from tracked state, so units restored earlier in the
        // same request are visible) and, when enabled, not expired at asOf. FEFO order: dated
        // batches first by earliest expiry, undated last, then oldest id.
        private List<Stock> UsableBatches(long brandId, long clinicId, DateTime? asOf = null)
        {
            var rows = _db.Stocks.Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Quantity > 0
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
                .ToList();
            foreach (var e in _db.ChangeTracker.Entries<Stock>().ToList())
            {
                if ((e.State == EntityState.Modified || e.State == EntityState.Added)
                    && e.Entity.BrandId == brandId && BelongsToClinic(e.Entity, clinicId) && !rows.Contains(e.Entity))
                    rows.Add(e.Entity);
            }
            var usable = rows.Where(s => s.Quantity > 0);
            if (ExcludeExpiredFromFefo && asOf.HasValue)
                usable = usable.Where(s => !s.Expiry.HasValue || s.Expiry.Value.Date >= asOf.Value.Date);
            return usable.OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id).ToList();
        }

        // Plan drawing `quantity` units of a lot: usable batches of that lot at the clinic, earliest
        // expiry first (an expiry given by the client narrows the choice when it matches). `planned`
        // carries what earlier lines of the SAME request already claimed from each batch, so two lines
        // on one batch can never oversell. Nothing is mutated here.
        public InventoryOperationResult PlanLotDraw(long brandId, long clinicId, string lot, DateTime? expiry,
            int quantity, Dictionary<int, int> planned, List<(Stock stock, int take)> draws, string verb)
        {
            var candidates = UsableBatches(brandId, clinicId).Where(s => s.BatchLot == lot).ToList();
            if (candidates.Count == 0)
                return InventoryOperationResult.Fail($"Batch '{lot}' not found or has no remaining stock");
            if (expiry.HasValue && candidates.Any(s => s.Expiry.HasValue && s.Expiry.Value.Date == expiry.Value.Date))
                candidates = candidates.Where(s => s.Expiry.HasValue && s.Expiry.Value.Date == expiry.Value.Date).ToList();

            int Free(Stock s) => s.Quantity - (planned.TryGetValue(s.Id, out var p) ? p : 0);
            int total = candidates.Sum(Free);
            if (quantity > total)
                return InventoryOperationResult.Fail($"Cannot {verb} more than available ({Math.Max(total, 0)}) in batch '{lot}'");

            int remaining = quantity;
            foreach (var s in candidates)
            {
                if (remaining == 0) break;
                int take = Math.Min(remaining, Free(s));
                if (take <= 0) continue;
                draws.Add((s, take));
                planned[s.Id] = (planned.TryGetValue(s.Id, out var q) ? q : 0) + take;
                remaining -= take;
            }
            return InventoryOperationResult.Ok();
        }

        // Read-only dry run for the give-time "no batch" prompt.
        public bool HasFillableBatch(long brandId, long clinicId, DateTime? asOf = null)
        {
            return UsableBatches(brandId, clinicId, asOf).Any();
        }

        // ------------------------------------------------------------------------------------
        // REVERSAL SUPPORT
        // ------------------------------------------------------------------------------------

        // Ledger rows of one source document that have not been reversed yet (newest first).
        private List<InventoryTransaction> UnreversedMovements(InventoryTransactionType type, long sourceId)
        {
            var rows = _db.InventoryTransactions
                .Where(t => t.SourceType == type && t.SourceId == sourceId && t.ReversesTransactionId == null)
                .OrderByDescending(t => t.Id).ToList();
            if (rows.Count == 0) return rows;
            var ids = rows.Select(r => (long?)r.Id).ToList();
            var reversed = _db.InventoryTransactions
                .Where(t => t.ReversesTransactionId != null && ids.Contains(t.ReversesTransactionId))
                .Select(t => t.ReversesTransactionId).ToList();
            return rows.Where(r => !reversed.Contains(r.Id)).ToList();
        }

        // ------------------------------------------------------------------------------------
        // RECEIPTS
        // ------------------------------------------------------------------------------------

        private Stock NewBatch(long brandId, long clinicId, int? billId, decimal stockAmount, string? lot, DateTime? expiry)
        {
            var stock = new Stock
            {
                BrandId = brandId,
                ClinicId = clinicId,       // always set: the clinic no longer has to be inferred from a bill
                BillId = billId,
                Quantity = 0,
                OriginalQuantity = 0,
                StockAmount = stockAmount,
                BatchLot = lot,
                Expiry = expiry,
                IsClosed = true
            };
            _db.Stocks.Add(stock);
            _db.MarkInventoryWrite(stock);
            return stock;
        }

        // ----- Purchase (BillController.Create) -----
        // A line only merges into a row already belonging to THIS bill (two identical lines in one
        // request); it never merges into another bill's row.
        public async Task<Stock> PostPurchaseLine(long doctorId, long clinicId, int billId, long brandId,
            int quantity, decimal stockAmount, string? batchLot, DateTime? expiry, DateTime billDate)
        {
            var stock = await _db.Stocks
                .Where(s => s.BrandId == brandId && s.BillId == billId && s.BatchLot == batchLot && s.Expiry == expiry)
                .FirstOrDefaultAsync();
            if (stock == null)
                stock = NewBatch(brandId, clinicId, billId, stockAmount, batchLot, expiry);

            ApplyDelta(stock, quantity, receipt: true, doctorId, clinicId);
            if (stock.Id == 0) await _db.SaveChangesAsync(); // need the Id for the ledger row

            Log(doctorId, clinicId, brandId, stock.Id, batchLot, expiry, quantity, stockAmount,
                InventoryTransactionType.Purchase, billId, billDate);
            ProjectTouched();
            return stock;
        }

        // ----- Bill edit (BillController.Update) -----
        // Lines are matched to existing batches (by StockId when the client sends it, else by
        // brand + lot + expiry). Editing never rewrites history:
        //   price only            -> cost updated, NO stock movement (audit row with delta 0)
        //   quantity changed      -> ONE signed BillEdit movement on the same batch; OriginalQuantity
        //                            follows the new purchased quantity; refused below consumed units
        //   lot / expiry changed  -> label change on the batch (audit row), no stock movement
        //   line removed          -> only if none of its units were used
        //   new line              -> a new batch via PostPurchaseLine
        public async Task<InventoryOperationResult> EditBillLines(Bill bill, IList<BillLineDTO> lines,
            decimal awtPercent, DateTime billDate)
        {
            // Identical lines in one request are ONE batch (same rule as Create): merge their quantities.
            lines = lines
                .GroupBy(l => l.StockId.HasValue ? "S" + l.StockId.Value : $"K{l.BrandId}|{l.BatchLot}|{l.Expiry:O}")
                .Select(g => new BillLineDTO
                {
                    StockId = g.First().StockId, BrandId = g.First().BrandId, BatchLot = g.First().BatchLot,
                    Expiry = g.First().Expiry, UnitPrice = g.First().UnitPrice, Quantity = g.Sum(x => x.Quantity)
                }).ToList();

            var pool = bill.Stocks.OrderBy(s => s.OriginalQuantity == 0 ? 1 : 0).ThenBy(s => s.Id).ToList();
            var plan = new List<(BillLineDTO line, Stock? stock)>();

            foreach (var line in lines.Where(l => l.StockId.HasValue))
            {
                var owned = pool.FirstOrDefault(s => s.Id == line.StockId!.Value);
                if (owned == null)
                    return InventoryOperationResult.Fail($"Batch {line.StockId} does not belong to this bill.");
                // A different vaccine on the same line is a brand change: the old batch is removed
                // (only if unused, checked below) and a new batch is created for the new brand.
                Stock? st = owned.BrandId == line.BrandId ? owned : null;
                if (st != null) pool.Remove(st);
                plan.Add((line, st));
            }
            foreach (var line in lines.Where(l => !l.StockId.HasValue))
            {
                var st = pool.FirstOrDefault(s => s.BrandId == line.BrandId && s.BatchLot == line.BatchLot && s.Expiry == line.Expiry);
                if (st != null) pool.Remove(st);
                plan.Add((line, st));
            }

            // ---- validate everything before touching anything
            foreach (var (line, st) in plan)
            {
                if (st == null) continue;
                int consumed = st.OriginalQuantity - st.Quantity;
                if (line.Quantity < consumed)
                    return InventoryOperationResult.Fail(
                        $"{consumed} unit(s) of batch {st.BatchLot} were already used; the quantity cannot be lowered below {consumed}.");
            }
            foreach (var st in pool.Where(s => s.OriginalQuantity > 0))
            {
                if (st.OriginalQuantity - st.Quantity > 0)
                    return InventoryOperationResult.Fail(
                        $"Line for batch {st.BatchLot} has {st.OriginalQuantity - st.Quantity} unit(s) already used and cannot be removed.");
            }

            // ---- apply
            foreach (var (line, st) in plan)
            {
                decimal stockAmount = Math.Round(line.UnitPrice * (1 + awtPercent / 100), 4);
                if (st == null)
                {
                    await PostPurchaseLine(bill.DoctorId, bill.ClinicId, bill.Id, line.BrandId, line.Quantity,
                        stockAmount, line.BatchLot, line.Expiry, billDate);
                    continue;
                }

                if (st.BatchLot != line.BatchLot || st.Expiry != line.Expiry)
                {
                    Log(bill.DoctorId, bill.ClinicId, st.BrandId, st.Id, line.BatchLot, line.Expiry, 0, st.StockAmount,
                        InventoryTransactionType.BillEdit, bill.Id, billDate, consumesStock: false,
                        decisionReason: $"RELABEL {st.BatchLot}/{st.Expiry:yyyy-MM-dd} -> {line.BatchLot}/{line.Expiry:yyyy-MM-dd}");
                    st.BatchLot = line.BatchLot;
                    st.Expiry = line.Expiry;
                    _db.MarkInventoryWrite(st);
                }
                if (Math.Round(st.StockAmount, 2) != Math.Round(stockAmount, 2))   // stored precision is 2 dp
                {
                    st.StockAmount = stockAmount;
                    _db.MarkInventoryWrite(st);
                    Log(bill.DoctorId, bill.ClinicId, st.BrandId, st.Id, st.BatchLot, st.Expiry, 0, stockAmount,
                        InventoryTransactionType.BillEdit, bill.Id, billDate, consumesStock: false,
                        decisionReason: "COST_EDIT");
                }
                int delta = line.Quantity - st.OriginalQuantity;
                if (delta != 0)
                {
                    ApplyDelta(st, delta, receipt: true, bill.DoctorId, bill.ClinicId);
                    Log(bill.DoctorId, bill.ClinicId, st.BrandId, st.Id, st.BatchLot, st.Expiry, delta, st.StockAmount,
                        InventoryTransactionType.BillEdit, bill.Id, billDate);
                }
                _db.MarkInventoryWrite(st);
                _db.Entry(st).State = EntityState.Modified;
            }
            foreach (var st in pool.Where(s => s.OriginalQuantity > 0 || s.Quantity > 0))
            {
                int remove = st.Quantity;
                ApplyDelta(st, -remove, receipt: true, bill.DoctorId, bill.ClinicId);
                Log(bill.DoctorId, bill.ClinicId, st.BrandId, st.Id, st.BatchLot, st.Expiry, -remove, st.StockAmount,
                    InventoryTransactionType.BillEdit, bill.Id, billDate);
            }
            ProjectTouched();
            return InventoryOperationResult.Ok();
        }

        // ----- Split-consumed (BillController.SplitConsumed) -----
        // Moves the cost history of the already-consumed units of a line onto a new (already paid)
        // bill. The new batch holds NO live units: it is created with the consumed quantity as a
        // receipt and immediately consumed, so Stock, ledger and BrandAmount are all unchanged in
        // total. The original batch keeps only its remaining units (OriginalQuantity re-based to
        // Quantity); its consumption ledger rows stay where they are.
        public async Task SplitConsumedLine(long doctorId, long clinicId, Stock stock, int consumed,
            int originalBillId, Bill newBill, DateTime billDate)
        {
            var newStock = NewBatch(stock.BrandId, clinicId, newBill.Id, stock.StockAmount, stock.BatchLot, stock.Expiry);
            ApplyDelta(newStock, consumed, receipt: true, doctorId, clinicId);
            ApplyDelta(newStock, -consumed, receipt: false, doctorId, clinicId);
            await _db.SaveChangesAsync(); // need newStock.Id for the ledger rows

            Log(doctorId, clinicId, stock.BrandId, newStock.Id, stock.BatchLot, stock.Expiry, consumed, stock.StockAmount,
                InventoryTransactionType.Purchase, newBill.Id, billDate, decisionReason: $"SPLIT_FROM_BILL_{originalBillId}");
            Log(doctorId, clinicId, stock.BrandId, newStock.Id, stock.BatchLot, stock.Expiry, -consumed, stock.StockAmount,
                InventoryTransactionType.SplitConsumed, newBill.Id, billDate, decisionReason: $"CONSUMED_ON_BILL_{originalBillId}");

            // Re-base the original line's purchased quantity to what is left (receipt bookkeeping only).
            stock.OriginalQuantity = stock.Quantity;
            _db.MarkInventoryWrite(stock);
            _db.Entry(stock).State = EntityState.Modified;
            if (!_touchedStocks.Contains(stock)) _touchedStocks.Add(stock);
            Log(doctorId, clinicId, stock.BrandId, stock.Id, stock.BatchLot, stock.Expiry, 0, stock.StockAmount,
                InventoryTransactionType.SplitConsumed, originalBillId, billDate, consumesStock: false,
                decisionReason: $"SPLIT_TO_BILL_{newBill.Id}:{consumed}");
            ProjectTouched();
        }

        // ----- §6.3a Batch correction (ScheduleController.CorrectBatch) -----
        public void LogBatchCorrection(long doctorId, long clinicId, long brandId, int? stockId,
            string? newBatchLot, DateTime? newExpiry, long scheduleId, DateTime eventDate, long? createdByPaId)
        {
            Log(doctorId, clinicId, brandId, stockId, newBatchLot, newExpiry, 0, null,
                InventoryTransactionType.BatchCorrection, scheduleId, eventDate, createdByPaId,
                consumesStock: false, decisionReason: null);
        }

        // ----- Bill reverse (BillController.Reverse) -----
        // Removes the batch's remaining units. Units already consumed keep their cost basis
        // (OriginalQuantity ends equal to the consumed count).
        public async Task ReverseBillStock(long doctorId, long clinicId, Stock stock, int billId, DateTime billDate)
        {
            int remaining = stock.Quantity;
            if (remaining > 0)
            {
                var purchaseRow = _db.InventoryTransactions
                    .Where(t => t.StockId == stock.Id && t.SourceType == InventoryTransactionType.Purchase && t.SourceId == billId)
                    .OrderBy(t => t.Id).FirstOrDefault();
                ApplyDelta(stock, -remaining, receipt: true, doctorId, clinicId);
                Log(doctorId, clinicId, stock.BrandId, stock.Id, stock.BatchLot, stock.Expiry,
                    -remaining, stock.StockAmount, InventoryTransactionType.BillReverse, billId, billDate,
                    reversesTransactionId: purchaseRow?.Id);
            }
            ProjectTouched();
            await Task.CompletedTask;
        }

        // ----- Adjust Stock: Increase (AdjustStockController.Create, Type == "Increase") -----
        // Always its own batch (never merged into a purchase bill's row, so a bill's payable is
        // never inflated by an adjustment) with Stock.ClinicId set; no fabricated anchor bill.
        public async Task<InventoryOperationResult> AdjustIncrease(long doctorId, long clinicId, long brandId,
            int quantity, decimal price, long adjustStockId, string? batchLot, DateTime? expiry, DateTime eventDate)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null)
                return InventoryOperationResult.Fail("Brand not configured at this clinic");
            if (quantity <= 0)
                return InventoryOperationResult.Fail("Quantity must be greater than zero");

            var stock = NewBatch(brandId, clinicId, null, price, batchLot, expiry);
            ApplyDelta(stock, quantity, receipt: true, doctorId, clinicId);
            await _db.SaveChangesAsync(); // need the Id for the ledger row

            Log(doctorId, clinicId, brandId, stock.Id, batchLot, expiry, quantity, price,
                InventoryTransactionType.AdjustIncrease, adjustStockId, eventDate);
            ProjectTouched();
            return new InventoryOperationResult { IsSuccess = true, Batch = stock };
        }

        // ----- §Opening Balance (StockController.PostOpeningBalance) -----
        public async Task<InventoryOperationResult> PostOpeningBalance(long doctorId, long clinicId,
            long brandId, int quantity, decimal unitCost, string? batchLot, DateTime? expiry, DateTime eventDate)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null)
                return InventoryOperationResult.Fail("Brand not configured at this clinic");
            if (quantity <= 0)
                return InventoryOperationResult.Fail("Quantity must be greater than zero");

            var stock = NewBatch(brandId, clinicId, null, unitCost, batchLot, expiry);
            ApplyDelta(stock, quantity, receipt: true, doctorId, clinicId);
            await _db.SaveChangesAsync(); // need the Id for the ledger row

            ba.NeedsReconcile = false;
            _db.MarkInventoryWrite(ba);
            Log(doctorId, clinicId, brandId, stock.Id, batchLot, expiry, quantity, unitCost,
                InventoryTransactionType.OpeningBalance, stock.Id, eventDate);
            ProjectTouched();
            return InventoryOperationResult.Ok();
        }

        // ------------------------------------------------------------------------------------
        // CONSUMPTION
        // ------------------------------------------------------------------------------------

        // ----- Adjust Stock: Loss (AdjustStockController.Create, Type == "Loss") -----
        // Draws the quantity from the batches of the named lot, earliest expiry first, one ledger
        // row per batch drawn.
        public async Task<InventoryOperationResult> AdjustLoss(long doctorId, long clinicId, long brandId,
            int quantity, long adjustStockId, string batchLot, DateTime eventDate)
        {
            var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
            if (ba == null || ba.Quantity == 0)
                return InventoryOperationResult.Fail("No stock available for this brand at this clinic");
            if (quantity <= 0)
                return InventoryOperationResult.Fail("Quantity must be greater than zero");

            var lotRows = UsableBatches(brandId, clinicId).Where(s => s.BatchLot == batchLot).ToList();
            if (lotRows.Count == 0)
                return InventoryOperationResult.Fail("Batch not found or has no remaining stock");
            int available = lotRows.Sum(s => s.Quantity);
            if (quantity > available)
                return InventoryOperationResult.Fail($"Cannot reduce more than available quantity ({available}) in this batch");

            int remaining = quantity;
            foreach (var row in lotRows)
            {
                if (remaining == 0) break;
                int take = Math.Min(remaining, row.Quantity);
                ApplyDelta(row, -take, receipt: false, doctorId, clinicId);
                remaining -= take;
                Log(doctorId, clinicId, brandId, row.Id, batchLot, row.Expiry, -take, row.StockAmount,
                    InventoryTransactionType.AdjustLoss, adjustStockId, eventDate);
            }
            ProjectTouched();
            return InventoryOperationResult.Ok();
        }

        // ----- Adjust Stock: reverse (AdjustStockController.Delete) -----
        // Reverses the original movement(s) on the same batch(es). An Increase can only be
        // reversed while its units are still on the shelf.
        public async Task<InventoryOperationResult> ReverseAdjustment(long doctorId, long clinicId, long brandId,
            int adjustment, string? batchLot, long adjustStockId)
        {
            var originals = adjustment > 0
                ? UnreversedMovements(InventoryTransactionType.AdjustIncrease, adjustStockId)
                : UnreversedMovements(InventoryTransactionType.AdjustLoss, adjustStockId)
                    .Concat(UnreversedMovements(InventoryTransactionType.Wastage, adjustStockId))
                    .Concat(UnreversedMovements(InventoryTransactionType.Expiry, adjustStockId)).ToList();
            var reverseType = InventoryTransactionType.AdjustReverse;

            if (originals.Count == 0)
            {
                // Legacy adjustment with no batch attached or no ledger row: nothing physical to undo.
                Log(doctorId, clinicId, brandId, null, batchLot, null, 0, null, reverseType, adjustStockId,
                    ClinicClock.TodayPkt(), consumesStock: false, decisionReason: "NO_ORIGINAL_MOVEMENT");
                await Task.CompletedTask;
                return InventoryOperationResult.Ok();
            }

            var plan = new List<(InventoryTransaction orig, Stock? stock)>();
            foreach (var o in originals)
            {
                Stock? st = o.StockId.HasValue ? _db.Stocks.FirstOrDefault(s => s.Id == o.StockId.Value) : null;
                if (st != null && o.QuantityDelta > 0 && st.Quantity < o.QuantityDelta)
                    return InventoryOperationResult.Fail(
                        $"{o.QuantityDelta - st.Quantity} of these {o.QuantityDelta} unit(s) have already been used, so the adjustment cannot be reversed.");
                plan.Add((o, st));
            }
            foreach (var (o, st) in plan)
            {
                if (st == null)
                {
                    Log(doctorId, clinicId, brandId, null, o.BatchLot, o.Expiry, 0, o.UnitCost, reverseType,
                        adjustStockId, ClinicClock.TodayPkt(), consumesStock: false, decisionReason: "BATCH_MISSING",
                        reversesTransactionId: o.Id);
                    continue;
                }
                ApplyDelta(st, -o.QuantityDelta, receipt: o.QuantityDelta > 0, doctorId, clinicId);
                Log(doctorId, clinicId, brandId, st.Id, st.BatchLot, st.Expiry, -o.QuantityDelta, st.StockAmount,
                    reverseType, adjustStockId, ClinicClock.TodayPkt(), reversesTransactionId: o.Id);
            }
            ProjectTouched();
            await Task.CompletedTask;
            return InventoryOperationResult.Ok();
        }

        // ----- Write-off (AdjustStockController.WriteOff): wastage / breakage / expired vials -----
        // Removes units from ONE specific batch (by id, never by lot text) as a typed movement, so
        // expired or spoiled stock leaves the shelf explicitly instead of sitting there forever.
        public InventoryOperationResult WriteOff(long doctorId, long clinicId, int stockId, int quantity,
            bool expired, long adjustStockId, DateTime eventDate)
        {
            if (quantity <= 0)
                return InventoryOperationResult.Fail("Quantity must be greater than zero");
            var batch = _db.Stocks.Include(s => s.Bill).FirstOrDefault(s => s.Id == stockId);
            if (batch == null)
                return InventoryOperationResult.Fail("Batch not found");
            if (!BelongsToClinic(batch, clinicId))
                return InventoryOperationResult.Fail("This batch is not at the selected clinic");
            if (quantity > batch.Quantity)
                return InventoryOperationResult.Fail($"Cannot write off more than the {batch.Quantity} unit(s) in this batch");

            ApplyDelta(batch, -quantity, receipt: false, doctorId, clinicId);
            Log(doctorId, clinicId, batch.BrandId, batch.Id, batch.BatchLot, batch.Expiry, -quantity, batch.StockAmount,
                expired ? InventoryTransactionType.Expiry : InventoryTransactionType.Wastage, adjustStockId, eventDate);
            ProjectTouched();
            return InventoryOperationResult.Ok();
        }

        // ----- Stock Transfer: out (source) + in (destination) (StockTransferController.Create) -----
        public async Task<Stock> TransferOut(long doctorId, long fromClinicId, Stock sourceStock,
            BrandAmount sourceBa, int quantity, long stockTransferId, DateTime eventDate)
        {
            ApplyDelta(sourceStock, -quantity, receipt: false, doctorId, fromClinicId);
            Log(doctorId, fromClinicId, sourceStock.BrandId, sourceStock.Id, sourceStock.BatchLot,
                sourceStock.Expiry, -quantity, sourceStock.StockAmount, InventoryTransactionType.TransferOut,
                stockTransferId, eventDate);
            ProjectTouched();
            await Task.CompletedTask;
            return sourceStock;
        }

        public async Task<Stock> TransferIn(long doctorId, long toClinicId, long brandId, int billId,
            int quantity, decimal unitPrice, string batchLot, DateTime? expiry, long stockTransferId,
            decimal sourceSalePrice, DateTime eventDate)
        {
            // Destination BrandAmount row is created if missing, seeded from the SOURCE clinic's sale
            // price (unchanged behaviour). Its quantity is a projection, never written by hand.
            BrandAmountProvisioner.Ensure(_db, brandId, doctorId, toClinicId, sourceSalePrice);

            var destStock = NewBatch(brandId, toClinicId, billId, unitPrice, batchLot, expiry);
            ApplyDelta(destStock, quantity, receipt: true, doctorId, toClinicId);
            await _db.SaveChangesAsync(); // need destStock.Id for the ledger row

            Log(doctorId, toClinicId, brandId, destStock.Id, batchLot, expiry, quantity, unitPrice,
                InventoryTransactionType.TransferIn, stockTransferId, eventDate);
            ProjectTouched();
            return destStock;
        }

        // ----- Stock Transfer: reverse (StockTransferController.Delete) -----
        // A transfer can only be undone while the transferred units are still whole at the
        // destination. If any were used or moved on, it is REFUSED with an explicit message; the
        // source is never handed units that no longer physically exist.
        public InventoryOperationResult CheckTransferReversible(long stockTransferId)
        {
            var inRow = UnreversedMovements(InventoryTransactionType.TransferIn, stockTransferId).FirstOrDefault();
            var outRow = UnreversedMovements(InventoryTransactionType.TransferOut, stockTransferId).FirstOrDefault();
            if (inRow == null || outRow == null || inRow.StockId == null || outRow.StockId == null)
                return InventoryOperationResult.Fail(
                    "This transfer has no inventory record that can be reversed automatically. Correct the stock with Adjust Stock.");
            var dest = _db.Stocks.FirstOrDefault(s => s.Id == inRow.StockId.Value);
            var source = _db.Stocks.FirstOrDefault(s => s.Id == outRow.StockId.Value);
            if (dest == null || source == null)
                return InventoryOperationResult.Fail("The stock batches of this transfer no longer exist. Correct the stock with Adjust Stock.");
            if (dest.Quantity < inRow.QuantityDelta)
                return InventoryOperationResult.Fail(
                    $"{inRow.QuantityDelta - dest.Quantity} of these {inRow.QuantityDelta} unit(s) were already used or moved at the destination clinic, " +
                    $"so the transfer cannot be reversed. {dest.Quantity} unit(s) remain there; transfer them back or record the difference with Adjust Stock.");
            return InventoryOperationResult.Ok();
        }

        public async Task<InventoryOperationResult> ReverseTransfer(long doctorId, long fromClinicId, long toClinicId,
            long brandId, long stockTransferId)
        {
            var check = CheckTransferReversible(stockTransferId);
            if (!check.IsSuccess) return check;

            var inRow = UnreversedMovements(InventoryTransactionType.TransferIn, stockTransferId).First();
            var outRow = UnreversedMovements(InventoryTransactionType.TransferOut, stockTransferId).First();
            var dest = _db.Stocks.First(s => s.Id == inRow.StockId!.Value);
            var source = _db.Stocks.First(s => s.Id == outRow.StockId!.Value);

            ApplyDelta(dest, -inRow.QuantityDelta, receipt: true, doctorId, toClinicId);
            Log(doctorId, toClinicId, brandId, dest.Id, dest.BatchLot, dest.Expiry, -inRow.QuantityDelta, dest.StockAmount,
                InventoryTransactionType.TransferReverse, stockTransferId, ClinicClock.TodayPkt(), reversesTransactionId: inRow.Id);

            ApplyDelta(source, -outRow.QuantityDelta, receipt: false, doctorId, fromClinicId);   // outRow delta is negative => restores
            Log(doctorId, fromClinicId, brandId, source.Id, source.BatchLot, source.Expiry, -outRow.QuantityDelta, source.StockAmount,
                InventoryTransactionType.TransferReverse, stockTransferId, ClinicClock.TodayPkt(), reversesTransactionId: outRow.Id);

            ProjectTouched();
            await Task.CompletedTask;
            return InventoryOperationResult.Ok();
        }

        // ----- Direct Sale (DirectSaleController.Create) -----
        public async Task<Stock> SellDirect(long doctorId, long clinicId, Stock sourceStock, BrandAmount sourceBa,
            int quantity, long directSaleId, DateTime eventDate)
        {
            ApplyDelta(sourceStock, -quantity, receipt: false, doctorId, clinicId);
            Log(doctorId, clinicId, sourceStock.BrandId, sourceStock.Id, sourceStock.BatchLot,
                sourceStock.Expiry, -quantity, sourceStock.StockAmount, InventoryTransactionType.DirectSale,
                directSaleId, eventDate);
            ProjectTouched();
            await Task.CompletedTask;
            return sourceStock;
        }

        // ----- Direct Sale: reverse (DirectSaleController.Delete) -----
        // Restores each unit to the batch it came from (recorded on the sale's ledger row). The
        // batch's usability follows from its quantity (IsClosed is derived in ApplyDelta).
        public async Task<InventoryOperationResult> ReverseDirectSale(long doctorId, long clinicId, long brandId,
            int quantity, string batchLot, decimal unitPrice, DateTime? expiry, long directSaleId)
        {
            var originals = UnreversedMovements(InventoryTransactionType.DirectSale, directSaleId);
            if (originals.Count == 0 || originals.Any(o => o.StockId == null))
                return InventoryOperationResult.Fail(
                    "This sale has no inventory record that can be reversed automatically. Correct the stock with Adjust Stock.");
            var plan = new List<(InventoryTransaction orig, Stock stock)>();
            foreach (var o in originals)
            {
                var st = _db.Stocks.FirstOrDefault(s => s.Id == o.StockId!.Value);
                if (st == null)
                    return InventoryOperationResult.Fail("The stock batch of this sale no longer exists. Correct the stock with Adjust Stock.");
                plan.Add((o, st));
            }
            foreach (var (o, st) in plan)
            {
                ApplyDelta(st, -o.QuantityDelta, receipt: false, doctorId, clinicId);   // original delta is negative
                Log(doctorId, clinicId, brandId, st.Id, st.BatchLot, st.Expiry, -o.QuantityDelta, st.StockAmount,
                    InventoryTransactionType.DirectSaleReverse, directSaleId, ClinicClock.TodayPkt(), reversesTransactionId: o.Id);
            }
            ProjectTouched();
            await Task.CompletedTask;
            return InventoryOperationResult.Ok();
        }

        // ------------------------------------------------------------------------------------
        // GIVE / UNGIVE
        // ------------------------------------------------------------------------------------
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

        // Legacy signature — a normal, stock-consuming give.
        public void AdministerSync(BrandAmount ba, long clinicId, long scheduleId, DateTime eventDate, long? createdByPaId = null)
        {
            AdministerSync(ba, clinicId, scheduleId, eventDate, createdByPaId,
                consumesStock: true, decisionReason: InventoryDecisionReason.Normal, outStockId: out _);
        }

        // Give. `consumesStock` comes from the §6.2a decision model (resolved by the caller):
        //   true  -> deduct 1 unit from the FEFO batch, one Administer ledger row on that batch.
        //            If NO usable batch exists the dose is still recorded (a real vaccination is
        //            always recordable) as a PENDING unbatched use: no ledger row, no stock, no
        //            BrandAmount change, so nothing is ever driven negative or left unexplained.
        //   false -> OHF / pre-period / historical: a zero-delta audit row only.
        // `outStockId` returns the batch the dose consumed, or null (pending / no movement).
        public void AdministerSync(BrandAmount ba, long clinicId, long scheduleId, DateTime eventDate,
            long? createdByPaId, bool consumesStock, string? decisionReason, out int? outStockId,
            string? preferredLot = null, DateTime? preferredExpiry = null)
        {
            long doctorId = ba.DoctorId;
            long brandId = ba.BrandId;
            outStockId = null;

            if (!consumesStock)
            {
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Administer, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: decisionReason);
                return;
            }

            var usable = UsableBatches(brandId, clinicId, eventDate);
            // Honour the batch the nurse chose (batch picker / typed lot) when it is a usable batch of
            // this brand at this clinic; its expiry narrows the choice among same-lot rows. Anything
            // that doesn't match (blank, stale, other brand's lot) falls back to FEFO — a give is
            // never rejected over a lot hint.
            Stock? src = null;
            var lotHint = (preferredLot ?? "").Trim();
            if (lotHint.Length > 0)
            {
                var sameLot = usable.Where(s => (s.BatchLot ?? "").Trim() == lotHint).ToList();
                if (preferredExpiry.HasValue)
                {
                    var sameExpiry = sameLot.Where(s => s.Expiry.HasValue && s.Expiry.Value.Date == preferredExpiry.Value.Date).ToList();
                    if (sameExpiry.Count > 0) sameLot = sameExpiry;
                }
                src = sameLot.FirstOrDefault();
            }
            src ??= usable.FirstOrDefault();
            if (src == null)
            {
                RecordPendingUse(doctorId, clinicId, brandId, scheduleId, eventDate, createdByPaId, decisionReason);
                ba.NeedsReconcile = true;   // "there are doses waiting for a batch"
                _db.MarkInventoryWrite(ba);
                return;
            }

            ApplyDelta(src, -1, receipt: false, doctorId, clinicId);
            outStockId = src.Id;
            Log(doctorId, clinicId, brandId, src.Id, src.BatchLot, src.Expiry, -1, src.StockAmount,
                InventoryTransactionType.Administer, scheduleId, eventDate, createdByPaId,
                consumesStock: true, decisionReason: decisionReason);
            ProjectTouched();
        }

        private void RecordPendingUse(long doctorId, long clinicId, long brandId, long scheduleId,
            DateTime givenDate, long? givenByPaId, string? decisionReason)
        {
            var live = _db.UnbatchedUses.FirstOrDefault(u => u.ActiveScheduleKey == scheduleId);
            if (live != null && live.ActiveScheduleKey != null) return;   // already recorded (duplicate submit)
            // A use voided earlier in this same request still holds the unique key in the database:
            // persist that change first so the new use can take the key (bulk brand correction).
            if (_db.ChangeTracker.Entries<UnbatchedUse>().Any(e => e.State == EntityState.Modified
                    && e.Entity.ScheduleId == scheduleId && e.Entity.ActiveScheduleKey == null))
                _db.SaveChanges();
            _db.UnbatchedUses.Add(new UnbatchedUse
            {
                ScheduleId = scheduleId,
                ActiveScheduleKey = scheduleId,
                DoctorId = doctorId,
                ClinicId = clinicId,
                BrandId = brandId,
                GivenDate = givenDate.Date,
                GivenByPaId = givenByPaId,
                DecisionReason = decisionReason,
                Status = UnbatchedUseStatus.Pending
            });
        }

        // The newest Administer row of a schedule that has not been reversed yet.
        private InventoryTransaction? LatestUnreversedGive(long scheduleId)
        {
            var gives = _db.InventoryTransactions
                .Where(t => t.SourceType == InventoryTransactionType.Administer && t.SourceId == scheduleId)
                .OrderByDescending(t => t.Id).ToList();
            if (gives.Count == 0) return null;
            var ids = gives.Select(g => (long?)g.Id).ToList();
            var reversed = _db.InventoryTransactions
                .Where(t => t.ReversesTransactionId != null && ids.Contains(t.ReversesTransactionId))
                .Select(t => t.ReversesTransactionId).ToList();
            return gives.FirstOrDefault(g => !reversed.Contains(g.Id));
        }

        // The clinic the live stock-consuming give of this dose was booked against (from its ledger
        // row), or null when there is none. The authoritative answer for "which clinic do I restore
        // to" — independent of the dose's lot/expiry text, which can be blank or wrong.
        public long? LiveGiveClinicId(long scheduleId)
        {
            var g = LatestUnreversedGive(scheduleId);
            return (g != null && g.ConsumesStock && g.ClinicId > 0) ? g.ClinicId : (long?)null;
        }

        // True when the dose still has a stock effect to undo (a live pending dose, or an un-reversed
        // stock-consuming give). Ungive uses this instead of the clinic's inventory switch, so turning
        // inventory off between a give and its ungive can never strand the consumed unit.
        public bool HasLiveGive(long scheduleId)
        {
            if (_db.UnbatchedUses.Any(u => u.ScheduleId == scheduleId && u.ActiveScheduleKey != null)) return true;
            var g = LatestUnreversedGive(scheduleId);
            return g != null && g.ConsumesStock;
        }

        // Ungive MIRRORS the give (§6.5) using the give's own ledger row: same batch, same clinic.
        //   pending unbatched use  -> voided, no stock effect
        //   no-stock decision      -> zero-delta audit row
        //   consumed a batch       -> +1 back onto THAT batch (its usability follows from Quantity)
        //   no recorded give       -> nothing to reverse (never invent a unit)
        private void UnadministerCore(long doctorId, long clinicId, long brandId, long scheduleId,
            DateTime eventDate, long? createdByPaId)
        {
            var use = _db.UnbatchedUses.FirstOrDefault(u => u.ActiveScheduleKey == scheduleId);
            var giveRow = LatestUnreversedGive(scheduleId);

            if (use != null && use.Status == UnbatchedUseStatus.Pending)
            {
                use.Status = UnbatchedUseStatus.Voided;
                use.ActiveScheduleKey = null;
                use.ResolvedAt = DateTime.UtcNow;
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: "PENDING_VOIDED");
                RefreshNeedsReconcile(use.DoctorId, new[] { (use.BrandId, use.ClinicId) });
                return;
            }
            if (use != null)   // claimed/other: release it below together with the batch restore
            {
                use.Status = UnbatchedUseStatus.Voided;
                use.ActiveScheduleKey = null;
                use.ResolvedAt = DateTime.UtcNow;
            }

            if (giveRow == null)
            {
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: "NO_ORIGINAL_MOVEMENT");
                return;
            }

            if (!giveRow.ConsumesStock)
            {
                Log(doctorId, clinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: giveRow.DecisionReason, reversesTransactionId: giveRow.Id);
                return;
            }

            if (giveRow.StockId == null)
            {
                // Legacy unbatched give (ledger -1, no batch). Offset the ledger; no batch to restore.
                giveRow.ReconciledByTransactionId = null;
                _db.MarkInventoryWrite(giveRow);
                Log(giveRow.DoctorId, giveRow.ClinicId, brandId, null, null, null, -giveRow.QuantityDelta, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    reversesTransactionId: giveRow.Id);
                return;
            }

            var batch = _db.Stocks.FirstOrDefault(s => s.Id == giveRow.StockId.Value);
            if (batch == null)
            {
                Log(giveRow.DoctorId, giveRow.ClinicId, brandId, null, null, null, 0, null,
                    InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                    consumesStock: false, decisionReason: "BATCH_MISSING", reversesTransactionId: giveRow.Id);
                return;
            }
            ApplyDelta(batch, -giveRow.QuantityDelta, receipt: false, giveRow.DoctorId, giveRow.ClinicId);
            Log(giveRow.DoctorId, giveRow.ClinicId, brandId, batch.Id, batch.BatchLot, batch.Expiry, -giveRow.QuantityDelta,
                batch.StockAmount, InventoryTransactionType.Unadminister, scheduleId, eventDate, createdByPaId,
                reversesTransactionId: giveRow.Id);
        }

        // ------------------------------------------------------------------------------------
        // PROJECTION REBUILD (the only sanctioned way to repair BrandAmount)
        // ------------------------------------------------------------------------------------

        // Recomputes BrandAmount.Quantity from the batch rows for one clinic (optionally one brand).
        // It never invents stock and never touches batches or ledger movements; every change is
        // recorded as a zero-delta ledger note (BA_REPROJECTED old->new) so the correction is
        // explainable. Returns the corrections made.
        public List<(long brandId, long doctorId, long clinicId, int was, int now)> RebuildBrandAmounts(long clinicId, long brandId = 0)
        {
            var result = new List<(long, long, long, int, int)>();
            var rows = _db.BrandAmounts.Where(b => b.ClinicId == clinicId && (brandId == 0 || b.BrandId == brandId)).ToList();
            var stocks = _db.Stocks.Include(s => s.Bill)
                .Where(s => s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId))
                .ToList();
            foreach (var ba in rows)
            {
                int sum = stocks.Where(s => s.BrandId == ba.BrandId).Sum(s => s.Quantity);
                if (ba.Quantity != sum)
                {
                    int was = ba.Quantity;
                    ba.Quantity = sum;
                    _db.MarkInventoryWrite(ba);
                    Log(ba.DoctorId, clinicId, ba.BrandId, null, null, null, 0, null,
                        InventoryTransactionType.MigrationCorrection, 0, ClinicClock.TodayPkt(), consumesStock: false,
                        decisionReason: $"BA_REPROJECTED {was}->{sum} (rebuild)");
                    result.Add((ba.BrandId, ba.DoctorId, clinicId, was, sum));
                }
            }
            RefreshNeedsReconcile2(rows);
            return result;
        }

        private void RefreshNeedsReconcile2(IEnumerable<BrandAmount> rows)
        {
            foreach (var ba in rows)
                RefreshNeedsReconcile(ba.DoctorId, new[] { (ba.BrandId, ba.ClinicId) });
        }

        // ------------------------------------------------------------------------------------
        // PENDING (UNBATCHED) DOSES: list / claim / dismiss
        // ------------------------------------------------------------------------------------

        // Pending uses for a brand at a clinic, oldest dose first. A use is only listed while its
        // dose is still recorded as given with that brand.
        public List<UnbatchedUse> PendingUses(long doctorId, long clinicId, long brandId)
        {
            var uses = _db.UnbatchedUses
                .Where(u => u.DoctorId == doctorId && u.ClinicId == clinicId && u.BrandId == brandId
                            && u.Status == UnbatchedUseStatus.Pending)
                .OrderBy(u => u.GivenDate).ThenBy(u => u.Id).ToList()
                .Where(u => u.Status == UnbatchedUseStatus.Pending)   // tracked instances carry in-request changes
                .ToList();
            var ids = uses.Select(u => u.ScheduleId).ToList();
            var live = _db.Schedules.Where(s => ids.Contains(s.Id) && s.IsDone && s.BrandId == brandId)
                .Select(s => s.Id).ToList();
            return uses.Where(u => live.Contains(u.ScheduleId)).ToList();
        }

        public int PendingCount(long doctorId, long clinicId, long brandId) =>
            PendingUses(doctorId, clinicId, brandId).Count;

        // ClaimUnbatched: allocate pending doses to ONE real batch through the normal movement
        // path. Every claimed dose is one Administer ledger row on that batch (dated at the dose's
        // own give date), the batch is reduced, the dose record gets the batch's lot / expiry /
        // cost / StockId, and the brand counter follows by projection. All-or-nothing: if any
        // requested use is not claimable, nothing changes.
        public InventoryOperationResult ClaimUnbatched(long doctorId, int stockId, IList<long> useIds)
        {
            if (useIds == null || useIds.Count == 0)
                return InventoryOperationResult.Fail("Select at least one dose to include.");
            var stock = _db.Stocks.Include(s => s.Bill).FirstOrDefault(s => s.Id == stockId);
            if (stock == null)
                return InventoryOperationResult.Fail("Batch not found.");
            long stockClinic = stock.ClinicId ?? stock.Bill?.ClinicId ?? 0;

            var uses = _db.UnbatchedUses.Where(u => useIds.Contains(u.Id)).OrderBy(u => u.GivenDate).ThenBy(u => u.Id).ToList();
            if (uses.Count != useIds.Distinct().Count())
                return InventoryOperationResult.Fail("One or more selected doses no longer exist.");

            foreach (var u in uses)
            {
                if (u.Status != UnbatchedUseStatus.Pending)
                    return InventoryOperationResult.Fail("One or more selected doses were already handled. Refresh and try again.");
                if (u.DoctorId != doctorId || u.BrandId != stock.BrandId || u.ClinicId != stockClinic)
                    return InventoryOperationResult.Fail("A selected dose belongs to a different vaccine or clinic than this batch.");
                var sch = _db.Schedules.FirstOrDefault(s => s.Id == u.ScheduleId);
                if (sch == null || !sch.IsDone || sch.BrandId != u.BrandId)
                    return InventoryOperationResult.Fail("A selected dose is no longer recorded as given with this vaccine. Refresh and try again.");
                if (ExcludeExpiredFromFefo && stock.Expiry.HasValue && stock.Expiry.Value.Date < u.GivenDate.Date)
                    return InventoryOperationResult.Fail("This batch had already expired on the date one of the doses was given.");
            }
            if (uses.Count > stock.Quantity)
                return InventoryOperationResult.Fail($"This batch holds {stock.Quantity} unit(s) but {uses.Count} dose(s) were selected.");

            foreach (var u in uses)
            {
                ApplyDelta(stock, -1, receipt: false, doctorId, u.ClinicId);
                Log(doctorId, u.ClinicId, u.BrandId, stock.Id, stock.BatchLot, stock.Expiry, -1, stock.StockAmount,
                    InventoryTransactionType.Administer, u.ScheduleId, u.GivenDate, u.GivenByPaId,
                    consumesStock: true, decisionReason: u.DecisionReason ?? InventoryDecisionReason.Normal);

                var sch = _db.Schedules.First(s => s.Id == u.ScheduleId);
                sch.StockId = stock.Id;
                sch.StockClinicId = u.ClinicId;
                sch.Lot = stock.BatchLot ?? "";
                sch.Expiry = stock.Expiry;
                sch.VaccineCost = stock.StockAmount;

                u.Status = UnbatchedUseStatus.Claimed;
                u.ClaimStockId = stock.Id;
                u.ResolvedAt = DateTime.UtcNow;
                // stays a live use (ActiveScheduleKey kept) so an ungive can find and void it
            }
            ProjectTouched();
            RefreshNeedsReconcile(doctorId, uses.Select(x => (x.BrandId, x.ClinicId)).Distinct());
            return InventoryOperationResult.Ok();
        }

        // Claim as many pending doses as the freshly received batch can cover (oldest first), from
        // the list the doctor chose. Used by the purchase / transfer-in / adjust-increase paths.
        // Returns how many doses were claimed.
        public int ClaimPendingForNewBatch(long doctorId, Stock batch, IList<long>? chosenUseIds)
        {
            if (chosenUseIds == null || chosenUseIds.Count == 0 || batch.Quantity <= 0) return 0;
            long clinic = batch.ClinicId ?? 0;
            var eligible = PendingUses(doctorId, clinic, batch.BrandId)
                .Where(u => chosenUseIds.Contains(u.Id)
                    && !(ExcludeExpiredFromFefo && batch.Expiry.HasValue && batch.Expiry.Value.Date < u.GivenDate.Date))
                .Take(batch.Quantity).Select(u => u.Id).ToList();
            if (eligible.Count == 0) return 0;
            var res = ClaimUnbatched(doctorId, batch.Id, eligible);
            if (!res.IsSuccess) throw new InventoryInvariantException(res.Message);
            return eligible.Count;
        }

        // Doctor decision: the dose did not come from clinic stock. It stays recorded as given.
        public InventoryOperationResult DismissUnbatched(long doctorId, long useId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return InventoryOperationResult.Fail("A reason is required.");
            var u = _db.UnbatchedUses.FirstOrDefault(x => x.Id == useId && x.DoctorId == doctorId);
            if (u == null) return InventoryOperationResult.Fail("Dose not found.");
            if (u.Status != UnbatchedUseStatus.Pending)
                return InventoryOperationResult.Fail("This dose was already handled.");
            u.Status = UnbatchedUseStatus.Dismissed;
            u.DismissReason = reason.Trim();
            u.ActiveScheduleKey = null;
            u.ResolvedAt = DateTime.UtcNow;
            Log(doctorId, u.ClinicId, u.BrandId, null, null, null, 0, null,
                InventoryTransactionType.Administer, u.ScheduleId, u.GivenDate, u.GivenByPaId,
                consumesStock: false, decisionReason: "DISMISSED: " + u.DismissReason);
            RefreshNeedsReconcile(doctorId, new[] { (u.BrandId, u.ClinicId) });
            return InventoryOperationResult.Ok();
        }

        // A patient (and their dose records) is being deleted: any pending unbatched use of those
        // doses is voided. Given doses that consumed a batch keep their ledger rows (the vial was
        // really used); only the "waiting for a batch" facts are closed.
        public void VoidPendingForSchedules(IEnumerable<long> scheduleIds)
        {
            var ids = scheduleIds.ToList();
            if (ids.Count == 0) return;
            var uses = _db.UnbatchedUses.Where(u => ids.Contains(u.ScheduleId) && u.ActiveScheduleKey != null).ToList();
            foreach (var u in uses)
            {
                u.Status = UnbatchedUseStatus.Voided;
                u.ActiveScheduleKey = null;
                u.ResolvedAt = DateTime.UtcNow;
            }
            RefreshNeedsReconcile2(_db.BrandAmounts.ToList().Where(b => uses.Any(u => u.BrandId == b.BrandId && u.ClinicId == b.ClinicId && u.DoctorId == b.DoctorId)).ToList());
        }

        // BrandAmount.NeedsReconcile means "doses of this brand are waiting for a batch".
        private void RefreshNeedsReconcile(long doctorId, IEnumerable<(long brandId, long clinicId)> pairs)
        {
            foreach (var (brandId, clinicId) in pairs)
            {
                var ba = _db.BrandAmounts.FirstOrDefault(x => x.BrandId == brandId && x.DoctorId == doctorId && x.ClinicId == clinicId);
                if (ba == null) continue;
                var live = _db.UnbatchedUses
                    .Where(u => u.DoctorId == doctorId && u.ClinicId == clinicId && u.BrandId == brandId
                                && u.Status == UnbatchedUseStatus.Pending)
                    .ToList()   // tracked instances carry in-memory changes (voided / claimed in this request)
                    .Any(u => u.Status == UnbatchedUseStatus.Pending && u.ActiveScheduleKey != null)
                    || _db.ChangeTracker.Entries<UnbatchedUse>().Any(e => e.State == EntityState.Added
                        && e.Entity.DoctorId == doctorId && e.Entity.ClinicId == clinicId && e.Entity.BrandId == brandId
                        && e.Entity.Status == UnbatchedUseStatus.Pending);
                ba.NeedsReconcile = live;
                _db.MarkInventoryWrite(ba);
            }
        }

        public void UnadministerSync(long doctorId, long clinicId, long brandId, long scheduleId,
            DateTime eventDate, long? createdByPaId = null)
        {
            UnadministerCore(doctorId, clinicId, brandId, scheduleId, eventDate, createdByPaId);
            ProjectTouched();
        }

        // Bulk-ungive restore path (ScheduleController.UpdateBulkInjection).
        public void UnadministerBulkSync(BrandAmount ba, long clinicId, long brandId, long scheduleId,
            DateTime eventDate, long? createdByPaId = null)
        {
            UnadministerCore(ba.DoctorId, clinicId, brandId, scheduleId, eventDate, createdByPaId);
            ProjectTouched();
        }
    }
}
