using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

public class Adversarial_BillTests
{
    static long A(StockWorld w) => w.ClinicA;

    // 100 bought at A (lot L1), 40 consumed => shelf 60
    static int Seed100Use40(StockWorld w)
    {
        int bill = w.PostBill(w.ClinicA, 100, 10);
        w.GiveViaService(w.ClinicA, 40);
        w.Clean("seed");
        return bill;
    }

    [Fact]
    public void PriceOnlyEdit_NoStockMovement_LedgerHasOnlyZeroDeltaAudit()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var before = w.Ledger().Where(l => l.QuantityDelta != 0).Sum(l => l.QuantityDelta);
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 100, 12));
        Assert.True(o.IsSuccess, o.Message);
        var s = w.Stocks().Single();
        Assert.Equal(60, s.Quantity); Assert.Equal(100, s.OriginalQuantity); Assert.Equal(12m, s.StockAmount);
        Assert.Equal(60, w.BaQty(A(w)));
        Assert.Equal(before, w.Ledger().Sum(l => l.QuantityDelta));
        Assert.All(w.Ledger(InventoryTransactionType.BillEdit), l => Assert.Equal(0, l.QuantityDelta));
        w.Clean("price edit");
    }

    [Fact]
    public void QuantityUp_AddsExactlyDelta()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 150, 10));
        Assert.True(o.IsSuccess, o.Message);
        var s = w.Stocks().Single();
        Assert.Equal(110, s.Quantity); Assert.Equal(150, s.OriginalQuantity);
        Assert.Equal(110, w.BaQty(A(w)));
        Assert.Equal(new[] { 50 }, w.Ledger(InventoryTransactionType.BillEdit).Where(l => l.QuantityDelta != 0).Select(l => l.QuantityDelta).ToArray());
        w.Clean("qty up");
    }

    [Fact]
    public void QuantityDown_ButNotBelowConsumed()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 50, 10));
        Assert.True(o.IsSuccess, o.Message);
        var s = w.Stocks().Single();
        Assert.Equal(10, s.Quantity); Assert.Equal(50, s.OriginalQuantity); Assert.False(s.IsClosed);
        Assert.Equal(10, w.BaQty(A(w)));
        w.Clean("qty down");
    }

    [Fact]
    public void QuantityDown_ToExactlyConsumed_ClosesBatch_AndRestoreReopens()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 40, 10)).IsSuccess);
        var s = w.Stocks().Single();
        Assert.Equal(0, s.Quantity); Assert.Equal(40, s.OriginalQuantity); Assert.True(s.IsClosed);
        Assert.Equal(0, w.BaQty(A(w)));
        w.Clean("down to consumed");
        // raise again: batch must reopen and be usable
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 45, 10)).IsSuccess);
        s = w.Stocks().Single();
        Assert.Equal(5, s.Quantity); Assert.False(s.IsClosed);
        using var db = w.NewContext();
        Assert.True(new InventoryTransactionService(db).HasFillableBatch(w.BrandId, A(w)));
        w.Clean("reopened");
    }

    [Fact]
    public void QuantityBelowConsumed_Rejected_ZeroChange()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var snap = w.Snapshot();
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 30, 99));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("rejected edit");
    }

    [Fact]
    public void LotRelabel_Unconsumed_NewLotUsableOldLotGone()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 100, 10);
        int sid = w.Stocks().Single().Id;
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L2", 100, 10, stockId: sid));
        Assert.True(o.IsSuccess, o.Message);
        var s = w.Stocks().Single();
        Assert.Equal("L2", s.BatchLot); Assert.Equal(100, s.Quantity); Assert.Equal(100, s.OriginalQuantity);
        Assert.Equal(100, w.BaQty(A(w)));
        Assert.True(w.Sell(A(w), SaleItem(w.BrandId, "L2", 10)).IsSuccess);
        Assert.False(w.Sell(A(w), SaleItem(w.BrandId, "L1", 1)).IsSuccess);
        Assert.Equal(90, w.Shelf(A(w)));
        w.Clean("relabel");
    }

    [Fact]
    public void LotRelabel_WithoutStockId_IsCloseAndReopen_ShelfTotalUnchanged()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 100, 10);
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L2", 100, 10)).IsSuccess);
        Assert.Equal(100, w.Shelf(A(w))); Assert.Equal(100, w.BaQty(A(w)));
        Assert.Equal(100, w.Stocks().Sum(x => x.OriginalQuantity));
        Assert.Equal(100, w.Stocks().Single(x => x.BatchLot == "L2").Quantity);
        w.Clean("relabel without stockId");
    }

    [Fact]
    public void LotRelabel_OfConsumedLine_RejectedZeroChange_PerDesignDoc()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var snap = w.Snapshot();
        Assert.False(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L2", 100, 10)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("relabel consumed");
    }

    [Fact]
    public void ExpiryRelabel_Unconsumed_NoMovement()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 100, 10);
        int sid = w.Stocks().Single().Id;
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 100, 10, stockId: sid, expiry: new DateTime(2036, 6, 1)));
        Assert.True(o.IsSuccess, o.Message);
        var s = w.Stocks().Single();
        Assert.Equal(new DateTime(2036, 6, 1), s.Expiry); Assert.Equal(100, s.Quantity); Assert.Equal(100, s.OriginalQuantity);
        Assert.Single(w.Stocks());
        w.Clean("expiry relabel");
    }

    [Fact]
    public void LineRemoved_Unconsumed_ZeroesThatBatchOnly()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        var (o0, bill) = w.CreateBill(A(w), 0, Line(w.BrandId, "L1", 100, 10), Line(b2, "Y1", 5, 20));
        Assert.True(o0.IsSuccess, o0.Message);
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 100, 10));
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(100, w.Shelf(A(w))); Assert.Equal(0, w.Shelf(A(w), b2));
        Assert.Equal(0, w.BaQty(A(w), b2)); Assert.Equal(100, w.BaQty(A(w)));
        var y = w.Stocks(b2).Single();
        Assert.Equal(0, y.Quantity); Assert.Equal(0, y.OriginalQuantity); Assert.True(y.IsClosed);
        w.Clean("line removed");
    }

    [Fact]
    public void LineRemoved_Consumed_RejectedZeroChange()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        var (o0, bill) = w.CreateBill(A(w), 0, Line(w.BrandId, "L1", 100, 10), Line(b2, "Y1", 5, 20));
        Assert.True(o0.IsSuccess, o0.Message);
        Assert.True(w.Sell(A(w), SaleItem(b2, "Y1", 2)).IsSuccess);
        var snap = w.Snapshot();
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 100, 10));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("consumed line removal rejected");
    }

    [Fact]
    public void BrandChange_Unconsumed_MovesStockToOtherBrand()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        int bill = w.PostBill(A(w), 20, 10);
        var o = w.EditBill(bill, A(w), 0, Line(b2, "L1", 20, 10));
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(0, w.Shelf(A(w))); Assert.Equal(20, w.Shelf(A(w), b2));
        Assert.Equal(0, w.BaQty(A(w))); Assert.Equal(20, w.BaQty(A(w), b2));
        w.Clean("brand change");
    }

    [Fact]
    public void BrandChange_OfConsumedLine_RejectedZeroChange()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        int bill = w.PostBill(A(w), 20, 10);
        w.GiveViaService(A(w), 3);
        var snap = w.Snapshot();
        var o = w.EditBill(bill, A(w), 0, Line(b2, "L1", 20, 10));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("brand change of consumed line");
    }

    [Fact]
    public void BrandChange_WithStockIdSent_SameAsWithout()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        int bill = w.PostBill(A(w), 20, 10);
        int sid = w.Stocks().Single().Id;
        var o = w.EditBill(bill, A(w), 0, Line(b2, "L1", 20, 10, stockId: sid));
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(0, w.Shelf(A(w))); Assert.Equal(20, w.Shelf(A(w), b2));
        w.Clean("brand change with stockId");
    }

    [Fact]
    public void TwoIdenticalLinesInOneBill_MergeToOneBatch_AndEditIsIdempotent()
    {
        using var w = new StockWorld();
        var (o0, bill) = w.CreateBill(A(w), 0, Line(w.BrandId, "L1", 10, 10), Line(w.BrandId, "L1", 5, 10));
        Assert.True(o0.IsSuccess, o0.Message);
        var st = w.Stocks();
        Assert.Equal(15, st.Sum(s => s.Quantity)); Assert.Equal(15, w.BaQty(A(w)));
        w.Clean("two lines created");
        // resend the very same two lines: physical result must stay 15
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 10, 10), Line(w.BrandId, "L1", 5, 10));
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(15, w.Shelf(A(w))); Assert.Equal(15, w.BaQty(A(w)));
        Assert.Equal(15, w.Stocks().Sum(s => s.OriginalQuantity));
        w.Clean("two lines resent");
    }

    [Fact]
    public void TwoIdenticalLines_ResentAfterConsumption_MustNotBeRejectedAsBelowConsumed()
    {
        // 10 + 5 = 15 in one batch, 12 consumed. Resending the SAME two lines is a no-op and
        // must succeed: the bill total (15) is still >= consumed (12).
        using var w = new StockWorld();
        var (o0, bill) = w.CreateBill(A(w), 0, Line(w.BrandId, "L1", 10, 10), Line(w.BrandId, "L1", 5, 10));
        Assert.True(o0.IsSuccess, o0.Message);
        w.GiveViaService(A(w), 12);
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 10, 10), Line(w.BrandId, "L1", 5, 10));
        Assert.True(o.IsSuccess, "no-op edit rejected: " + o.Message);
        Assert.Equal(3, w.Shelf(A(w)));
        w.Clean("resent duplicates after consumption");
    }

    [Fact]
    public void EditTwiceInARow_OriginalQuantityNeverAccumulates()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        foreach (var q in new[] { 100, 100, 120, 120, 100, 90, 90 })
        {
            Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", q, 10)).IsSuccess);
            var s = w.Stocks().Single();
            Assert.Equal(q, s.OriginalQuantity);
            Assert.Equal(q - 40, s.Quantity);
            Assert.Equal(q - 40, w.BaQty(A(w)));
            w.Clean("after edit to " + q);
        }
    }

    [Fact]
    public void EditWithAwt_StockAmountDoesNotCompound()
    {
        using var w = new StockWorld();
        var (o0, bill) = w.CreateBill(A(w), 10m, Line(w.BrandId, "L1", 10, 100));
        Assert.True(o0.IsSuccess, o0.Message);
        Assert.Equal(110m, w.Stocks().Single().StockAmount);
        for (int i = 0; i < 4; i++)
        {
            Assert.True(w.EditBill(bill, A(w), 10m, Line(w.BrandId, "L1", 10, 100)).IsSuccess);
            Assert.Equal(110m, w.Stocks().Single().StockAmount);
        }
        Assert.True(w.EditBill(bill, A(w), 20m, Line(w.BrandId, "L1", 10, 100)).IsSuccess);
        Assert.Equal(120m, w.Stocks().Single().StockAmount);
        Assert.True(w.EditBill(bill, A(w), 0m, Line(w.BrandId, "L1", 10, 100)).IsSuccess);
        Assert.Equal(100m, w.Stocks().Single().StockAmount);
        Assert.Equal(10, w.Stocks().Single().OriginalQuantity);
        w.Clean("awt edits");
    }

    [Fact]
    public void EditWithAwt_TotalPayableAfterEditEqualsQtyTimesUnitTimesAwt()
    {
        using var w = new StockWorld();
        var (o0, bill) = w.CreateBill(A(w), 10m, Line(w.BrandId, "L1", 10, 100));
        Assert.True(o0.IsSuccess);
        Assert.True(w.EditBill(bill, A(w), 10m, Line(w.BrandId, "L1", 20, 100)).IsSuccess);
        using var db = w.NewContext();
        var b = db.Bills.Include(x => x.Stocks).Single();
        Assert.Equal(2200m, b.Stocks.Sum(s => s.OriginalQuantity * s.StockAmount));
        Assert.Equal(200m, b.AwtAmount);
    }

    [Fact]
    public void EditToBrandNewLine_CreatesBatchAndKeepsOld()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 10, 10);
        var o = w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 10, 10), Line(w.BrandId, "L2", 7, 11));
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(17, w.Shelf(A(w))); Assert.Equal(17, w.BaQty(A(w)));
        Assert.Equal(2, w.Stocks().Count);
        w.Clean("new line");
    }

    [Fact]
    public void EditWithZeroQuantityOrPrice_RejectedZeroChange()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var snap = w.Snapshot();
        Assert.False(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 0, 10)).IsSuccess);
        Assert.False(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 100, 0)).IsSuccess);
        Assert.False(w.EditBill(9999, A(w), 0, Line(w.BrandId, "L1", 100, 10)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void EditingBill_ThenGiveThenEditAgain_ShelfStaysConsistent()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 10, 10);
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 12, 10)).IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(11, w.Shelf(A(w)));
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 5, 10)).IsSuccess);
        var s = w.Stocks().Single();
        Assert.Equal(4, s.Quantity); Assert.Equal(5, s.OriginalQuantity);
        Assert.True(w.UngiveDose1().IsSuccess);
        s = w.Stocks().Single();
        Assert.Equal(5, s.Quantity); Assert.Equal(5, s.OriginalQuantity);
        w.Clean("edit/give/edit/ungive");
    }

    [Fact]
    public void RelabelAfterGive_Rejected_ZeroChange_AndUngiveStillRestoresSameBatch()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 3, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.EditBill(bill, A(w), 0, Line(w.BrandId, "NEWLOT", 3, 10)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.True(w.UngiveDose1().IsSuccess);
        var s = w.Stocks().Single();
        Assert.Equal("L1", s.BatchLot); Assert.Equal(3, s.Quantity);
        w.Clean("relabel rejected then ungive");
    }

    // ------------------------------------------------------------ bill reverse
    [Fact]
    public void ReverseBill_AfterPartialConsumption_IsRefused_ThenSplitAndReverseRemovesOnlyRemaining()
    {
        using var w = new StockWorld();
        int bill = Seed100Use40(w);
        var snap = w.Snapshot();
        var refused = w.ReverseBill(bill);
        Assert.False(refused.IsSuccess);
        Assert.Contains("40 unit(s) of this bill were already used", refused.Message);
        Assert.Equal(snap, w.Snapshot());                       // refused = zero change

        int stockId = w.Stocks().Single().Id;
        using (var db = w.NewContext()) Assert.True(Result.Of(w.Bills(db).SplitConsumed(bill, stockId).GetAwaiter().GetResult()).IsSuccess);
        var o = w.ReverseBill(bill);
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(0, w.Shelf(A(w)));
        Assert.Equal(0, w.BaQty(A(w)));
        w.Clean("bill reversed");
        // purchase again: shelf must be exactly the new purchase
        w.PostBill(A(w), 10, 10);
        Assert.Equal(10, w.Shelf(A(w))); Assert.Equal(10, w.BaQty(A(w)));
        w.Clean("after re-purchase");
    }

    [Fact]
    public void ReverseBill_WhileADoseFromItIsGiven_IsRefused_SoUngiveCanNeverResurrectStock()
    {
        // The old behaviour let a bill be reversed and a later ungive put the unit back on the
        // orphaned batch. Now the bill cannot be reversed while any of its units are in use.
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.False(w.ReverseBill(bill).IsSuccess);
        Assert.Equal(4, w.Shelf(A(w)));
        Assert.True(w.UngiveDose1().IsSuccess);               // dose returned first ...
        Assert.Equal(5, w.Shelf(A(w)));
        Assert.True(w.ReverseBill(bill).IsSuccess);           // ... then the bill can be cancelled
        Assert.Equal(0, w.Shelf(A(w)));
        Assert.Equal(w.Shelf(A(w)), w.BaQty(A(w)));
        w.Clean("bill reverse after the dose was returned");
    }

    [Fact]
    public void ReverseBill_AfterPartTransferredOut_IsRefused_DestinationUntouched()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 10, 10);
        Assert.True(w.Transfer(A(w), w.ClinicB, XItem(w.BrandId, "L1", 4)).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.ReverseBill(bill).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.Equal(6, w.Shelf(A(w))); Assert.Equal(4, w.Shelf(w.ClinicB));
        w.Clean("refused bill reverse after transfer");
    }

    [Fact]
    public void ReverseBill_Twice_SecondIsNotFoundAndChangesNothing()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 10, 10);
        Assert.True(w.ReverseBill(bill).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.ReverseBill(bill).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void ReverseBill_FullyConsumed_IsRefused_ThenSplitLeavesNothingToReverse()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 3, 10);
        w.GiveViaService(A(w), 3);
        Assert.False(w.ReverseBill(bill).IsSuccess);
        int stockId = w.Stocks().Single().Id;
        using (var db = w.NewContext()) Assert.True(Result.Of(w.Bills(db).SplitConsumed(bill, stockId).GetAwaiter().GetResult()).IsSuccess);
        Assert.True(w.ReverseBill(bill).IsSuccess);
        Assert.Equal(0, w.BaQty(A(w)));
        w.Clean("fully consumed bill split then reversed");
    }

    [Fact]
    public void ReverseBill_ThenSaleOfItsLot_Rejected()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 10, 10);
        Assert.True(w.ReverseBill(bill).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.Sell(A(w), SaleItem(w.BrandId, "L1", 1)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void ReverseBill_WithEditsInBetween_LedgerSumsToZero()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(A(w), 10, 10);
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 25, 10)).IsSuccess);
        Assert.True(w.EditBill(bill, A(w), 0, Line(w.BrandId, "L1", 8, 10)).IsSuccess);
        Assert.True(w.ReverseBill(bill).IsSuccess);
        Assert.Equal(0, w.Ledger().Sum(l => l.QuantityDelta));
        w.Clean("edit edit reverse");
    }
}
