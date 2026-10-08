using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

// A give honours the batch the nurse chose; anything unusable falls back to FEFO (never rejected).
public class PreferredLotGiveTests
{
    static int? GiveWith(StockWorld w, string? lot, DateTime? expiry, long scheduleId)
    {
        using var db = w.NewContext();
        var ba = db.BrandAmounts.First(x => x.BrandId == w.BrandId && x.ClinicId == w.ClinicA);
        new InventoryTransactionService(db).AdministerSync(ba, w.ClinicA, scheduleId, StockWorld.Today, null, true, "NORMAL",
            out var stockId, lot, expiry);
        db.SaveChanges();
        return stockId;
    }

    static string LotOf(StockWorld w, int? stockId) { using var db = w.NewContext(); return db.Stocks.AsNoTracking().Single(s => s.Id == stockId).BatchLot!; }

    [Fact]
    public void ChosenLot_IsTheOneDeducted_NotJustFefo()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10, "L1");
        w.PostBill(w.ClinicA, 3, 10, "L2");
        var id = GiveWith(w, "L2", null, 9101);
        Assert.Equal("L2", LotOf(w, id));
        using var db = w.NewContext();
        Assert.Equal(2, db.Stocks.AsNoTracking().Single(s => s.BatchLot == "L2").Quantity);
        Assert.Equal(3, db.Stocks.AsNoTracking().Single(s => s.BatchLot == "L1").Quantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOT-A-LOT")]
    public void BlankOrUnknownLot_FallsBackToFefo(string? lot)
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10, "L1");
        w.PostBill(w.ClinicA, 3, 10, "L2");
        var id = GiveWith(w, lot, null, 9102);
        Assert.Equal("L1", LotOf(w, id));
    }

    [Fact]
    public void ChosenLot_UngiveRestoresSameBatch()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10, "L1");
        w.PostBill(w.ClinicA, 3, 10, "L2");
        var id = GiveWith(w, "L2", null, 9103);
        using (var db = w.NewContext())
        {
            new InventoryTransactionService(db).UnadministerSync(w.DoctorId, w.ClinicA, w.BrandId, 9103, StockWorld.Today);
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        Assert.Equal(3, db2.Stocks.AsNoTracking().Single(s => s.BatchLot == "L2").Quantity);
        Assert.Equal(3, db2.Stocks.AsNoTracking().Single(s => s.BatchLot == "L1").Quantity);
    }

    [Fact]
    public void ExpiredLot_NeverGiven_EvenWhenChosen_FallsBackToUnexpired()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: new DateTime(2020, 1, 1))).o.IsSuccess);
        w.PostBill(w.ClinicA, 3, 10, "GOOD");
        var id = GiveWith(w, "OLD", null, 9104);
        Assert.Equal("GOOD", LotOf(w, id));
    }

    [Fact]
    public void OnlyExpiredStock_GiveIsRecordedUnbatched_NothingDeducted()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: new DateTime(2020, 1, 1))).o.IsSuccess);
        var id = GiveWith(w, null, null, 9105);
        Assert.Null(id);
        using var db = w.NewContext();
        Assert.Equal(2, db.Stocks.AsNoTracking().Single(s => s.BatchLot == "OLD").Quantity);
        Assert.Single(db.UnbatchedUses.AsNoTracking().Where(u => u.ScheduleId == 9105).ToList());
    }
}
