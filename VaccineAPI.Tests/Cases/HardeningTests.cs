using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Holes found by the QA mutation run, plus write-off and the remaining endpoints.</summary>
public class HardeningTests
{
    // ---- mutation iv: the explicit up-front rejection of a partly-used transfer (2 lines)
    [Fact]
    public void TransferDelete_TwoLines_OneUsedAtDestination_IsRejectedWithMessage_AndNothingMoves()
    {
        using var w = new StockWorld();
        var other = w.AddBrand("OTHER");
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 10, 10), Kit.Line(other, "M1", 10, 10));
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, Kit.XItem(w.BrandId, "L1", 10), Kit.XItem(other, "M1", 10)).IsSuccess);
        Assert.True(w.Sell(w.ClinicB, Kit.SaleItem(w.BrandId, "L1", 4)).IsSuccess);   // 4 of line 1 used at B
        var before = w.Snapshot();

        var res = w.DeleteTransfer(w.TransferIds().First());
        Assert.False(res.IsSuccess);
        Assert.Contains("already used or moved at the destination", res.Message);
        Assert.Equal(before, w.Snapshot());                       // zero movement, both lines untouched
        w.Clean("after the refused transfer delete");
    }

    // ---- mutation ix: claiming more doses than the batch holds fails cleanly
    [Fact]
    public void Claim_MoreDosesThanTheBatchHolds_FailsCleanly_NothingChanges()
    {
        using var w = new StockWorld();
        for (int i = 0; i < 3; i++) { var (c, s) = w.AddChild(); Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess); }
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 2, 10));
        List<long> ids; int stockId;
        using (var db = w.NewContext()) { ids = db.UnbatchedUses.Select(u => u.Id).ToList(); stockId = db.Stocks.Single().Id; }
        var before = w.Snapshot();

        using var db2 = w.NewContext();
        var svc = new InventoryTransactionService(db2);
        var res = svc.ClaimUnbatched(w.DoctorId, stockId, ids);
        Assert.False(res.IsSuccess);
        Assert.Contains("holds 2 unit(s) but 3 dose(s)", res.Message);
        db2.SaveChanges();
        Assert.Equal(before, w.Snapshot());
    }

    // ---- idempotency key is scoped to the endpoint
    [Fact]
    public void SameClientRequestIdOnTwoDifferentEndpoints_IsNotConfused()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext())
        {
            var d = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S", BillNo = "B1", ClientRequestId = "shared" };
            d.Lines.Add(Kit.Line(w.BrandId, "L1", 10, 10));
            Assert.True(Result.Of(w.Bills(db).Create(d).GetAwaiter().GetResult()).IsSuccess);
        }
        using (var db = w.NewContext())
        {
            var r = w.Sales(db).Create(new DirectSaleCreateDTO { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ClientName = "c", SaleDate = StockWorld.Today,
                ClientRequestId = "shared", Items = { Kit.SaleItem(w.BrandId, "L1", 3) } }).GetAwaiter().GetResult();
            var o = Result.Of(r);
            Assert.True(o.IsSuccess, o.Message);
            Assert.Equal("Sale recorded", o.Message);            // not the bill's replayed answer
        }
        Assert.Equal(7, w.Shelf(w.ClinicA));
        w.Clean("after the shared key");
    }

    // ---- write-off
    private static Result.Outcome WriteOff(StockWorld w, int stockId, int qty, string kind, string reason = "vial broke")
    {
        using var db = w.NewContext();
        return Result.Of(w.Adjusts(db).WriteOff(new WriteOffDTO { DoctorId = w.DoctorId, ClinicId = w.ClinicA, StockId = stockId, Quantity = qty,
            Kind = kind, Reason = reason, Date = StockWorld.Today }).GetAwaiter().GetResult());
    }

    [Fact]
    public void WriteOff_ExpiredUnits_LeaveTheShelf_AsTypedMovement_AndCanBeUndone()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 10, 10));
        int stockId; using (var db = w.NewContext()) stockId = db.Stocks.Single().Id;

        Assert.True(WriteOff(w, stockId, 4, "Expiry", "expired 01/2026").IsSuccess);
        Assert.Equal(6, w.Shelf(w.ClinicA));
        Assert.Equal(6, w.BaQty(w.ClinicA));
        using (var db = w.NewContext())
            Assert.Contains(db.InventoryTransactions, t => t.SourceType == InventoryTransactionType.Expiry && t.QuantityDelta == -4 && t.StockId == stockId);
        w.Clean("after write-off");

        Assert.True(WriteOff(w, stockId, 6, "Wastage").IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA));
        using (var db = w.NewContext()) Assert.True(db.Stocks.Single().IsClosed);

        // undo the first write-off through the adjustment delete: same batch restored
        long adjId = w.AdjustIds().First();
        Assert.True(w.DeleteAdjust(adjId).IsSuccess);
        Assert.Equal(4, w.Shelf(w.ClinicA));
        using (var db = w.NewContext()) Assert.False(db.Stocks.Single().IsClosed);
        w.Clean("after undoing a write-off");
    }

    [Fact]
    public void WriteOff_Validation_NothingChanges()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 5, 10));
        int stockId; using (var db = w.NewContext()) stockId = db.Stocks.Single().Id;
        var before = w.Snapshot();
        Assert.False(WriteOff(w, stockId, 6, "Expiry").IsSuccess);          // more than the batch holds
        Assert.False(WriteOff(w, stockId, 1, "Theft").IsSuccess);           // unknown kind
        Assert.False(WriteOff(w, stockId, 1, "Expiry", "  ").IsSuccess);    // reason required
        Assert.False(WriteOff(w, 9999, 1, "Expiry").IsSuccess);             // no such batch
        Assert.Equal(before, w.Snapshot());
    }

    // ---- endpoints that had no coverage
    [Fact]
    public void OpeningBalance_CreatesBatchLedgerAndCounter()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext()) { db.Clinics.Find(w.ClinicA)!.StockPeriodStart = StockWorld.Today.AddDays(-30); db.SaveChanges(); }
        using var db2 = w.NewContext();
        var res = new StockController(db2, new InventoryTransactionService(db2)).PostOpeningBalance(new OpeningBalanceDTO
        {
            DoctorId = w.DoctorId, ClinicId = w.ClinicA,
            Lines = { new OpeningBalanceLine { BrandId = w.BrandId, Quantity = 12, UnitCost = 8, BatchLot = "OB1", Expiry = StockWorld.Expiry } }
        }).GetAwaiter().GetResult();
        Assert.True(Result.Of(res).IsSuccess, Result.Of(res).Message);
        Assert.Equal(12, w.Shelf(w.ClinicA));
        Assert.Equal(12, w.BaQty(w.ClinicA));
        using var db3 = w.NewContext();
        Assert.Contains(db3.InventoryTransactions, t => t.SourceType == InventoryTransactionType.OpeningBalance && t.QuantityDelta == 12);
        w.Clean("after opening balance");
    }

    [Fact]
    public void IntegrityAndBatchLots_ReflectRealState()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 10, 10));
        using (var db = w.NewContext())
        {
            var c = new StockController(db, new InventoryTransactionService(db));
            var clean = c.CheckIntegrity(w.ClinicA).GetAwaiter().GetResult();
            Assert.True((bool)Result.Prop(clean, "IsClean")!);
            var lots = Result.Prop(c.GetBatchLots(w.BrandId, w.ClinicA).GetAwaiter().GetResult(), "ResponseData") as System.Collections.IEnumerable;
            Assert.Single(lots!.Cast<object>());
        }
        using (var db = w.NewContext()) db.Database.ExecuteSqlRaw("UPDATE brandamounts SET Quantity = 99 WHERE ClinicId = " + w.ClinicA);
        using (var db = w.NewContext())
        {
            var dirty = new StockController(db, new InventoryTransactionService(db)).CheckIntegrity(w.ClinicA).GetAwaiter().GetResult();
            Assert.False((bool)Result.Prop(dirty, "IsClean")!);
            Assert.Equal(1, (int)Result.Prop(dirty, "MismatchCount")!);
        }
    }

    [Fact]
    public void Deletes_AreRefusedWhileStockOrGivenDosesExist()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 3, 10));

        using (var db = w.NewContext())
        {
            var r = new ClinicController(db, null!).Delete((int)w.ClinicA);
            Assert.False(r.IsSuccess); Assert.Contains("still holds stock", r.Message);
        }
        using (var db = w.NewContext())
        {
            var r = new BrandController(db, null!).Delete(w.BrandId).GetAwaiter().GetResult();
            Assert.False(r.IsSuccess); Assert.Contains("still has stock", r.Message);
        }
        using (var db = w.NewContext())
        {
            var r = new DoctorController(db, null!, null!).Delete((int)w.DoctorId);
            Assert.False(r.IsSuccess); Assert.Contains("still holds stock", r.Message);
        }
        long userId; using (var db = w.NewContext()) userId = db.Doctors.Find(w.DoctorId)!.UserId;
        using (var db = w.NewContext())
        {
            // Deleting a user is a super-admin action; run it as one so the stock guard is what answers.
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.Items["auth.identity"] = new AuthIdentity { UserId = 1, Role = "SUPERADMIN" };
            var saved = AuthContext.Accessor;
            AuthContext.Accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http };
            try
            {
                var r = new UserController(db, null!, new ConfigurationBuilder().Build()).Delete(userId).GetAwaiter().GetResult();
                Assert.IsType<ConflictObjectResult>(r);
            }
            finally { AuthContext.Accessor = saved; }
        }
        // a dose that has been given cannot be deleted out from under its stock trail
        Assert.True(w.GiveDose1().IsSuccess);
        using (var db = w.NewContext())
        {
            var r = new DoseController(db, null!).Delete((int)w.Dose1Id);
            Assert.False(r.IsSuccess); Assert.Contains("already been given", r.Message);
        }
        using var chk = w.NewContext();
        Assert.True(chk.Clinics.Any(c => c.Id == w.ClinicA));
        Assert.True(chk.Brands.Any(b => b.Id == w.BrandId));
        Assert.True(chk.Doses.Any(d => d.Id == w.Dose1Id));
        w.Clean("after refused deletes");
    }

    [Fact]
    public void ChildDelete_VoidsThatPatientsPendingDose()
    {
        using var w = new StockWorld();
        var (c, s) = w.AddChild();
        Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess);
        using (var db = w.NewContext()) Assert.Equal(1, db.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Pending));
        using (var db = w.NewContext())
        {
            var r = new ChildController(db, null!, null!, new ConfigurationBuilder().Build()).Delete((int)c);
            Assert.True(r.IsSuccess, r.Message);
        }
        using var chk = w.NewContext();
        Assert.Equal(0, chk.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Pending));
        Assert.Equal(1, chk.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Voided));
    }
}
