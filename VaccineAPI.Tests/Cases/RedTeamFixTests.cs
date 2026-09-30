using Microsoft.EntityFrameworkCore;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Regression tests for what the independent red-team audit found.</summary>
public class RedTeamFixTests
{
    // RT3: sale bill numbers are only unique per doctor; a delete must never touch another doctor's sale
    [Fact]
    public void DeletingOneDoctorsSale_NeverReversesAnotherDoctorsSaleWithTheSameBillNumber()
    {
        using var w = new StockWorld();
        long d2, c2;
        using (var db = w.NewContext())
        {
            db.BypassInventoryWriterCheck = true;
            var u = new User { MobileNumber = "3111111111", Password = "x", UserType = "DOCTOR", CountryCode = "92" };
            db.Users.Add(u); db.SaveChanges();
            var doc = new Doctor { FirstName = "Two", UserId = u.Id, AllowInventory = true };
            db.Doctors.Add(doc); db.SaveChanges();
            var cl = new Clinic { Name = "C2", DoctorId = doc.Id, IsOnline = true, MaintainInventory = true };
            db.Clinics.Add(cl); db.SaveChanges();
            db.BrandAmounts.Add(new BrandAmount { BrandId = w.BrandId, DoctorId = doc.Id, ClinicId = cl.Id, SalePrice = 100 });
            db.SaveChanges();
            d2 = doc.Id; c2 = cl.Id;
        }
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 10, 10));
        using (var db = w.NewContext())
        {
            var d = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = d2, ClinicId = c2, SupplierName = "S", BillNo = "X2" };
            d.Lines.Add(Kit.Line(w.BrandId, "L2", 10, 10));
            Assert.True(Result.Of(w.Bills(db).Create(d).GetAwaiter().GetResult()).IsSuccess);
        }
        Assert.True(w.Sell(w.ClinicA, Kit.SaleItem(w.BrandId, "L1", 3)).IsSuccess);
        using (var db = w.NewContext())
            Assert.True(Result.Of(w.Sales(db).Create(new DirectSaleCreateDTO { DoctorId = d2, ClinicId = c2, ClientName = "c", SaleDate = StockWorld.Today,
                Items = { Kit.SaleItem(w.BrandId, "L2", 3) } }).GetAwaiter().GetResult()).IsSuccess);
        using (var db = w.NewContext())
        {
            var nos = db.DirectSales.Select(s => s.SaleBillNo).Distinct().ToList();
            Assert.Single(nos);                                   // both doctors got the same number
        }

        long doctor1Sale; using (var db = w.NewContext()) doctor1Sale = db.DirectSales.First(s => s.DoctorId == w.DoctorId).Id;
        Assert.True(w.DeleteSale(doctor1Sale).IsSuccess);

        using var chk = w.NewContext();
        Assert.Equal(1, chk.DirectSales.Count(s => s.DoctorId == d2));                 // doctor 2's sale survives
        Assert.Equal(7, chk.Stocks.Where(s => s.ClinicId == c2 || (s.Bill != null && s.Bill.ClinicId == c2)).Sum(s => s.Quantity));
        InventoryInvariants.AssertClean(chk, "after deleting doctor 1's sale");
    }

    // RT1: bulk brand correction of a dose that has no batch keeps the pending record
    [Fact]
    public void BulkBrandCorrectionOfAPendingDose_MovesThePendingRecordToTheNewBrand()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        var (c, s) = w.AddChild();
        Assert.True(w.BulkGive(s, w.BrandId, s).IsSuccess);
        using (var db = w.NewContext()) Assert.Equal(1, db.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Pending));

        var dto = new ScheduleDTO { Id = s, DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, Date = StockWorld.Today, ConfirmUnbatchedGive = true, IsPAApprove = true };
        dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)s, BrandId = b2 });
        using (var db = w.NewContext()) Assert.True(w.Schedules(db).UpdateBulkInjection(dto).IsSuccess);

        using var chk = w.NewContext();
        var live = chk.UnbatchedUses.Where(u => u.ActiveScheduleKey != null).ToList();
        Assert.Single(live);
        Assert.Equal(b2, live[0].BrandId);
        Assert.Equal(UnbatchedUseStatus.Pending, live[0].Status);
        w.Clean("after bulk brand correction");
    }

    // RT2: turning inventory off between a give and its ungive must not strand the unit
    [Fact]
    public void UngiveWhileInventoryIsSwitchedOff_StillReturnsTheUnit()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(4, w.Shelf(w.ClinicA));
        using (var db = w.NewContext()) { db.Clinics.Find(w.ClinicA)!.MaintainInventory = false; db.SaveChanges(); }
        Assert.True(w.UngiveDose1().IsSuccess);
        using (var db = w.NewContext()) { db.Clinics.Find(w.ClinicA)!.MaintainInventory = true; db.SaveChanges(); }
        Assert.Equal(5, w.Shelf(w.ClinicA));
        w.Clean("ungive with inventory off");
    }

    // RT7-style: a missing counter row is created, never silently skipped
    [Fact]
    public void PurchaseAtAClinicWithNoCounterRow_CreatesTheCounter()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext())
            db.Database.ExecuteSqlRaw("DELETE FROM brandamounts WHERE ClinicId = " + w.ClinicA);
        w.PostBill(w.ClinicA, 7, 10);
        Assert.Equal(7, w.BaQty(w.ClinicA));
        w.Clean("counter created by the purchase");
    }

    // #8: identical lines + claim ids
    [Fact]
    public void PurchaseWithTwoIdenticalLinesAndClaimIds_Succeeds()
    {
        using var w = new StockWorld();
        for (int i = 0; i < 3; i++) { var (c, s) = w.AddChild(); Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess); }
        List<long> ids; using (var db = w.NewContext()) ids = db.UnbatchedUses.Select(u => u.Id).ToList();
        using var db2 = w.NewContext();
        var dto = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S", BillNo = "B9", ClaimUnbatchedUseIds = ids };
        dto.Lines.Add(Kit.Line(w.BrandId, "L1", 5, 10));
        dto.Lines.Add(Kit.Line(w.BrandId, "L1", 5, 10));
        var o = Result.Of(w.Bills(db2).Create(dto).GetAwaiter().GetResult());
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(7, w.Shelf(w.ClinicA));
        w.Clean("after identical lines with claim");
    }

    // #9: a bill reversal row references the purchase row it undoes
    [Fact]
    public void BillReverseRow_ReferencesThePurchaseRow()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 6, 10);
        Assert.True(w.ReverseBill(bill).IsSuccess);
        using var db = w.NewContext();
        var purchase = db.InventoryTransactions.Single(t => t.SourceType == InventoryTransactionType.Purchase);
        var reverse = db.InventoryTransactions.Single(t => t.SourceType == InventoryTransactionType.BillReverse);
        Assert.Equal(purchase.Id, reverse.ReversesTransactionId);
    }

    // #10: a StockId that is not on the bill is an error, not a silent zeroing
    [Fact]
    public void BillEditWithAStockIdThatIsNotOnTheBill_IsRefused_NothingChanges()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 12, 10);
        var before = w.Snapshot();
        var o = w.EditBill(bill, w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 12, 10, stockId: 424242));
        Assert.False(o.IsSuccess);
        Assert.Contains("does not belong to this bill", o.Message);
        Assert.Equal(before, w.Snapshot());
    }

    // #6: restore onto a re-based batch keeps purchased >= on hand; edit + reverse never go negative
    [Fact]
    public void SplitThenUngiveThenEditThenReverse_NeverBreaksPurchasedQuantity()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.GiveDose1().IsSuccess);                       // 9 on hand
        int stockId = w.Stocks().Single().Id;
        using (var db = w.NewContext()) Assert.True(Result.Of(w.Bills(db).SplitConsumed(bill, stockId).GetAwaiter().GetResult()).IsSuccess);
        w.Clean("after split");
        Assert.True(w.UngiveDose1().IsSuccess);                     // unit restored onto the re-based batch
        w.Clean("after ungive of the split dose");
        Assert.True(w.EditBill(bill, w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 6, 10)).IsSuccess);
        w.Clean("after edit");
        Assert.True(w.ReverseBill(bill).IsSuccess);
        w.Clean("after reverse");
    }

    // runtime single-writer guard
    [Fact]
    public void RuntimeGuard_RefusesAnyInventoryWriteThatDidNotComeFromTheService()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        using (var db = w.NewContext())
        {
            db.Stocks.First().Quantity = 999;                       // a "rogue" direct write
            var ex = Assert.Throws<InventoryInvariantException>(() => db.SaveChanges());
            Assert.Contains("UNAUTHORISED INVENTORY WRITE", ex.Message);
        }
        using (var db = w.NewContext())
        {
            db.BrandAmounts.First().Quantity = 999;
            Assert.Throws<InventoryInvariantException>(() => db.SaveChanges());
        }
        using (var db = w.NewContext())
        {
            db.InventoryTransactions.Add(new InventoryTransaction { DoctorId = 1, ClinicId = 1, BrandId = 1, QuantityDelta = 5, EventDate = StockWorld.Today });
            Assert.Throws<InventoryInvariantException>(() => db.SaveChanges());
        }
        using (var db = w.NewContext())
        {
            db.BrandAmounts.First().SalePrice = 55;                 // a price edit is NOT an inventory write
            db.SaveChanges();
        }
        Assert.Equal(5, w.Shelf(w.ClinicA));
        w.Clean("after rogue writes were refused");
    }

    // guard: a brand or clinic that was EVER stocked cannot be deleted (cascade would erase batches)
    [Fact]
    public void BrandAndClinicWithZeroQuantityHistory_CannotBeDeleted()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 3, 10);
        w.GiveViaService(w.ClinicA, 3);                             // batch is now at zero, history remains
        using (var db = w.NewContext())
        {
            var r = new BrandController(db, null!).Delete(w.BrandId).GetAwaiter().GetResult();
            Assert.False(r.IsSuccess); Assert.Contains("stock history", r.Message);
        }
        using (var db = w.NewContext())
        {
            var r = new ClinicController(db, null!).Delete((int)w.ClinicA);
            Assert.False(r.IsSuccess); Assert.Contains("stock history", r.Message);
        }
    }

    // transfer delete keeps the XFER bill when a later bill edit put a live line on it
    [Fact]
    public void TransferDelete_KeepsTheXferBill_WhenItCarriesLiveStockFromABillEdit()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 10, 10));
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, Kit.XItem(w.BrandId, "L1", 4)).IsSuccess);
        int xferBillId; using (var db = w.NewContext()) xferBillId = db.Bills.Single(b => b.BillNo.StartsWith("XFER-")).Id;
        Assert.True(w.EditBill(xferBillId, w.ClinicB, 0, Kit.Line(w.BrandId, "L1", 4, 10), Kit.Line(w.BrandId, "LNEW", 3, 10)).IsSuccess);
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        using var chk = w.NewContext();
        Assert.True(chk.Bills.Any(b => b.Id == xferBillId), "the bill carrying live stock must stay");
        Assert.Equal(3, chk.Stocks.Where(s => s.BatchLot == "LNEW").Sum(s => s.Quantity));
        InventoryInvariants.AssertClean(chk, "after the transfer delete");
    }
}
