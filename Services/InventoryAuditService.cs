using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    public class AuditFinding
    {
        public string Code { get; set; } = "";
        public string Severity { get; set; } = "";     // HIGH / MEDIUM / INFO
        public long ClinicId { get; set; }
        public long BrandId { get; set; }
        public int? StockId { get; set; }
        public int Expected { get; set; }
        public int Actual { get; set; }
        public string Message { get; set; } = "";
    }

    public class AuditBatchRow
    {
        public long ClinicId { get; set; }
        public long BrandId { get; set; }
        public int StockId { get; set; }
        public string? BatchLot { get; set; }
        public DateTime? Expiry { get; set; }
        public int StoredQuantity { get; set; }
        public int LedgerQuantity { get; set; }
        public int Difference => StoredQuantity - LedgerQuantity;
        public int OriginalQuantity { get; set; }
        public bool IsClosed { get; set; }
    }

    public class AuditBrandRow
    {
        public long ClinicId { get; set; }
        public long BrandId { get; set; }
        public long DoctorId { get; set; }
        public int BrandAmount { get; set; }
        public int BatchSum { get; set; }
        public int LedgerSum { get; set; }
        public int PendingDoses { get; set; }
        public int BrandAmountVsBatches => BrandAmount - BatchSum;
        public int LedgerVsBatches => LedgerSum - BatchSum;
    }

    public class AuditReport
    {
        public List<AuditBrandRow> Brands { get; set; } = new List<AuditBrandRow>();
        public List<AuditBatchRow> Batches { get; set; } = new List<AuditBatchRow>();
        public List<AuditFinding> Findings { get; set; } = new List<AuditFinding>();
        public bool IsClean => Findings.All(f => f.Severity == "INFO");
        public Dictionary<string, int> CountsByCode => Findings.GroupBy(f => f.Code).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    // Deterministic, READ-ONLY reconciliation report:  stored stock vs ledger-derived stock vs
    // BrandAmount, per clinic / brand / batch, plus every anomaly class. It never changes data
    // and never hides a discrepancy: every correction is a separate, explicit, logged operation.
    public class InventoryAuditService
    {
        private readonly Context _db;
        public InventoryAuditService(Context db) { _db = db; }

        private static readonly InventoryTransactionType[] ReceiptTypes =
        {
            InventoryTransactionType.Purchase, InventoryTransactionType.TransferIn, InventoryTransactionType.OpeningBalance,
            InventoryTransactionType.AdjustIncrease, InventoryTransactionType.BillEdit, InventoryTransactionType.BillReverse,
            InventoryTransactionType.MigrationBackfill
        };

        public AuditReport Report(long? doctorId = null, long? clinicId = null)
        {
            var report = new AuditReport();
            var stocks = _db.Stocks.AsNoTracking().Include(s => s.Bill).ToList();
            var ledger = _db.InventoryTransactions.AsNoTracking().ToList();
            var bas = _db.BrandAmounts.AsNoTracking().ToList();
            var pending = _db.UnbatchedUses.AsNoTracking().Where(u => u.Status == UnbatchedUseStatus.Pending).ToList();

            long ClinicOf(Stock s) => s.ClinicId ?? s.Bill?.ClinicId ?? 0;
            var doctorClinics = doctorId.HasValue
                ? _db.Clinics.AsNoTracking().Where(c => c.DoctorId == doctorId.Value).Select(c => c.Id).ToHashSet()
                : null;
            bool InScope(long clinic) => (!clinicId.HasValue || clinic == clinicId.Value)
                                      && (doctorClinics == null || doctorClinics.Contains(clinic));

            var stockIds = new HashSet<int>(stocks.Select(s => s.Id));
            var ledgerByStock = ledger.Where(l => l.StockId.HasValue).GroupBy(l => l.StockId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // ---- per batch
            foreach (var s in stocks.OrderBy(s => ClinicOf(s)).ThenBy(s => s.BrandId).ThenBy(s => s.Id))
            {
                long clinic = ClinicOf(s);
                if (!InScope(clinic)) continue;
                ledgerByStock.TryGetValue(s.Id, out var rows);
                rows ??= new List<InventoryTransaction>();
                int ledgerQty = rows.Sum(r => r.QuantityDelta);
                report.Batches.Add(new AuditBatchRow
                {
                    ClinicId = clinic, BrandId = s.BrandId, StockId = s.Id, BatchLot = s.BatchLot, Expiry = s.Expiry,
                    StoredQuantity = s.Quantity, LedgerQuantity = ledgerQty, OriginalQuantity = s.OriginalQuantity, IsClosed = s.IsClosed
                });

                void Add(string code, string sev, string msg, int exp = 0, int act = 0) => report.Findings.Add(new AuditFinding
                { Code = code, Severity = sev, ClinicId = clinic, BrandId = s.BrandId, StockId = s.Id, Expected = exp, Actual = act, Message = msg });

                if (clinic == 0) Add("ORPHAN_BATCH", "HIGH", $"Batch {s.Id} ({s.BatchLot}) has no clinic: it has no ClinicId and no bill to infer one from.");
                if (s.Quantity < 0) Add("NEGATIVE_BATCH", "HIGH", $"Batch {s.Id} ({s.BatchLot}) is negative ({s.Quantity}).", 0, s.Quantity);
                if (s.IsClosed && s.Quantity > 0) Add("CLOSED_WITH_STOCK", "HIGH", $"Batch {s.Id} ({s.BatchLot}) is closed but holds {s.Quantity} unit(s): FEFO cannot use them.", 0, s.Quantity);
                if (!s.IsClosed && s.Quantity == 0) Add("OPEN_ZERO", "INFO", $"Batch {s.Id} ({s.BatchLot}) is open with zero quantity.");
                if (s.Quantity > 0 && rows.Count == 0) Add("STOCK_WITHOUT_LEDGER", "HIGH", $"Batch {s.Id} ({s.BatchLot}) holds {s.Quantity} unit(s) with no ledger history.", 0, s.Quantity);
                if (rows.Count > 0 && ledgerQty != s.Quantity)
                    Add("BATCH_LEDGER_MISMATCH", "HIGH", $"Batch {s.Id} ({s.BatchLot}): stored {s.Quantity} but its ledger rows sum to {ledgerQty}.", ledgerQty, s.Quantity);
                int receipts = rows.Where(r => ReceiptTypes.Contains(r.SourceType) && r.ConsumesStock).Sum(r => r.QuantityDelta);
                if (rows.Count > 0 && receipts != s.OriginalQuantity)
                    Add("ORIGINAL_QTY_MISMATCH", "INFO", $"Batch {s.Id} ({s.BatchLot}): OriginalQuantity {s.OriginalQuantity} vs receipt rows {receipts}.", receipts, s.OriginalQuantity);
            }

            // ---- ledger rows pointing at a batch that does not exist / legacy unbatched
            foreach (var l in ledger.Where(l => l.StockId.HasValue && !stockIds.Contains(l.StockId.Value)).OrderBy(l => l.Id))
            {
                if (!InScope(l.ClinicId)) continue;
                report.Findings.Add(new AuditFinding { Code = "LEDGER_WITHOUT_STOCK", Severity = "HIGH", ClinicId = l.ClinicId, BrandId = l.BrandId,
                    StockId = l.StockId, Actual = l.QuantityDelta, Message = $"Ledger row {l.Id} references batch {l.StockId} which no longer exists." });
            }
            foreach (var g in ledger.Where(l => !l.StockId.HasValue && l.QuantityDelta != 0 && InScope(l.ClinicId))
                                    .GroupBy(l => (l.ClinicId, l.BrandId)).OrderBy(g => g.Key.ClinicId).ThenBy(g => g.Key.BrandId))
            {
                report.Findings.Add(new AuditFinding { Code = "LEGACY_UNBATCHED_LEDGER", Severity = "MEDIUM", ClinicId = g.Key.ClinicId, BrandId = g.Key.BrandId,
                    Actual = g.Sum(x => x.QuantityDelta),
                    Message = $"{g.Count()} ledger row(s) with no batch move {g.Sum(x => x.QuantityDelta)} unit(s) (legacy unbatched gives/adjustments)." });
            }

            // ---- reversal rows must undo exactly what they reference
            var byId = ledger.ToDictionary(l => l.Id);
            var ledgerByBrandClinic = ledger.GroupBy(l => (l.BrandId, l.ClinicId)).ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityDelta));
            var batchSumByBrandClinic = stocks.GroupBy(x => (x.BrandId, ClinicOf(x))).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var pendingByBrandClinic = pending.GroupBy(u => (u.BrandId, u.ClinicId)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var r in ledger.Where(l => l.ReversesTransactionId.HasValue).OrderBy(l => l.Id))
            {
                if (!byId.TryGetValue(r.ReversesTransactionId!.Value, out var orig))
                {
                    report.Findings.Add(new AuditFinding { Code = "REVERSAL_ORPHAN", Severity = "HIGH", ClinicId = r.ClinicId, BrandId = r.BrandId,
                        Message = $"Reversal row {r.Id} references missing ledger row {r.ReversesTransactionId}." });
                }
                else if (r.QuantityDelta != -orig.QuantityDelta && r.ConsumesStock && r.SourceType != InventoryTransactionType.BillReverse)
                {
                    report.Findings.Add(new AuditFinding { Code = "REVERSAL_QUANTITY_MISMATCH", Severity = "HIGH", ClinicId = r.ClinicId, BrandId = r.BrandId,
                        StockId = r.StockId, Expected = -orig.QuantityDelta, Actual = r.QuantityDelta,
                        Message = $"Reversal row {r.Id} moves {r.QuantityDelta} but the row it reverses ({orig.Id}) moved {orig.QuantityDelta}." });
                }
            }

            // ---- transfers: every out needs an in (and the reverse), unless both were reversed
            var reversedIds = new HashSet<long>(ledger.Where(l => l.ReversesTransactionId.HasValue).Select(l => l.ReversesTransactionId!.Value));
            var outs = ledger.Where(l => l.SourceType == InventoryTransactionType.TransferOut && !reversedIds.Contains(l.Id)).Select(l => l.SourceId).ToHashSet();
            var ins = ledger.Where(l => l.SourceType == InventoryTransactionType.TransferIn && !reversedIds.Contains(l.Id)).Select(l => l.SourceId).ToHashSet();
            foreach (var id in outs.Except(ins).Concat(ins.Except(outs)).OrderBy(x => x))
            {
                var any = ledger.First(l => l.SourceId == id && (l.SourceType == InventoryTransactionType.TransferOut || l.SourceType == InventoryTransactionType.TransferIn));
                if (!InScope(any.ClinicId)) continue;
                report.Findings.Add(new AuditFinding { Code = "TRANSFER_UNPAIRED", Severity = "HIGH", ClinicId = any.ClinicId, BrandId = any.BrandId,
                    Message = $"Transfer {id} has only one side in the ledger (out={outs.Contains(id)}, in={ins.Contains(id)})." });
            }

            // ---- per brand & clinic
            var pairs = bas.Select(b => (b.ClinicId, b.BrandId, b.DoctorId))
                .Concat(report.Batches.Select(b => (b.ClinicId, b.BrandId, DoctorId: 0L))).Distinct().ToList();
            foreach (var ba in bas.OrderBy(b => b.ClinicId).ThenBy(b => b.BrandId))
            {
                if (!InScope(ba.ClinicId)) continue;
                if (doctorId.HasValue && ba.DoctorId != doctorId.Value) continue;
                int batchSum = batchSumByBrandClinic.TryGetValue((ba.BrandId, ba.ClinicId), out var bsv) ? bsv : 0;
                int ledgerSum = ledgerByBrandClinic.TryGetValue((ba.BrandId, ba.ClinicId), out var lsv) ? lsv : 0;
                int pend = pendingByBrandClinic.TryGetValue((ba.BrandId, ba.ClinicId), out var pv) ? pv : 0;
                report.Brands.Add(new AuditBrandRow { ClinicId = ba.ClinicId, BrandId = ba.BrandId, DoctorId = ba.DoctorId,
                    BrandAmount = ba.Quantity, BatchSum = batchSum, LedgerSum = ledgerSum, PendingDoses = pend });
                if (ba.Quantity < 0)
                    report.Findings.Add(new AuditFinding { Code = "NEGATIVE_BRANDAMOUNT", Severity = "HIGH", ClinicId = ba.ClinicId, BrandId = ba.BrandId, Expected = 0, Actual = ba.Quantity, Message = "BrandAmount is negative." });
                if (ba.Quantity != batchSum)
                    report.Findings.Add(new AuditFinding { Code = "BRAND_COUNTER_MISMATCH", Severity = "HIGH", ClinicId = ba.ClinicId, BrandId = ba.BrandId, Expected = batchSum, Actual = ba.Quantity,
                        Message = $"BrandAmount {ba.Quantity} but its batches sum to {batchSum}." });
                if (ledgerSum != batchSum)
                    report.Findings.Add(new AuditFinding { Code = "BRAND_LEDGER_MISMATCH", Severity = "HIGH", ClinicId = ba.ClinicId, BrandId = ba.BrandId, Expected = batchSum, Actual = ledgerSum,
                        Message = $"Ledger for this brand sums to {ledgerSum} but its batches sum to {batchSum}." });
                if (pend > 0)
                    report.Findings.Add(new AuditFinding { Code = "PENDING_DOSES", Severity = "INFO", ClinicId = ba.ClinicId, BrandId = ba.BrandId, Actual = pend,
                        Message = $"{pend} dose(s) given without batch details are waiting to be allocated (no stock effect)." });
            }
            foreach (var g in report.Batches.Where(b => b.StoredQuantity != 0 && InScope(b.ClinicId))
                                            .GroupBy(b => (b.ClinicId, b.BrandId)).OrderBy(g => g.Key.ClinicId).ThenBy(g => g.Key.BrandId))
            {
                if (!bas.Any(b => b.ClinicId == g.Key.ClinicId && b.BrandId == g.Key.BrandId))
                    report.Findings.Add(new AuditFinding { Code = "MISSING_BRANDAMOUNT", Severity = "HIGH", ClinicId = g.Key.ClinicId, BrandId = g.Key.BrandId,
                        Actual = g.Sum(b => b.StoredQuantity), Message = "Batches hold stock but no BrandAmount row exists for this brand at this clinic." });
            }

            report.Findings = report.Findings.OrderBy(f => f.Code).ThenBy(f => f.ClinicId).ThenBy(f => f.BrandId).ThenBy(f => f.StockId ?? 0).ToList();
            return report;
        }
    }
}
