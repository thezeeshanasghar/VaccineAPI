using Microsoft.EntityFrameworkCore;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>The reconciliation report must SEE every kind of discrepancy and never hide one.</summary>
public class AuditAndRebuildTests
{
    private static StockWorld WorldWithStock()
    {
        var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 10, 10));
        return w;
    }

    private static AuditReport Audit(StockWorld w) { using var db = w.NewContext(); return new InventoryAuditService(db).Report(); }
    private static void Sql(StockWorld w, string sql) { using var db = w.NewContext(); db.Database.ExecuteSqlRaw(sql); }

    [Fact]
    public void CleanWorld_ReportsNoProblems()
    {
        using var w = WorldWithStock();
        w.GiveDose1();
        var r = Audit(w);
        Assert.True(r.IsClean, string.Join("; ", r.Findings.Where(f => f.Severity != "INFO").Select(f => f.Code + ":" + f.Message)));
        Assert.Contains(r.Batches, b => b.StoredQuantity == 9 && b.LedgerQuantity == 9);
        var brand = r.Brands.Single(b => b.ClinicId == w.ClinicA);
        Assert.Equal((9, 9, 9), (brand.BrandAmount, brand.BatchSum, brand.LedgerSum));
    }

    [Fact]
    public void TamperedBatchQuantity_IsFlaggedAtBatchAndBrandLevel()
    {
        using var w = WorldWithStock();
        Sql(w, "UPDATE stocks SET Quantity = 25");
        var codes = Audit(w).Findings.Select(f => f.Code).ToList();
        Assert.Contains("BATCH_LEDGER_MISMATCH", codes);
        Assert.Contains("BRAND_COUNTER_MISMATCH", codes);
        Assert.Contains("BRAND_LEDGER_MISMATCH", codes);
    }

    [Fact]
    public void TamperedCounter_IsFlagged_AndRebuildRepairsItWithALedgerNote()
    {
        using var w = WorldWithStock();
        Sql(w, "UPDATE brandamounts SET Quantity = 99 WHERE ClinicId = " + w.ClinicA);
        Assert.Contains(Audit(w).Findings, f => f.Code == "BRAND_COUNTER_MISMATCH" && f.Expected == 10 && f.Actual == 99);

        using (var db = w.NewContext())
        {
            var svc = new InventoryTransactionService(db);
            var fixedRows = svc.RebuildBrandAmounts(w.ClinicA);
            Assert.Single(fixedRows);
            svc.SaveAndAssert();
        }
        Assert.Equal(10, w.BaQty(w.ClinicA));
        using var check = w.NewContext();
        Assert.Contains(check.InventoryTransactions, t => t.DecisionReason != null && t.DecisionReason.StartsWith("BA_REPROJECTED 99->10"));
        Assert.DoesNotContain(Audit(w).Findings, f => f.Code == "BRAND_COUNTER_MISMATCH");
    }

    [Fact]
    public void ClosedBatchWithStock_NegativeBatch_OrphanBatch_AreFlagged()
    {
        using var w = WorldWithStock();
        Sql(w, "UPDATE stocks SET IsClosed = 1");
        Assert.Contains(Audit(w).Findings, f => f.Code == "CLOSED_WITH_STOCK");

        Sql(w, "UPDATE stocks SET IsClosed = 0, Quantity = -3");
        Assert.Contains(Audit(w).Findings, f => f.Code == "NEGATIVE_BATCH");

        Sql(w, "UPDATE stocks SET Quantity = 10, ClinicId = NULL, BillId = NULL");
        Assert.Contains(Audit(w).Findings, f => f.Code == "ORPHAN_BATCH");
    }

    [Fact]
    public void StockWithoutLedger_LedgerWithoutStock_LegacyUnbatched_AreFlagged()
    {
        using var w = WorldWithStock();
        Sql(w, "DELETE FROM inventorytransactions");
        Assert.Contains(Audit(w).Findings, f => f.Code == "STOCK_WITHOUT_LEDGER");

        using var w2 = WorldWithStock();
        Sql(w2, "UPDATE inventorytransactions SET StockId = 9999");
        Assert.Contains(Audit(w2).Findings, f => f.Code == "LEDGER_WITHOUT_STOCK");

        using var w3 = WorldWithStock();
        Sql(w3, $"INSERT INTO inventorytransactions (DoctorId, ClinicId, BrandId, StockId, QuantityDelta, SourceType, SourceId, CreatedAt, EventDate, ConsumesStock) VALUES ({w3.DoctorId},{w3.ClinicA},{w3.BrandId},NULL,-1,12,1,'2026-01-01','2026-01-01',1)");
        Assert.Contains(Audit(w3).Findings, f => f.Code == "LEGACY_UNBATCHED_LEDGER");
    }

    [Fact]
    public void ReversalWithWrongQuantity_AndUnpairedTransfer_AreFlagged()
    {
        using var w = WorldWithStock();
        var (o, _) = (w.Sell(w.ClinicA, Kit.SaleItem(w.BrandId, "L1", 4)), 0);
        Assert.True(o.IsSuccess);
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        Sql(w, "UPDATE inventorytransactions SET QuantityDelta = 3 WHERE ReversesTransactionId IS NOT NULL");
        Assert.Contains(Audit(w).Findings, f => f.Code == "REVERSAL_QUANTITY_MISMATCH");

        using var w2 = WorldWithStock();
        Assert.True(w2.Transfer(w2.ClinicA, w2.ClinicB, Kit.XItem(w2.BrandId, "L1", 5)).IsSuccess);
        Sql(w2, "DELETE FROM inventorytransactions WHERE SourceType = 8");   // drop the TransferIn side
        Assert.Contains(Audit(w2).Findings, f => f.Code == "TRANSFER_UNPAIRED");
    }

    [Fact]
    public void PendingDoses_AreReportedAsAnExpectedNamedDifference_NotDrift()
    {
        using var w = new StockWorld();
        var (c, s) = w.AddChild();
        Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess);
        var r = Audit(w);
        Assert.True(r.IsClean);                                   // pending is INFO, not an error
        Assert.Contains(r.Findings, f => f.Code == "PENDING_DOSES" && f.Actual == 1);
        Assert.Equal(1, r.Brands.Single(b => b.ClinicId == w.ClinicA).PendingDoses);
    }

    [Fact]
    public void ReconcileEndpoint_RebuildsFromBatches_NeverInventsStock()
    {
        using var w = WorldWithStock();
        Sql(w, "UPDATE brandamounts SET Quantity = 77 WHERE ClinicId = " + w.ClinicA);
        using var db = w.NewContext();
        var ctl = new StockController(db, new InventoryTransactionService(db));
        var res = ctl.Reconcile(w.ClinicA, 0).GetAwaiter().GetResult();
        Assert.True(Result.Of(res).IsSuccess, Result.Of(res).Message);
        Assert.Equal(10, w.BaQty(w.ClinicA));
        Assert.Equal(10, w.Shelf(w.ClinicA));                     // batches untouched
        w.Clean("after reconcile");
    }

    [Fact]
    public void RetiredBackfillRun_DoesNotWipeTheLedger()
    {
        using var w = WorldWithStock();
        int before; using (var db = w.NewContext()) before = db.InventoryTransactions.Count();
        using var db2 = w.NewContext();
        var res = new InventoryBackfillController(db2, new InventoryTransactionService(db2)).Run();
        Assert.False(Result.Of(res).IsSuccess);
        using var db3 = w.NewContext();
        Assert.Equal(before, db3.InventoryTransactions.Count());
    }
}
