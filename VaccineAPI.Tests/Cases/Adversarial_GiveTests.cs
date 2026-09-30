using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

public class Adversarial_GiveTests
{
    static Schedule Sch(StockWorld w, long id) { using var db = w.NewContext(); return db.Schedules.AsNoTracking().Single(s => s.Id == id); }
    static List<UnbatchedUse> Uses(StockWorld w) { using var db = w.NewContext(); return db.UnbatchedUses.AsNoTracking().OrderBy(u => u.Id).ToList(); }
    static bool Needs(StockWorld w, long clinic) { using var db = w.NewContext(); return db.BrandAmounts.Single(b => b.BrandId == w.BrandId && b.ClinicId == clinic).NeedsReconcile; }

    // ------------------------------------------------------------ cycle
    [Fact]
    public void GiveUngiveRegive_ExactLedgerAndBatchState()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var g1 = w.GiveDose1(); Assert.True(g1.IsSuccess, g1.Message);
        Assert.Equal(2, w.Stocks().Single().Quantity); Assert.Equal(2, w.BaQty(w.ClinicA));
        Assert.True(Sch(w, w.Schedule1Id).IsDone);
        w.Clean("give");
        var u = w.UngiveDose1(); Assert.True(u.IsSuccess, u.Message);
        Assert.Equal(3, w.Stocks().Single().Quantity); Assert.Equal(3, w.BaQty(w.ClinicA));
        Assert.False(Sch(w, w.Schedule1Id).IsDone);
        w.Clean("ungive");
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(2, w.Stocks().Single().Quantity);
        var led = w.Ledger();
        Assert.Equal(new[] { InventoryTransactionType.Purchase, InventoryTransactionType.Administer, InventoryTransactionType.Unadminister, InventoryTransactionType.Administer },
            led.Select(l => l.SourceType).ToArray());
        Assert.Equal(new[] { 3, -1, 1, -1 }, led.Select(l => l.QuantityDelta).ToArray());
        Assert.Equal(led[1].Id, led[2].ReversesTransactionId);
        Assert.All(led, l => Assert.Equal(w.Stocks().Single().Id, l.StockId));
        w.Clean("regive");
    }

    [Fact]
    public void ManyCycles_ShelfReturnsExactlyEachTime()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 1, 10);
        for (int i = 0; i < 6; i++)
        {
            Assert.True(w.GiveDose1().IsSuccess);
            Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.True(w.Stocks().Single().IsClosed);
            Assert.True(w.UngiveDose1().IsSuccess);
            Assert.Equal(1, w.Shelf(w.ClinicA)); Assert.False(w.Stocks().Single().IsClosed);
            w.Clean("cycle " + i);
        }
        Assert.Equal(1, w.BaQty(w.ClinicA));
        Assert.Empty(Uses(w));
    }

    [Fact]
    public void Give_SetsScheduleStockIdToConsumedBatch()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "EARLY", 5, 10, expiry: new DateTime(2030, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "LATE", 5, 10, expiry: new DateTime(2035, 1, 1))).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(w.Stocks().Single(s => s.BatchLot == "EARLY").Id, Sch(w, w.Schedule1Id).StockId);
    }

    [Fact]
    public void Give_StampsScheduleLotAndExpiryFromTheBatchItActuallyConsumed()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "EARLY", 5, 10, expiry: new DateTime(2030, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "LATE", 5, 10, expiry: new DateTime(2035, 1, 1))).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        var sch = Sch(w, w.Schedule1Id);
        Assert.Equal("EARLY", sch.Lot);
        Assert.Equal(new DateTime(2030, 1, 1), sch.Expiry);
    }

    [Fact]
    public void EditingAGivenDose_SameBrand_KeepsMovementAndStamp()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "EARLY", 5, 10, expiry: new DateTime(2030, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "LATE", 5, 10, expiry: new DateTime(2035, 1, 1))).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        var snapStocks = w.Stocks().Select(s => (s.Id, s.Quantity)).ToList();
        var lotBefore = Sch(w, w.Schedule1Id).Lot;
        var again = w.GiveDose1();     // true -> true, identical resubmission (double click / edit)
        Assert.True(again.IsSuccess, again.Message);
        Assert.Equal(snapStocks, w.Stocks().Select(s => (s.Id, s.Quantity)).ToList());
        Assert.Single(w.Ledger(InventoryTransactionType.Administer));
        Assert.Equal(lotBefore, Sch(w, w.Schedule1Id).Lot);
        w.Clean("true->true");
    }

    [Fact]
    public void UngiveRestoresTheSameBatch_EvenWhenAnEarlierBatchArrivedMeanwhile()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "B1", 1, 10, expiry: new DateTime(2031, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "B2", 5, 10, expiry: new DateTime(2035, 1, 1))).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);                               // B1 -> 0
        Assert.Equal(0, w.Stocks().Single(s => s.BatchLot == "B1").Quantity);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "B0", 3, 10, expiry: new DateTime(2029, 1, 1))).o.IsSuccess);
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(1, w.Stocks().Single(s => s.BatchLot == "B1").Quantity);
        Assert.Equal(3, w.Stocks().Single(s => s.BatchLot == "B0").Quantity);
        Assert.Equal(5, w.Stocks().Single(s => s.BatchLot == "B2").Quantity);
        Assert.False(w.Stocks().Single(s => s.BatchLot == "B1").IsClosed);
        w.Clean("ungive same batch");
    }

    [Fact]
    public void UngiveOfTwoDosFromTwoBatches_EachGoesBackToItsOwnBatch()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "B1", 1, 10, expiry: new DateTime(2031, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "B2", 5, 10, expiry: new DateTime(2035, 1, 1))).o.IsSuccess);
        var (c2, s2) = w.AddChild();
        Assert.True(w.GiveDose1().IsSuccess);                                   // B1
        Assert.True(w.Give(s2, c2, (int)w.Dose1Id).IsSuccess);                  // B2
        Assert.True(w.Ungive(s2, c2, (int)w.Dose1Id).IsSuccess);
        Assert.Equal(5, w.Stocks().Single(s => s.BatchLot == "B2").Quantity);
        Assert.Equal(0, w.Stocks().Single(s => s.BatchLot == "B1").Quantity);
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(1, w.Stocks().Single(s => s.BatchLot == "B1").Quantity);
        Assert.Equal(6, w.BaQty(w.ClinicA));
        w.Clean("two dose ungive");
    }

    // ------------------------------------------------------------ give at zero => pending
    [Fact]
    public void GiveAtZeroStock_IsPending_NoStockLedgerOrQuantityChange_ThenUngiveVoidsIt()
    {
        using var w = new StockWorld();
        var before = w.Snapshot();
        var g = w.GiveDose1(); Assert.True(g.IsSuccess, g.Message);
        Assert.True(Sch(w, w.Schedule1Id).IsDone);
        Assert.Empty(w.Stocks());
        Assert.Equal(0, w.BaQty(w.ClinicA));
        Assert.Empty(w.Ledger());                       // no ledger row at all for a pending use
        var uses = Uses(w);
        Assert.Single(uses); Assert.Equal(UnbatchedUseStatus.Pending, uses[0].Status); Assert.Equal(w.Schedule1Id, uses[0].ScheduleId);
        Assert.Equal(w.Schedule1Id, uses[0].ActiveScheduleKey);
        w.Clean("pending");

        var u = w.UngiveDose1(); Assert.True(u.IsSuccess, u.Message);
        uses = Uses(w);
        Assert.Single(uses); Assert.Equal(UnbatchedUseStatus.Voided, uses[0].Status); Assert.Null(uses[0].ActiveScheduleKey);
        Assert.Empty(w.Stocks()); Assert.Equal(0, w.BaQty(w.ClinicA));
        Assert.All(w.Ledger(), l => Assert.Equal(0, l.QuantityDelta));
        Assert.False(Sch(w, w.Schedule1Id).IsDone);
        w.Clean("pending voided");
    }

    [Fact]
    public void PendingGive_RegiveAfterVoid_CreatesNewLiveUse()
    {
        using var w = new StockWorld();
        for (int i = 0; i < 3; i++)
        {
            Assert.True(w.GiveDose1().IsSuccess);
            Assert.True(w.UngiveDose1().IsSuccess);
        }
        Assert.True(w.GiveDose1().IsSuccess);
        var uses = Uses(w);
        Assert.Equal(4, uses.Count);
        Assert.Single(uses.Where(u => u.Status == UnbatchedUseStatus.Pending));
        Assert.Equal(3, uses.Count(u => u.Status == UnbatchedUseStatus.Voided));
        Assert.Equal(0, w.BaQty(w.ClinicA));
        w.Clean("pending cycles");
    }

    [Fact]
    public void PendingGive_DoubleSubmit_StillOneUse()
    {
        using var w = new StockWorld();
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);            // true->true duplicate
        Assert.Single(Uses(w));
        Assert.Equal(0, w.BaQty(w.ClinicA));
        w.Clean("pending double submit");
    }

    [Fact]
    public void PendingUse_ThenPurchase_StockUntouched_AndUngiveNeverInventsAUnit()
    {
        using var w = new StockWorld();
        Assert.True(w.GiveDose1().IsSuccess);            // pending
        w.PostBill(w.ClinicA, 10, 10);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(10, w.BaQty(w.ClinicA));
        Assert.Equal(UnbatchedUseStatus.Pending, Uses(w).Single().Status);
        w.Clean("purchase after pending");
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(10, w.BaQty(w.ClinicA));
        Assert.Equal(UnbatchedUseStatus.Voided, Uses(w).Single().Status);
        w.Clean("ungive of pending after purchase");
        // a later real give takes exactly one from the purchase
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(9, w.Shelf(w.ClinicA));
        w.Clean("real give after");
    }

    [Fact]
    public void GiveWithoutConfirmAtZeroStock_Rejected_ZeroChange()
    {
        using var w = new StockWorld();
        var snap = w.Snapshot();
        for (int i = 0; i < 3; i++)
        {
            var r = w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, confirmUnbatched: null);
            Assert.False(r.IsSuccess);
        }
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void ManyGivesBeyondStock_FirstUseBatch_RestArePending_UngiveDoesNotClaimPending()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 2, 10);
        var kids = new List<(long c, long s)> { (w.ChildId, w.Schedule1Id) };
        for (int i = 0; i < 3; i++) kids.Add(w.AddChild());
        foreach (var k in kids) Assert.True(w.Give(k.s, k.c, (int)w.Dose1Id).IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA));
        Assert.Equal(2, Uses(w).Count(u => u.Status == UnbatchedUseStatus.Pending));
        Assert.Equal(2, w.Ledger(InventoryTransactionType.Administer).Count);
        w.Clean("4 gives on 2 units");
        Assert.True(w.Ungive(kids[0].s, kids[0].c, (int)w.Dose1Id).IsSuccess);          // real one
        Assert.Equal(1, w.Shelf(w.ClinicA));                                             // only ITS unit returns
        Assert.Equal(2, Uses(w).Count(u => u.Status == UnbatchedUseStatus.Pending));     // pending stay pending
        Assert.True(w.Ungive(kids[3].s, kids[3].c, (int)w.Dose1Id).IsSuccess);          // pending one
        Assert.Equal(1, w.Shelf(w.ClinicA));
        Assert.Equal(1, Uses(w).Count(u => u.Status == UnbatchedUseStatus.Pending));
        w.Clean("ungives");
    }

    // ------------------------------------------------------------ brand change true->true
    [Fact]
    public void BrandChange_OnGivenDose_ReversesOldBrand_DeductsNewBrand()
    {
        using var w = new StockWorld();
        long y = w.AddBrand("BRANDY");
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(y, "Y1", 4, 10)).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(4, w.Shelf(w.ClinicA));
        var r = w.GiveDose1(brand: y); Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(5, w.Shelf(w.ClinicA)); Assert.Equal(3, w.Shelf(w.ClinicA, y));
        Assert.Equal(5, w.BaQty(w.ClinicA)); Assert.Equal(3, w.BaQty(w.ClinicA, y));
        Assert.Equal(y, Sch(w, w.Schedule1Id).BrandId);
        var led = w.Ledger().Where(l => l.SourceType == InventoryTransactionType.Administer || l.SourceType == InventoryTransactionType.Unadminister).ToList();
        Assert.Equal(new[] { -1, 1, -1 }, led.Select(l => l.QuantityDelta).ToArray());
        w.Clean("brand change");
        // and back again, then ungive: everything returns
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(4, w.Shelf(w.ClinicA)); Assert.Equal(4, w.Shelf(w.ClinicA, y));
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(5, w.Shelf(w.ClinicA)); Assert.Equal(4, w.Shelf(w.ClinicA, y));
        w.Clean("brand change back and ungive");
    }

    [Fact]
    public void BrandChange_ToBrandWithNoStock_RestoresOld_NewIsPending()
    {
        using var w = new StockWorld();
        long y = w.AddBrand("BRANDY");
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.True(w.GiveDose1(brand: y).IsSuccess);
        Assert.Equal(5, w.Shelf(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicA, y));
        Assert.Equal(y, Uses(w).Single(u => u.Status == UnbatchedUseStatus.Pending).BrandId);
        w.Clean("brand change to empty brand");
        // ungive: pending voided, old brand untouched
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(5, w.Shelf(w.ClinicA));
        Assert.Empty(Uses(w).Where(u => u.Status == UnbatchedUseStatus.Pending));
        w.Clean("ungive after brand change");
    }

    [Fact]
    public void BrandChange_FromPendingBrandToStockedBrand_VoidsPending_DeductsNew()
    {
        using var w = new StockWorld();
        long y = w.AddBrand("BRANDY");
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(y, "Y1", 4, 10)).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);            // BRANDX, no stock -> pending
        Assert.Single(Uses(w).Where(u => u.Status == UnbatchedUseStatus.Pending));
        Assert.True(w.GiveDose1(brand: y).IsSuccess);
        Assert.Equal(3, w.Shelf(w.ClinicA, y));
        Assert.Empty(Uses(w).Where(u => u.Status == UnbatchedUseStatus.Pending));
        w.Clean("pending -> stocked brand");
    }

    // ------------------------------------------------------------ failed gives
    [Fact]
    public void FailedGives_RepeatedFiveTimes_ZeroMovement()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        var yesterday = StockWorld.Today.AddDays(-1);
        var cases = new List<Func<Response<ScheduleDTO>>>
        {
            // dose 2 while dose 1 not given
            () => w.Give(w.Schedule2Id, w.ChildId, (int)w.Dose2Id),
            // no given date
            () => { using var db = w.NewContext(); return w.Schedules(db).Update(new ScheduleDTO { Id = w.Schedule1Id, ChildId = w.ChildId, DoseId = (int)w.Dose1Id, DoctorId = w.DoctorId, IsDone = true, BrandId = w.BrandId, ConfirmUnbatchedGive = true }); },
            // given date on/before DOB
            () => w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, date: StockWorld.Today.AddYears(-3)),
            // future date
            () => w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, date: StockWorld.Today.AddDays(3)),
            // backdated in-period brand give without the historical answer
            () => w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, date: yesterday, reRecord: null),
            // brand with no BrandAmount row at the clinic
            () => w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, brand: 424242),
            // unknown schedule
            () => w.Give(777777, w.ChildId, (int)w.Dose1Id),
        };
        int i = 0;
        foreach (var c in cases)
        {
            for (int rep = 0; rep < 5; rep++)
            {
                var r = c();
                Assert.False(r.IsSuccess, $"case {i} rep {rep} unexpectedly succeeded");
                Assert.Equal(snap, w.Snapshot());
            }
            i++;
        }
        w.Clean("failed gives");
        Assert.Equal(10, w.Shelf(w.ClinicA));
    }

    [Fact]
    public void Dose2GiveAfterDose1_Works_ThenUngiveDose1WhileDose2Given_StockStaysExact()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.True(w.Give(w.Schedule2Id, w.ChildId, (int)w.Dose2Id).IsSuccess);
        Assert.Equal(8, w.Shelf(w.ClinicA));
        var u = w.UngiveDose1();      // whatever the business rule says, stock must remain exact
        Assert.Equal(u.IsSuccess ? 9 : 8, w.Shelf(w.ClinicA));
        Assert.Equal(w.Shelf(w.ClinicA), w.BaQty(w.ClinicA));
        w.Clean("ungive predecessor");
    }

    [Fact]
    public void UngiveOfNeverGivenDose_ChangesNoStock()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 4, 10);
        var r = w.UngiveDose1();
        Assert.Equal(4, w.Shelf(w.ClinicA)); Assert.Equal(4, w.BaQty(w.ClinicA));
        w.Clean("ungive of never given");
    }

    [Fact]
    public void UngiveTwice_SecondNeverAddsAnotherUnit()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 4, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.True(w.UngiveDose1().IsSuccess);
        w.UngiveDose1();
        w.UngiveDose1();
        Assert.Equal(4, w.Shelf(w.ClinicA)); Assert.Equal(4, w.BaQty(w.ClinicA));
        w.Clean("triple ungive");
    }

    // ------------------------------------------------------------ backdated / historical
    [Fact]
    public void BackdatedGive_LateRecording_Deducts_HistoricalDoesNot_UngiveMirrors()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        var y = StockWorld.Today.AddDays(-1);
        var (c2, s2) = w.AddChild();
        Assert.True(w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, date: y, reRecord: false).IsSuccess);     // deduct
        Assert.Equal(4, w.Shelf(w.ClinicA));
        Assert.True(w.Give(s2, c2, (int)w.Dose1Id, date: y, reRecord: true).IsSuccess);                       // history only
        Assert.Equal(4, w.Shelf(w.ClinicA));
        w.Clean("backdated gives");
        Assert.True(w.Ungive(s2, c2, (int)w.Dose1Id).IsSuccess);
        Assert.Equal(4, w.Shelf(w.ClinicA));
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(5, w.Shelf(w.ClinicA));
        w.Clean("backdated ungives");
    }

    // ------------------------------------------------------------ bulk
    static (long child, long[] schedules) BulkChild(StockWorld w, int doses)
    {
        var (c, s1) = w.AddChild();
        var list = new List<long> { s1 };
        for (int i = 1; i < doses; i++) list.Add(w.AddOneDoseVaccine(c, "BV" + i).schedule);
        return (c, list.ToArray());
    }

    [Fact]
    public void BulkGive_ThreeDosesSameBrand_OneUnitBatch_OneConsumesRestPending()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 1, 10);
        var (c, ss) = BulkChild(w, 3);
        var r = w.BulkGive(ss[0], w.BrandId, ss); Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicA));
        Assert.Single(w.Ledger(InventoryTransactionType.Administer));
        Assert.Equal(2, Uses(w).Count(u => u.Status == UnbatchedUseStatus.Pending));
        using (var db = w.NewContext()) Assert.All(db.Schedules.Where(s => ss.ToList().Contains(s.Id)).ToList(), s => Assert.True(s.IsDone));
        w.Clean("bulk 3 on 1");
        // bulk ungive (brands sent): the one real unit returns, pending voided; never 3
        var u = w.BulkUngive(ss[0], w.BrandId, true, ss); Assert.True(u.IsSuccess, u.Message);
        Assert.Equal(1, w.Shelf(w.ClinicA)); Assert.Equal(1, w.BaQty(w.ClinicA));
        Assert.Empty(Uses(w).Where(x => x.Status == UnbatchedUseStatus.Pending));
        w.Clean("bulk ungive");
    }

    [Fact]
    public void BulkGive_ThreeDosesSameBrand_ExactStock_AllBatched_UngiveRestoresAll()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var (c, ss) = BulkChild(w, 3);
        Assert.True(w.BulkGive(ss[0], w.BrandId, ss).IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicA));
        Assert.Equal(3, w.Ledger(InventoryTransactionType.Administer).Count);
        Assert.Empty(Uses(w));
        Assert.True(w.Stocks().Single().IsClosed);
        w.Clean("bulk exact");
        Assert.True(w.BulkUngive(ss[0], w.BrandId, true, ss).IsSuccess);
        Assert.Equal(3, w.Shelf(w.ClinicA)); Assert.Equal(3, w.BaQty(w.ClinicA));
        Assert.False(w.Stocks().Single().IsClosed);
        w.Clean("bulk exact ungive");
    }

    [Fact]
    public void BulkUngive_WithoutScheduleBrandsInRequest_MustStillRestoreStock()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var (c, ss) = BulkChild(w, 3);
        Assert.True(w.BulkGive(ss[0], w.BrandId, ss).IsSuccess);
        var u = w.BulkUngive(ss[0], w.BrandId, sendBrands: false, ss);
        Assert.True(u.IsSuccess, u.Message);
        using (var db = w.NewContext()) Assert.All(db.Schedules.Where(s => ss.ToList().Contains(s.Id)).ToList(), s => Assert.False(s.IsDone));
        Assert.Equal(3, w.Shelf(w.ClinicA));
        Assert.Equal(3, w.BaQty(w.ClinicA));
        w.Clean("bulk ungive without brands");
    }

    [Fact]
    public void BulkGive_WithOneBadBrand_RollsBackWholeBatch()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        var (c, ss) = BulkChild(w, 3);
        var snap = w.Snapshot();
        using (var db = w.NewContext())
        {
            var dto = new ScheduleDTO { Id = ss[0], DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, Date = StockWorld.Today, ConfirmUnbatchedGive = true };
            dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)ss[0], BrandId = w.BrandId });
            dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)ss[1], BrandId = w.BrandId });
            dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)ss[2], BrandId = 424242 });
            var r = w.Schedules(db).UpdateBulkInjection(dto);
            Assert.False(r.IsSuccess);
        }
        Assert.Equal(snap, w.Snapshot());
        w.Clean("bulk rollback");
    }

    [Fact]
    public void BulkGive_SameVaccineDose1AndDose2_SameDay_StockExact()
    {
        // The seed doses carry no MinGap, so the BUG-9 block does not apply; both doses are given
        // and each must consume exactly one unit.
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        var r = w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(3, w.Shelf(w.ClinicA)); Assert.Equal(3, w.BaQty(w.ClinicA));
        Assert.Equal(2, w.Ledger(InventoryTransactionType.Administer).Count);
        w.Clean("bulk same vaccine");
    }

    [Fact]
    public void BulkGive_FutureDateOrNoDate_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        var (c, ss) = BulkChild(w, 2);
        var snap = w.Snapshot();
        using (var db = w.NewContext())
        {
            var dto = new ScheduleDTO { Id = ss[0], DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today.AddDays(2), Date = StockWorld.Today, ConfirmUnbatchedGive = true };
            foreach (var s in ss) dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)s, BrandId = w.BrandId });
            Assert.False(w.Schedules(db).UpdateBulkInjection(dto).IsSuccess);
        }
        using (var db = w.NewContext())
        {
            var dto = new ScheduleDTO { Id = ss[0], DoctorId = w.DoctorId, IsDone = true, Date = StockWorld.Today, ConfirmUnbatchedGive = true };
            foreach (var s in ss) dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)s, BrandId = w.BrandId });
            Assert.False(w.Schedules(db).UpdateBulkInjection(dto).IsSuccess);
        }
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void BulkGive_BackdatedWithoutAnswer_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        var (c, ss) = BulkChild(w, 2);
        var snap = w.Snapshot();
        using var db = w.NewContext();
        var dto = new ScheduleDTO { Id = ss[0], DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today.AddDays(-1), Date = StockWorld.Today, ConfirmUnbatchedGive = true };
        foreach (var s in ss) dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)s, BrandId = w.BrandId });
        Assert.False(w.Schedules(db).UpdateBulkInjection(dto).IsSuccess);
        db.ChangeTracker.Clear();
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void BulkGive_DifferentBrandsPerDose_EachDeductsItsOwnBrand()
    {
        using var w = new StockWorld();
        long y = w.AddBrand("BRANDY");
        w.PostBill(w.ClinicA, 2, 10);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(y, "Y1", 2, 10)).o.IsSuccess);
        var (c, ss) = BulkChild(w, 2);
        using (var db = w.NewContext())
        {
            var dto = new ScheduleDTO { Id = ss[0], DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, Date = StockWorld.Today, ConfirmUnbatchedGive = true };
            dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)ss[0], BrandId = w.BrandId });
            dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)ss[1], BrandId = y });
            var r = w.Schedules(db).UpdateBulkInjection(dto); Assert.True(r.IsSuccess, r.Message);
        }
        Assert.Equal(1, w.Shelf(w.ClinicA)); Assert.Equal(1, w.Shelf(w.ClinicA, y));
        w.Clean("bulk two brands");
    }

    [Fact]
    public void BulkGive_ThenSingleUngiveOfOne_OnlyThatUnitReturns()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var (c, ss) = BulkChild(w, 3);
        Assert.True(w.BulkGive(ss[0], w.BrandId, ss).IsSuccess);
        using (var db = w.NewContext()) { }
        var s = ss[1];
        int doseId; using (var db = w.NewContext()) doseId = (int)db.Schedules.Single(x => x.Id == s).DoseId;
        var u = w.Ungive(s, c, doseId);
        Assert.True(u.IsSuccess, u.Message);
        Assert.Equal(1, w.Shelf(w.ClinicA)); Assert.Equal(1, w.BaQty(w.ClinicA));
        w.Clean("bulk then single ungive");
    }

    // ------------------------------------------------------------ multi clinic
    [Fact]
    public void Give_SameLotAtBothClinics_OnlyOnlineClinicConsumed_UngiveRestoresThere()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10, "L1");
        w.PostBill(w.ClinicB, 3, 10, "L1");
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(2, w.Shelf(w.ClinicA)); Assert.Equal(3, w.Shelf(w.ClinicB));
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(3, w.Shelf(w.ClinicA)); Assert.Equal(3, w.Shelf(w.ClinicB));
        Assert.Equal(3, w.BaQty(w.ClinicA)); Assert.Equal(3, w.BaQty(w.ClinicB));
        w.Clean("two clinic give");
    }

    [Fact]
    public void Ungive_WhenChildBelongsToOtherClinicThanTheOnlineOne_StillRestoresTheConsumedBatch()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10, "L1");
        w.PostBill(w.ClinicB, 3, 10, "L1");
        using (var db = w.NewContext()) { db.Childs.Single(c => c.Id == w.ChildId).ClinicId = w.ClinicB; db.SaveChanges(); }
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(2, w.Shelf(w.ClinicA)); Assert.Equal(3, w.Shelf(w.ClinicB));
        var u = w.UngiveDose1(); Assert.True(u.IsSuccess, u.Message);
        Assert.Equal(3, w.Shelf(w.ClinicA)); Assert.Equal(3, w.Shelf(w.ClinicB));
        w.Clean("ungive with child at other clinic");
    }

    [Fact]
    public void Give_OnlyOtherClinicHasStock_IsPendingNotBorrowed()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicB, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(5, w.Shelf(w.ClinicB)); Assert.Equal(5, w.BaQty(w.ClinicB));
        Assert.Single(Uses(w).Where(u => u.Status == UnbatchedUseStatus.Pending));
        w.Clean("no cross-clinic borrow");
    }

    // ------------------------------------------------------------ duplicates / documented behaviour
    [Fact]
    public void DuplicateBillCreate_TwiceIdentical_DocumentsCurrentBehaviour()
    {
        // FINDING: idempotency is NOT implemented. A resubmitted identical payload posts twice.
        using var w = new StockWorld();
        var dto = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S", BillNo = "SAME-1" };
        dto.Lines.Add(Line(w.BrandId, "L1", 10, 10));
        for (int i = 0; i < 2; i++) { using var db = w.NewContext(); Result.Of(w.Bills(db).Create(dto).GetAwaiter().GetResult()); }
        Console.WriteLine($"DUP-BILL: bills={w.Snapshot().Bills} shelf={w.Shelf(w.ClinicA)}");
        Assert.Equal(w.Shelf(w.ClinicA), w.BaQty(w.ClinicA));
        w.Clean("duplicate bill");
    }

    [Fact]
    public void DuplicateSaleAndTransferCreate_Twice_DocumentsCurrentBehaviour()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        var r1 = w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 3));
        var r2 = w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 3));
        var t1 = w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 2));
        var t2 = w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 2));
        Console.WriteLine($"DUP: sale {r1.IsSuccess}/{r2.IsSuccess} transfer {t1.IsSuccess}/{t2.IsSuccess} shelfA={w.Shelf(w.ClinicA)} shelfB={w.Shelf(w.ClinicB)}");
        w.Clean("duplicate sale/transfer");
    }
}
