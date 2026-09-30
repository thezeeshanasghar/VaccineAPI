using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

public class Adversarial_SplitAndPaymentTests
{
    [Fact]
    public void SplitConsumed_MustNotCreateLiveUnits_ShelfStaysAtPhysical60()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 100, 10);
        w.GiveViaService(w.ClinicA, 40);
        int stockId = w.Stocks().Single().Id;
        using (var db = w.NewContext())
        {
            var r = Result.Of(w.Bills(db).SplitConsumed(bill, stockId).GetAwaiter().GetResult());
            Assert.True(r.IsSuccess, r.Message);
        }
        Assert.Equal(60, w.Stocks().Sum(s => s.Quantity));
        Assert.Equal(60, w.BaQty(w.ClinicA));
        w.Clean("after split-consumed");
    }

    [Fact]
    public void SplitConsumed_ThenEditOfOriginalBill_KeepsShelf()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 100, 10);
        w.GiveViaService(w.ClinicA, 40);
        int stockId = w.Stocks().Single().Id;
        using (var db = w.NewContext())
            Assert.True(Result.Of(w.Bills(db).SplitConsumed(bill, stockId).GetAwaiter().GetResult()).IsSuccess);
        Assert.True(w.EditBill(bill, w.ClinicA, 0, Line(w.BrandId, "L1", 80, 10)).IsSuccess);
        Assert.Equal(80, w.Stocks().Sum(s => s.Quantity));
        w.Clean("split then edit");
    }

    [Fact]
    public void AddPayment_DoesNotTouchStock_AndOverpaymentRejected()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10);
        long sup; using (var db = w.NewContext()) { var sp = new Supplier { Name = "S", DoctorId = w.DoctorId }; db.Suppliers.Add(sp); db.SaveChanges(); sup = sp.Id; }
        var snap = w.Snapshot();
        using (var db = w.NewContext())
        {
            var ok = Result.Of(w.Bills(db).AddPayment(bill, new VaccineAPI.ModelDTO.SupplierPaymentCreateDTO { SupplierId = sup, Amount = 60, PaymentMethod = "Cash", PaymentDate = StockWorld.Today }).GetAwaiter().GetResult());
            Assert.True(ok.IsSuccess, ok.Message);
        }
        using (var db = w.NewContext())
        {
            var bad = Result.Of(w.Bills(db).AddPayment(bill, new VaccineAPI.ModelDTO.SupplierPaymentCreateDTO { SupplierId = sup, Amount = 60, PaymentMethod = "Cash", PaymentDate = StockWorld.Today }).GetAwaiter().GetResult());
            Assert.False(bad.IsSuccess);
        }
        var after = w.Snapshot();
        Assert.Equal(snap.Stocks, after.Stocks); Assert.Equal(snap.Ledger, after.Ledger);
        w.Clean("payments");
    }
}
