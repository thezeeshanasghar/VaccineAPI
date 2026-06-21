using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    public class StockDriftRow
    {
        public int StockId { get; set; }
        public long BrandId { get; set; }
        public string? BatchLot { get; set; }
        public int LiveQuantity { get; set; }
        public int LedgerQuantity { get; set; }
        public int Drift => LiveQuantity - LedgerQuantity;
    }

    public class BrandAmountDriftRow
    {
        public long BrandId { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public int LiveCount { get; set; }
        public int LedgerCount { get; set; }
        public int Drift => LiveCount - LedgerCount;
    }

    public class ReconciliationReport
    {
        public List<StockDriftRow> StockDrift { get; set; } = new List<StockDriftRow>();
        public List<BrandAmountDriftRow> BrandAmountDrift { get; set; } = new List<BrandAmountDriftRow>();
        public bool HasDrift => StockDrift.Count > 0 || BrandAmountDrift.Count > 0;
    }

    // Read-only safety net: recomputes what Stock.Quantity / BrandAmount.Count *should* be by
    // summing InventoryTransaction rows, and flags anywhere that disagrees with the live
    // cached value. Never writes anything — drift is reported, not auto-corrected, since the
    // live value is what every doctor-facing screen and invoice is already built on.
    public class InventoryReconciliationService
    {
        private readonly Context _db;
        public InventoryReconciliationService(Context db) { _db = db; }

        public async Task<ReconciliationReport> Verify()
        {
            var report = new ReconciliationReport();

            var stockSums = await _db.InventoryTransactions
                .Where(t => t.StockId != null)
                .GroupBy(t => t.StockId)
                .Select(g => new { StockId = g.Key.Value, Sum = g.Sum(t => t.QuantityDelta) })
                .ToListAsync();
            var stockSumLookup = stockSums.ToDictionary(x => x.StockId, x => x.Sum);

            var allStocks = await _db.Stocks.ToListAsync();
            foreach (var stock in allStocks)
            {
                int ledgerQty = stockSumLookup.TryGetValue(stock.Id, out var sum) ? sum : 0;
                if (ledgerQty != stock.Quantity)
                {
                    report.StockDrift.Add(new StockDriftRow
                    {
                        StockId = stock.Id,
                        BrandId = stock.BrandId,
                        BatchLot = stock.BatchLot,
                        LiveQuantity = stock.Quantity,
                        LedgerQuantity = ledgerQty
                    });
                }
            }

            var brandAmountSums = await _db.InventoryTransactions
                .GroupBy(t => new { t.BrandId, t.DoctorId, t.ClinicId })
                .Select(g => new { g.Key.BrandId, g.Key.DoctorId, g.Key.ClinicId, Sum = g.Sum(t => t.QuantityDelta) })
                .ToListAsync();
            var baSumLookup = brandAmountSums.ToDictionary(x => (x.BrandId, x.DoctorId, x.ClinicId), x => x.Sum);

            var allBrandAmounts = await _db.BrandAmounts.ToListAsync();
            foreach (var ba in allBrandAmounts)
            {
                int ledgerCount = baSumLookup.TryGetValue((ba.BrandId, ba.DoctorId, ba.ClinicId), out var sum) ? sum : 0;
                if (ledgerCount != ba.Count)
                {
                    report.BrandAmountDrift.Add(new BrandAmountDriftRow
                    {
                        BrandId = ba.BrandId,
                        DoctorId = ba.DoctorId,
                        ClinicId = ba.ClinicId,
                        LiveCount = ba.Count,
                        LedgerCount = ledgerCount
                    });
                }
            }

            return report;
        }
    }
}
