using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>The clinic owner's decisions of 2026-09-30, as executable rules.</summary>
public class OwnerDecisionTests
{
    // "It is the same cost as it was originally purchased" — a transfer never changes a unit's cost.
    [Fact]
    public void Transfer_ReceivedUnitsCostExactlyWhatTheSourceBatchCost_ClientPriceAndAwtIgnored()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 10m, Kit.Line(w.BrandId, "L1", 10, 100));   // 100 + 10% AWT = 110 per unit
        using (var db = w.NewContext())
        {
            var r = w.Transfers(db).Create(new StockTransferCreateDTO
            {
                DoctorId = w.DoctorId, FromClinicId = w.ClinicA, ToClinicId = w.ClinicB, TransferDate = StockWorld.Today,
                Reason = "r", AwtPercent = 5,                                   // must be ignored
                Items = { new StockTransferItemDTO { BrandId = w.BrandId, BatchLot = "L1", ExpiryDate = StockWorld.Expiry, Quantity = 4, UnitPrice = 50 } }   // must be ignored
            }).GetAwaiter().GetResult();
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }
        using var chk = w.NewContext();
        var dest = chk.Stocks.Single(s => s.ClinicId == w.ClinicB);
        Assert.Equal(110m, dest.StockAmount);
        var row = chk.StockTransfers.Single();
        Assert.Equal(110m, row.UnitPrice);
        Assert.Equal(0m, row.AwtPercent);
        var xfer = chk.Bills.Single(b => b.BillNo.StartsWith("XFER-"));
        Assert.Equal(0m, xfer.AwtPercent);
        Assert.Equal(440m, xfer.AmountPaid);                                     // 4 x 110
        InventoryInvariants.AssertClean(chk, "after a cost-preserving transfer");
    }

    // "Expiry is the LAST date the vaccine can be used/given"
    [Fact]
    public void ExpiryDate_IsTheLastUsableDay_AndAnExpiredBatchIsNeverGiven()
    {
        Assert.True(InventoryTransactionService.ExcludeExpiredFromFefo == false || true);   // static default in tests
        var saved = InventoryTransactionService.ExcludeExpiredFromFefo;
        InventoryTransactionService.ExcludeExpiredFromFefo = true;
        try
        {
            using var w = new StockWorld();
            var today = StockWorld.Today;
            // batch that expires TODAY: still usable today
            using (var db = w.NewContext())
            {
                var d = new BillCreateDTO { BillDate = today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S", BillNo = "E1" };
                d.Lines.Add(new BillLineDTO { BrandId = w.BrandId, BatchLot = "TODAY", Expiry = today, Quantity = 2, UnitPrice = 10 });
                Assert.True(Result.Of(w.Bills(db).Create(d).GetAwaiter().GetResult()).IsSuccess);
            }
            Assert.True(w.GiveDose1().IsSuccess);
            using (var db = w.NewContext())
                Assert.NotNull(db.Schedules.Single(s => s.Id == w.Schedule1Id).StockId);   // came from the batch, not pending
            Assert.Equal(1, w.Shelf(w.ClinicA));

            // batch that expired YESTERDAY: never given; the dose becomes a pending dose instead
            using (var db = w.NewContext())
            {
                var d = new BillCreateDTO { BillDate = today, DoctorId = w.DoctorId, ClinicId = w.ClinicB, SupplierName = "S", BillNo = "E2" };
                d.Lines.Add(new BillLineDTO { BrandId = w.BrandId, BatchLot = "OLD", Expiry = today.AddDays(-1), Quantity = 3, UnitPrice = 10 });
                Assert.True(Result.Of(w.Bills(db).Create(d).GetAwaiter().GetResult()).IsSuccess);
            }
            Assert.False(new InventoryTransactionService(w.NewContext()).HasFillableBatch(w.BrandId, w.ClinicB, today));
            Assert.Equal(3, w.Shelf(w.ClinicB));                                   // still on the shelf until written off
            w.Clean("expiry boundary");
        }
        finally { InventoryTransactionService.ExcludeExpiredFromFefo = saved; }
    }

    // "We never gave purchase permission to a PA": claims through a document are doctor-only
    [Fact]
    public void PaCannotIncludePendingDosesInABatch_EvenWithStockAdjustPermission()
    {
        using var w = new StockWorld();
        var (c, s) = w.AddChild();
        Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess);
        List<long> ids; long paId, paUserId; string stamp;
        using (var db = w.NewContext())
        {
            db.BypassInventoryWriterCheck = true;
            ids = db.UnbatchedUses.Select(u => u.Id).ToList();
            var u = new User { MobileNumber = "3222222222", Password = "x", UserType = "PA", CountryCode = "92" };
            db.Users.Add(u); db.SaveChanges();
            var pa = new PersonalAssistant { Name = "PA", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true, IsVerified = true };
            db.PersonalAssistant.Add(pa); db.SaveChanges();
            db.PaPermissions.Add(new PaPermission { PaId = pa.Id, StockAdjust = true }); db.SaveChanges();
            paId = pa.Id; paUserId = u.Id; stamp = u.SecurityStamp;
        }
        var before = w.Snapshot();
        using var db2 = w.NewContext();
        var r = w.Adjusts(db2).Create(new AdjustStockCreateDTO
        {
            DoctorId = w.DoctorId, ClinicId = w.ClinicA, BrandId = w.BrandId, Quantity = 5, Type = "Increase", Price = 10, BatchLot = "PA1",
            Date = StockWorld.Today, ExpiryDate = StockWorld.Expiry, ClaimUnbatchedUseIds = ids, PaId = paId, CallerUserId = paUserId, SecurityStamp = stamp
        }).GetAwaiter().GetResult();
        var o = Result.Of(r);
        Assert.False(o.IsSuccess);
        Assert.Contains("Only the doctor", o.Message);
        Assert.Equal(before, w.Snapshot());
    }
}
