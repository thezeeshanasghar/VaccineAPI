using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// The five reproduced production failures. Written FIRST, against the unmodified code, and
/// expected to FAIL there. After the architectural fix the very same tests must pass.
/// </summary>
public class FiveKnownFailureTests
{
    // ------------------------------------------------------------------ CASE 1
    [Fact]
    public async Task Case1_EditingBillPriceDoesNotChangePhysicalStock()
    {
        using var w = new StockWorld();
        int billId = w.PostBill(w.ClinicA, qty: 100, unitPrice: 10);
        w.GiveViaService(w.ClinicA, doses: 40);
        using (var db = w.NewContext()) InventoryInvariants.AssertClean(db, "before the edit");

        using (var db = w.NewContext())
        {
            var r = await w.Bills(db).Update(billId, new BillUpdateDTO
            {
                BillNo = "EDITED", BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA,
                SupplierName = "Sup", AwtPercent = 0,
                Lines = { new BillLineDTO { BrandId = w.BrandId, BatchLot = "L1", Expiry = StockWorld.Expiry, Quantity = 100, UnitPrice = 12 } }
            });
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }

        using var check = w.NewContext();
        var stock = check.Stocks.Single();
        Assert.Equal(60, stock.Quantity);                       // physical shelf
        Assert.Equal(100, stock.OriginalQuantity);              // never accumulated by an edit
        Assert.Equal(60, check.BrandAmounts.Single(b => b.ClinicId == w.ClinicA).Quantity);
        Assert.False(stock.IsClosed);
        InventoryInvariants.AssertClean(check, "after the price-only edit");
    }

    // ------------------------------------------------------------------ CASE 2
    [Fact]
    public void Case2_RejectedGiveProducesZeroInventoryMovement()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, qty: 100, unitPrice: 10);

        // Dose 2 while dose 1 was never given => "previous dose is not given" (validation).
        using (var db = w.NewContext())
        {
            var res = w.Schedules(db).Update(new ScheduleDTO
            {
                Id = w.Schedule2Id, ChildId = w.ChildId, DoseId = (int)w.Dose2Id, DoctorId = w.DoctorId,
                IsDone = true, GivenDate = StockWorld.Today, BrandId = w.BrandId, Date = StockWorld.Today,
                ConfirmUnbatchedGive = true, IsPAApprove = true
            });
            Assert.False(res.IsSuccess);
            Assert.Contains("previous dose", res.Message ?? "");
        }

        using var check = w.NewContext();
        Assert.Equal(100, check.Stocks.Single().Quantity);
        Assert.Equal(100, check.BrandAmounts.Single(b => b.ClinicId == w.ClinicA).Quantity);
        Assert.Empty(check.InventoryTransactions.Where(t => t.SourceType == InventoryTransactionType.Administer).ToList());
        Assert.False(check.Schedules.Single(s => s.Id == w.Schedule2Id).IsDone);
        InventoryInvariants.AssertClean(check, "after a rejected give");
    }

    // ------------------------------------------------------------------ CASE 3
    [Fact]
    public void Case3_GiveWithNoStockNeverManufacturesInventoryDrift()
    {
        using var w = new StockWorld();

        // 3 doses given before any purchase exists (dose 1 only; repeated on fresh children is
        // irrelevant to inventory, so the service call the endpoint uses is invoked directly).
        w.GiveViaService(w.ClinicA, doses: 3);
        using (var db = w.NewContext())
            InventoryInvariants.AssertClean(db, "after 3 gives with no stock (no negative/phantom state allowed)");

        // The purchase then arrives: physical shelf is 10 (the 3 doses are NOT yet attributed to it).
        w.PostBill(w.ClinicA, qty: 10, unitPrice: 10);
        using var check = w.NewContext();
        InventoryInvariants.AssertClean(check, "after the purchase of 10");
        Assert.Equal(10, check.Stocks.Single().Quantity);
    }

    // ------------------------------------------------------------------ CASE 4
    [Fact]
    public async Task Case4_TransferDeleteAfterDownstreamConsumptionDoesNotRecreateStockAtSource()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, qty: 10, unitPrice: 10);

        long transferId;
        using (var db = w.NewContext())
        {
            var r = await w.Transfers(db).Create(new StockTransferCreateDTO
            {
                DoctorId = w.DoctorId, FromClinicId = w.ClinicA, ToClinicId = w.ClinicB, TransferDate = StockWorld.Today,
                Reason = "test",
                Items = { new StockTransferItemDTO { BrandId = w.BrandId, BatchLot = "L1", ExpiryDate = StockWorld.Expiry, Quantity = 10, UnitPrice = 10 } }
            });
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }
        using (var db = w.NewContext()) transferId = db.StockTransfers.Single().Id;

        w.GiveViaService(w.ClinicB, doses: 4);           // 4 consumed at the destination
        using (var db = w.NewContext()) InventoryInvariants.AssertClean(db, "before deleting the transfer");

        using (var db = w.NewContext())
            await w.Transfers(db).Delete(transferId);    // may be rejected or partially reversed; must not corrupt

        using var check = w.NewContext();
        int atA = check.BrandAmounts.Single(b => b.ClinicId == w.ClinicA).Quantity;
        int atB = check.BrandAmounts.Single(b => b.ClinicId == w.ClinicB).Quantity;
        // 10 left A; 4 were used at B; physically 6 remain and they are at B. Source must NOT get 10.
        Assert.True(atA <= 6, $"source clinic was handed {atA} units that no longer physically exist there");
        Assert.Equal(6, atA + atB);                      // total on hand is the physical 6
        InventoryInvariants.AssertClean(check, "after the transfer delete");
    }

    // ------------------------------------------------------------------ CASE 5
    [Fact]
    public async Task Case5_ReversedSaleRestoresQuantityAndFefoUsability()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, qty: 5, unitPrice: 10);

        long saleId;
        using (var db = w.NewContext())
        {
            var r = await w.Sales(db).Create(new DirectSaleCreateDTO
            {
                DoctorId = w.DoctorId, ClinicId = w.ClinicA, ClientName = "C", SaleDate = StockWorld.Today,
                Items = { new DirectSaleItemDTO { BrandId = w.BrandId, BatchLot = "L1", ExpiryDate = StockWorld.Expiry, Quantity = 5, SalePricePerUnit = 20 } }
            });
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }
        using (var db = w.NewContext())
        {
            Assert.True(db.Stocks.Single().IsClosed);   // last 5 sold => batch depleted
            saleId = db.DirectSales.Single().Id;
        }

        using (var db = w.NewContext())
        {
            var r = await w.Sales(db).Delete(saleId);
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }

        using var check = w.NewContext();
        var stock = check.Stocks.Single();
        Assert.Equal(5, stock.Quantity);
        Assert.False(stock.IsClosed);
        Assert.True(new InventoryTransactionService(check).HasFillableBatch(w.BrandId, w.ClinicA),
            "FEFO cannot find the 5 restored units");
        InventoryInvariants.AssertClean(check, "after the sale reversal");
    }
}
