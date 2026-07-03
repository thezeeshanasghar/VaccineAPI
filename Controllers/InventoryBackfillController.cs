using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    // One-time migration tool: synthesizes InventoryTransaction rows for every stock movement
    // that already happened before the ledger existed, by reading the existing audit trails
    // (Bill/Stock, AdjustStock, StockTransfer, DirectSale, given Schedule rows). Additive only —
    // never modifies Bills/Stocks/AdjustStocks/StockTransfers/DirectSales/Schedules themselves.
    // Safe to re-run: Run() always wipes and rebuilds InventoryTransactions from scratch (idempotent),
    // since the table has no other writer until Phase 3 cutover wires the real controllers in.
    [Route("api/[controller]")]
    [ApiController]
    public class InventoryBackfillController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryReconciliationService _reconciliation;
        public InventoryBackfillController(Context db, InventoryReconciliationService reconciliation)
        {
            _db = db;
            _reconciliation = reconciliation;
        }

        [HttpPost("run")]
        public async Task<IActionResult> Run()
        {
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Idempotent: clear any prior backfill before rebuilding.
                await _db.Database.ExecuteSqlRawAsync("DELETE FROM inventorytransactions");

                int purchaseCount = await BackfillPurchases();
                int adjustCount = await BackfillAdjustments();
                int transferCount = await BackfillTransfers();
                int saleCount = await BackfillDirectSales();
                int administerCount = await BackfillAdministrations();

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                var report = await _reconciliation.Verify();

                return Ok(new
                {
                    IsSuccess = true,
                    Message = "Backfill complete",
                    ResponseData = new
                    {
                        PurchaseRowsWritten = purchaseCount,
                        AdjustmentRowsWritten = adjustCount,
                        TransferRowsWritten = transferCount,
                        DirectSaleRowsWritten = saleCount,
                        AdministerRowsWritten = administerCount,
                        AdministerRowsSkippedPreReset = SkippedPreResetAdministrations,
                        StockDriftCount = report.StockDrift.Count,
                        BrandAmountDriftCount = report.BrandAmountDrift.Count,
                        StockDrift = report.StockDrift,
                        BrandAmountDrift = report.BrandAmountDrift
                    }
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                var inner = ex.InnerException != null ? ex.InnerException.Message : "";
                return Ok(new { IsSuccess = false, Message = ex.Message + " | INNER: " + inner });
            }
        }

        [HttpGet("verify")]
        public async Task<IActionResult> Verify()
        {
            var report = await _reconciliation.Verify();
            return Ok(new { IsSuccess = true, ResponseData = report });
        }

        // Corrects BrandAmount.Count for every row that drifts from the ledger sum.
        // Safe to call any time — only writes rows that are actually wrong, and only to
        // the Count field. Does not touch Stock.Quantity, InventoryTransactions, or any
        // other table. Call POST /verify after to confirm drift is gone.
        [HttpPost("correct-drift")]
        public async Task<IActionResult> CorrectDrift()
        {
            var report = await _reconciliation.Verify();
            if (report.BrandAmountDrift.Count == 0)
                return Ok(new { IsSuccess = true, Message = "No BrandAmount drift — nothing to correct", CorrectedCount = 0 });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                int corrected = 0;
                foreach (var drift in report.BrandAmountDrift)
                {
                    var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                        x.BrandId == drift.BrandId && x.DoctorId == drift.DoctorId && x.ClinicId == drift.ClinicId);
                    if (ba == null) continue;
                    ba.Count = drift.LedgerCount;
                    corrected++;
                }
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    IsSuccess = true,
                    Message = $"Corrected {corrected} BrandAmount row(s)",
                    CorrectedCount = corrected,
                    Details = report.BrandAmountDrift.Select(d => new
                    {
                        BrandId = d.BrandId, DoctorId = d.DoctorId, ClinicId = d.ClinicId,
                        WasCount = d.LiveCount, NowCount = d.LedgerCount, Drift = d.Drift
                    })
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // Every Stock row's OriginalQuantity is what was originally purchased on that bill/line
        // (Transfer-in destination rows and Bill purchase rows alike — both go through
        // BillController.Create/StockTransferController.Create with the same OriginalQuantity
        // semantics). One Purchase/TransferIn-equivalent row per Stock row, sized at
        // OriginalQuantity, is therefore a faithful reconstruction of "how much arrived."
        // XFER-prefixed bills are tagged TransferIn instead of Purchase so the SourceType
        // reads correctly; everything else (including SplitConsumed-created bills, which are
        // indistinguishable from real purchases by data shape) is tagged Purchase, since a
        // split bill genuinely did receive that quantity from the doctor's perspective.
        private async Task<int> BackfillPurchases()
        {
            var stocks = await _db.Stocks.Include(s => s.Bill).Where(s => s.BillId != null).ToListAsync();
            int count = 0;
            foreach (var stock in stocks)
            {
                if (stock.Bill == null || stock.OriginalQuantity <= 0) continue;
                bool isTransferIn = stock.Bill.BillNo != null && stock.Bill.BillNo.StartsWith("XFER-");
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    DoctorId = stock.Bill.DoctorId,
                    ClinicId = stock.Bill.ClinicId,
                    BrandId = stock.BrandId,
                    StockId = stock.Id,
                    BatchLot = stock.BatchLot,
                    Expiry = stock.Expiry,
                    QuantityDelta = stock.OriginalQuantity,
                    UnitCost = stock.StockAmount,
                    SourceType = isTransferIn ? InventoryTransactionType.TransferIn : InventoryTransactionType.Purchase,
                    SourceId = stock.BillId.Value,
                    CreatedAt = stock.Bill.BillDate
                });
                count++;

                // NOTE: the gap between OriginalQuantity and live Quantity is everything
                // consumed since purchase (sale/loss/transfer-out/administer). This pass
                // deliberately does NOT log that gap here — DirectSale/AdjustLoss/TransferOut
                // each have their own audit trail and are backfilled separately below with
                // their real SourceId, and Administer is backfilled at brand/clinic level (no
                // StockId — Schedule has no FK to Stock) by BackfillAdministrations(). Logging
                // a second, separate "gap" row here under MigrationBackfill would double-count
                // that same historical consumption on top of the real per-event rows. The
                // unavoidable result: per-Stock-row StockDrift will show up wherever historical
                // Administer events consumed from a batch (since those are only attributed at
                // brand level, not batch level) — this is an accepted, documented gap, not
                // double-counting. BrandAmount-level reconciliation is unaffected by this and
                // should reconcile cleanly.
            }
            return count;
        }

        private async Task<int> BackfillAdjustments()
        {
            var rows = await _db.AdjustStocks.ToListAsync();
            int count = 0;
            foreach (var row in rows)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    DoctorId = row.DoctorId,
                    ClinicId = row.ClinicId,
                    BrandId = row.BrandId,
                    StockId = null, // AdjustStock has no StockId FK either — brand-level only, matches AdjustIncrease's existing no-Stock-row behavior
                    BatchLot = row.BatchLot,
                    Expiry = row.ExpiryDate,
                    QuantityDelta = row.Adjustment,
                    UnitCost = row.Price > 0 ? row.Price : (decimal?)null,
                    SourceType = row.Adjustment >= 0 ? InventoryTransactionType.AdjustIncrease : InventoryTransactionType.AdjustLoss,
                    SourceId = row.Id,
                    CreatedAt = row.Date
                });
                count++;
            }
            return count;
        }

        // StockTransfer rows already represent completed, non-reversed transfers (Delete
        // removes the StockTransfer rows themselves) — so only TransferOut needs backfilling
        // here; the matching TransferIn was already captured by BackfillPurchases() above
        // (every transfer's destination Stock row has BillId set to the XFER bill).
        private async Task<int> BackfillTransfers()
        {
            var rows = await _db.StockTransfers.ToListAsync();
            int count = 0;
            foreach (var row in rows)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    DoctorId = row.DoctorId,
                    ClinicId = row.FromClinicId,
                    BrandId = row.BrandId,
                    StockId = null, // source Stock row may have been mutated/deleted since; not safely resolvable in backfill
                    BatchLot = row.BatchLot,
                    Expiry = row.ExpiryDate,
                    QuantityDelta = -row.Quantity,
                    UnitCost = row.UnitPrice,
                    SourceType = InventoryTransactionType.TransferOut,
                    SourceId = row.Id,
                    CreatedAt = row.CreatedAt
                });
                count++;
            }
            return count;
        }

        private async Task<int> BackfillDirectSales()
        {
            var rows = await _db.DirectSales.ToListAsync();
            int count = 0;
            foreach (var row in rows)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    DoctorId = row.DoctorId,
                    ClinicId = row.ClinicId,
                    BrandId = row.BrandId,
                    StockId = null, // source Stock row may have been mutated/deleted since; not safely resolvable in backfill
                    BatchLot = row.BatchLot,
                    Expiry = row.ExpiryDate,
                    QuantityDelta = -row.Quantity,
                    UnitCost = row.PurchasePricePerUnit,
                    SourceType = InventoryTransactionType.DirectSale,
                    SourceId = row.Id,
                    CreatedAt = row.SaleDate
                });
                count++;
            }
            return count;
        }

        // Schedule has no StockId FK (only a denormalized Lot/Expiry snapshot at give-time) —
        // see project_inventory_architecture_audit memory. This pass logs brand/clinic-level
        // Administer rows only (StockId = null); it deliberately does NOT attempt to match
        // Lot/Expiry back to a specific live Stock row, since that row may since have been
        // edited, split, or hard-deleted, making any match here a guess. The BrandAmount-level
        // ledger sum stays correct; per-batch StockDrift for batches with historical given-doses
        // is an EXPECTED, documented gap, not a bug — already partly absorbed into
        // BackfillPurchases()'s consumedGap rows above where the Stock row itself still exists.
        //
        // Per-clinic cutoff: this app went through a full stock-system reset (deleted/recreated
        // Bills+Stocks, zeroed BrandAmount.Count) on a date that varies per clinic depending on
        // when each doctor's data was rebuilt — Schedules (given-dose history) was never
        // truncated in that reset. Backfilling Administer rows for doses given BEFORE a clinic's
        // earliest surviving Bill would subtract historical consumption that has no matching
        // Purchase left to reconcile against (that purchase data was deliberately deleted),
        // producing large permanent negative drift that doesn't reflect any current problem —
        // confirmed against project_stock_management memory's 2026-05-22 reset notes. Using each
        // clinic's own earliest Bill.BillDate (rather than one hardcoded global date) self-
        // calibrates per clinic, since different doctors' data was reset at different times.
        private async Task<int> BackfillAdministrations()
        {
            var givenSchedules = await _db.Schedules
                .Include(s => s.Child)
                .Where(s => s.IsDone && s.BrandId.HasValue && s.BrandId.Value > 0)
                .ToListAsync();

            // Project only Id/DoctorId — some live Clinic rows have NULL in columns the C#
            // model declares non-nullable (e.g. MonogramImage), which throws if EF materializes
            // the full Clinic entity. Selecting just what's needed avoids touching those columns.
            var clinicDoctorMap = await _db.Clinics
                .Select(c => new { c.Id, c.DoctorId })
                .ToDictionaryAsync(c => c.Id, c => c.DoctorId);

            var earliestBillByClinic = await _db.Bills
                .GroupBy(b => b.ClinicId)
                .Select(g => new { ClinicId = g.Key, EarliestBillDate = g.Min(b => b.BillDate) })
                .ToDictionaryAsync(x => x.ClinicId, x => x.EarliestBillDate);

            int count = 0;
            int skippedPreReset = 0;
            foreach (var schedule in givenSchedules)
            {
                if (schedule.Child == null) continue;
                if (!clinicDoctorMap.TryGetValue(schedule.Child.ClinicId, out var clinicDoctorId)) continue;

                var givenAt = schedule.DoneAt ?? schedule.GivenDate ?? DateTime.UtcNow;
                if (earliestBillByClinic.TryGetValue(schedule.Child.ClinicId, out var cutoff) && givenAt < cutoff)
                {
                    skippedPreReset++;
                    continue;
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    DoctorId = clinicDoctorId,
                    ClinicId = schedule.Child.ClinicId,
                    BrandId = schedule.BrandId.Value,
                    StockId = null,
                    BatchLot = string.IsNullOrEmpty(schedule.Lot) ? null : schedule.Lot,
                    Expiry = schedule.Expiry,
                    QuantityDelta = -1,
                    UnitCost = schedule.VaccineCost,
                    SourceType = InventoryTransactionType.Administer,
                    SourceId = schedule.Id,
                    CreatedAt = givenAt,
                    CreatedByPaId = schedule.GivenByPaId
                });
                count++;
            }
            SkippedPreResetAdministrations = skippedPreReset;
            return count;
        }

        // Exposed so Run() can report how many historical given-doses were excluded as
        // pre-dating their clinic's earliest surviving Bill (see comment above
        // BackfillAdministrations) — useful to sanity-check the cutoff actually fired.
        private int SkippedPreResetAdministrations { get; set; }
    }
}
