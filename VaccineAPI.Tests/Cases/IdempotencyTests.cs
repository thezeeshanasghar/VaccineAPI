using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Invariant 9: a repeated request (double click, refresh, retry) never posts twice.</summary>
public class IdempotencyTests
{
    private static BillCreateDTO BillDto(StockWorld w, string? key, int qty = 10)
    {
        var d = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S",
            BillNo = "B-" + Guid.NewGuid().ToString("N")[..6], ClientRequestId = key };
        d.Lines.Add(Kit.Line(w.BrandId, "L1", qty, 10));
        return d;
    }

    [Fact]
    public void BillCreate_SameClientRequestIdTwice_PostsOnce_AndReplaysTheAnswer()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext())
        {
            var first = w.Bills(db).Create(BillDto(w, "req-1")).GetAwaiter().GetResult();
            Assert.True(Result.Of(first).IsSuccess);
        }
        using (var db = w.NewContext())
        {
            var second = w.Bills(db).Create(BillDto(w, "req-1")).GetAwaiter().GetResult();
            Assert.True(Result.Of(second).IsSuccess);
            var body = Assert.IsType<Newtonsoft.Json.Linq.JObject>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(second).Value);
            Assert.True(body.Value<bool>("IsDuplicate"));
        }
        using var check = w.NewContext();
        Assert.Equal(1, check.Bills.Count());
        Assert.Equal(10, w.Shelf(w.ClinicA));
        w.Clean("after a duplicate bill submit");
    }

    [Fact]
    public void BillCreate_DifferentClientRequestIds_AreTwoLegitimatePurchases()
    {
        using var w = new StockWorld();
        foreach (var k in new[] { "a", "b" })
        {
            using var db = w.NewContext();
            Assert.True(Result.Of(w.Bills(db).Create(BillDto(w, k)).GetAwaiter().GetResult()).IsSuccess);
        }
        Assert.Equal(20, w.Shelf(w.ClinicA));
        w.Clean("two distinct purchases");
    }

    [Fact]
    public void SaleTransferAdjust_SameClientRequestId_PostOnce()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 50, 10));

        for (int i = 0; i < 3; i++)
        {
            using var db = w.NewContext();
            w.Sales(db).Create(new DirectSaleCreateDTO
            {
                DoctorId = w.DoctorId, ClinicId = w.ClinicA, ClientName = "c", SaleDate = StockWorld.Today, ClientRequestId = "sale-1",
                Items = { Kit.SaleItem(w.BrandId, "L1", 5) }
            }).GetAwaiter().GetResult();
        }
        Assert.Equal(45, w.Shelf(w.ClinicA));

        for (int i = 0; i < 3; i++)
        {
            using var db = w.NewContext();
            w.Transfers(db).Create(new StockTransferCreateDTO
            {
                DoctorId = w.DoctorId, FromClinicId = w.ClinicA, ToClinicId = w.ClinicB, TransferDate = StockWorld.Today, Reason = "r",
                ClientRequestId = "xfer-1", Items = { Kit.XItem(w.BrandId, "L1", 10) }
            }).GetAwaiter().GetResult();
        }
        Assert.Equal(35, w.Shelf(w.ClinicA));
        Assert.Equal(10, w.Shelf(w.ClinicB));

        for (int i = 0; i < 3; i++)
        {
            using var db = w.NewContext();
            w.Adjusts(db).Create(new AdjustStockCreateDTO
            {
                DoctorId = w.DoctorId, ClinicId = w.ClinicA, BrandId = w.BrandId, Quantity = 4, Type = "Increase", Price = 10, BatchLot = "AJ",
                Date = StockWorld.Today, ExpiryDate = StockWorld.Expiry, ClearUnbatchedBacklog = false, ClientRequestId = "adj-1"
            }).GetAwaiter().GetResult();
        }
        Assert.Equal(39, w.Shelf(w.ClinicA));
        w.Clean("after repeated sale/transfer/adjust submits");
    }

    [Fact]
    public void FourConcurrentSubmissionsOfTheSameRequest_CommitExactlyOnce()
    {
        using var w = new StockWorld();
        using var file = new FileDb(w);
        var results = new bool[4];
        var threads = Enumerable.Range(0, 4).Select(i => new Thread(() =>
        {
            try
            {
                using var db = file.NewContext();
                var r = w.Bills(db).Create(BillDto(w, "race-1")).GetAwaiter().GetResult();
                results[i] = Result.Of(r).IsSuccess;
            }
            catch { results[i] = false; }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        using var check = file.NewContext();
        Assert.Equal(1, check.Bills.Count());
        Assert.Equal(10, check.Stocks.Sum(s => s.Quantity));
        InventoryInvariants.AssertClean(check, "after the concurrent duplicate submissions");
    }
}
