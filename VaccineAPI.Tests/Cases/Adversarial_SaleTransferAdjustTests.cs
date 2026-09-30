using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

public class Adversarial_SaleTests
{
    [Fact]
    public void Purchase_Sale_ReverseSale_SaleAgain_BatchUsableAgain()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 5)).IsSuccess);
        Assert.True(w.Stocks().Single().IsClosed);
        Assert.Equal(0, w.BaQty(w.ClinicA));
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        var s = w.Stocks().Single();
        Assert.Equal(5, s.Quantity); Assert.False(s.IsClosed);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 5)).IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicA));
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        Assert.Equal(5, w.Shelf(w.ClinicA)); Assert.Equal(5, w.BaQty(w.ClinicA));
        Assert.Equal(5, w.Ledger().Sum(l => l.QuantityDelta));
        w.Clean("sale cycle");
    }

    [Fact]
    public void DeleteSaleTwice_SecondChangesNothing()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 2)).IsSuccess);
        long id = w.SaleIds().Single();
        Assert.True(w.DeleteSale(id).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.DeleteSale(id).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.Equal(5, w.Shelf(w.ClinicA));
    }

    [Fact]
    public void SaleMoreThanAvailable_Rejected_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        var snap = w.Snapshot();
        Assert.False(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 6)).IsSuccess);
        Assert.False(w.Sell(w.ClinicA, SaleItem(w.BrandId, "NOPE", 1)).IsSuccess);
        Assert.False(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 0)).IsSuccess);
        Assert.False(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", -3)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("rejected sales");
    }

    [Fact]
    public void SaleWithNoStockAtAll_Rejected_ZeroChange()
    {
        using var w = new StockWorld();
        var snap = w.Snapshot();
        Assert.False(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 1)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void SaleTwoLinesSameBatchExceedingStock_FailsAtomically_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        var o = w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 6), SaleItem(w.BrandId, "L1", 6));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.Equal(10, w.Shelf(w.ClinicA));
        w.Clean("atomic sale failure");
    }

    [Fact]
    public void SaleTwoLinesSameBatchFitting_BothApplied_AndReverseRestoresBoth()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 4), SaleItem(w.BrandId, "L1", 4)).IsSuccess);
        Assert.Equal(2, w.Shelf(w.ClinicA)); Assert.Equal(2, w.BaQty(w.ClinicA));
        Assert.Equal(2, w.Ledger(InventoryTransactionType.DirectSale).Count);
        w.Clean("two-line sale");
        Assert.True(w.DeleteSale(w.SaleIds().First()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(10, w.BaQty(w.ClinicA));
        Assert.Empty(w.SaleIds());
        w.Clean("two-line sale reversed");
    }

    [Fact]
    public void SaleLineOneOkLineTwoImpossible_NothingHappens()
    {
        using var w = new StockWorld();
        long b2 = w.AddBrand("BRANDY");
        w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        var o = w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 3), SaleItem(b2, "Y", 1));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void SaleOfLotSpanningTwoBatches_ShouldDrawAcrossBatches()
    {
        // The same lot bought on two bills = two batch rows (5 + 5). 10 units of L1 exist at the
        // clinic; a sale of 8 of L1 is physically possible (AdjustLoss supports this).
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        w.PostBill(w.ClinicA, 5, 10);
        var o = w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 8));
        Assert.True(o.IsSuccess, "sale of 8 from a 10-unit lot split over two batches refused: " + o.Message);
        Assert.Equal(2, w.Shelf(w.ClinicA));
        w.Clean("cross batch sale");
    }

    [Fact]
    public void SaleOfLotWithTwoBatches_TakesEarliestExpiryFirst()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "L1", 5, 10, expiry: new DateTime(2034, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "L1", 5, 10, expiry: new DateTime(2030, 1, 1))).o.IsSuccess);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 3)).IsSuccess);
        var early = w.Stocks().Single(s => s.Expiry == new DateTime(2030, 1, 1));
        Assert.Equal(2, early.Quantity);
        w.Clean("sale FEFO within a lot");
    }

    [Fact]
    public void SaleAtOneClinic_NeverTouchesTheOtherClinicsSameLot()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10, "L1");
        w.PostBill(w.ClinicB, 7, 10, "L1");
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 4)).IsSuccess);
        Assert.Equal(6, w.Shelf(w.ClinicA)); Assert.Equal(7, w.Shelf(w.ClinicB));
        Assert.Equal(6, w.BaQty(w.ClinicA)); Assert.Equal(7, w.BaQty(w.ClinicB));
        Assert.True(w.Sell(w.ClinicB, SaleItem(w.BrandId, "L1", 7)).IsSuccess);
        Assert.Equal(6, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        Assert.False(w.Sell(w.ClinicB, SaleItem(w.BrandId, "L1", 1)).IsSuccess);
        Assert.True(w.DeleteSale(w.SaleIds().First()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        w.Clean("two clinic sales");
    }

    [Fact]
    public void SaleAtClinicWithNoBrandAmountRow_Rejected()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext()) { db.BrandAmounts.RemoveRange(db.BrandAmounts.Where(b => b.ClinicId == w.ClinicB)); db.SaveChanges(); }
        w.PostBill(w.ClinicA, 5, 10);
        var snap = w.Snapshot();
        Assert.False(w.Sell(w.ClinicB, SaleItem(w.BrandId, "L1", 1)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void SaleReversal_AfterBatchWasEditedByBill_RestoresToThatSameBatch()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 4)).IsSuccess);
        Assert.True(w.EditBill(bill, w.ClinicA, 0, Line(w.BrandId, "L1", 20, 10)).IsSuccess);
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        var s = w.Stocks().Single();
        Assert.Equal(20, s.Quantity); Assert.Equal(20, s.OriginalQuantity);
        w.Clean("edit then sale reverse");
    }

    [Fact]
    public void BillCannotBeReversedWhileItsUnitsAreSold_SoASaleDeleteNeverResurrectsStock()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 4)).IsSuccess);
        Assert.False(w.ReverseBill(bill).IsSuccess);            // 4 units are sold: refused
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA));
        Assert.True(w.ReverseBill(bill).IsSuccess);             // now nothing is in use
        Assert.Equal(0, w.Shelf(w.ClinicA));
        w.Clean("sale returned, then bill reversed");
        Assert.Equal(w.Shelf(w.ClinicA), w.BaQty(w.ClinicA));
    }

    [Fact]
    public void SaleCostSnapshot_IsBatchStockAmount()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 10m, Line(w.BrandId, "L1", 10, 100)).o.IsSuccess);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 2)).IsSuccess);
        using var db = w.NewContext();
        Assert.Equal(110m, db.DirectSales.Single().PurchasePricePerUnit);
    }
}

public class Adversarial_TransferTests
{
    [Fact]
    public void TransferThenDeleteWhole_RestoresSourceExactly_AndDestinationEmpty()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 10)).IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.Equal(10, w.Shelf(w.ClinicB));
        Assert.Equal(0, w.BaQty(w.ClinicA)); Assert.Equal(10, w.BaQty(w.ClinicB));
        w.Clean("transferred");
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        Assert.Equal(10, w.BaQty(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicB));
        Assert.False(w.Stocks().Single(s => s.ClinicId == w.ClinicA).IsClosed);
        w.Clean("transfer reversed");
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 10)).IsSuccess);   // source batch fully usable again
        w.Clean("source usable");
    }

    [Fact]
    public void DeleteTransfer_RemovesTheDestinationXferBill()
    {
        // A reversed transfer must not leave a paid, empty "XFER-" purchase bill behind.
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 10)).IsSuccess);
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        using var db = w.NewContext();
        Assert.Empty(db.Bills.Where(b => b.BillNo.StartsWith("XFER")).ToList());
    }

    [Fact]
    public void DeleteTransfer_PartlyConsumedAtDestination_RejectedZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 10)).IsSuccess);
        Assert.True(w.Sell(w.ClinicB, SaleItem(w.BrandId, "L1", 3)).IsSuccess);
        var snap = w.Snapshot();
        var o = w.DeleteTransfer(w.TransferIds().Single());
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("rejected transfer delete");
    }

    [Fact]
    public void DeleteTransfer_AfterDestinationTransfersOnward_RejectedUntilOnwardIsReversed()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 10)).IsSuccess);
        Assert.True(w.Transfer(w.ClinicB, w.ClinicA, XItem(w.BrandId, "L1", 3)).IsSuccess);
        var ids = w.TransferIds();
        Assert.Equal(2, ids.Count);
        Assert.Equal(7, w.Shelf(w.ClinicB)); Assert.Equal(3, w.Shelf(w.ClinicA));
        var snap = w.Snapshot();
        Assert.False(w.DeleteTransfer(ids[0]).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.True(w.DeleteTransfer(ids[1]).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicB)); Assert.Equal(0, w.Shelf(w.ClinicA));
        Assert.True(w.DeleteTransfer(ids[0]).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        Assert.Equal(10, w.BaQty(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicB));
        w.Clean("onward transfer chain unwound");
    }

    [Fact]
    public void DeleteTransfer_Twice_SecondNotFoundNoChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 4)).IsSuccess);
        long id = w.TransferIds().Single();
        Assert.True(w.DeleteTransfer(id).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.DeleteTransfer(id).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void TransferMoreThanAvailable_Rejected_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        Assert.False(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 11)).IsSuccess);
        Assert.False(w.Transfer(w.ClinicA, w.ClinicA, XItem(w.BrandId, "L1", 1)).IsSuccess);
        Assert.False(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 0)).IsSuccess);
        Assert.False(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "NOPE", 1)).IsSuccess);
        Assert.False(w.Transfer(w.ClinicB, w.ClinicA, XItem(w.BrandId, "L1", 1)).IsSuccess);   // B has nothing
        Assert.Equal(snap, w.Snapshot());
        w.Clean("rejected transfers");
    }

    [Fact]
    public void TransferTwoLinesSameBatchExceeding_FailsAtomically_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        var o = w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 6), XItem(w.BrandId, "L1", 6));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("atomic transfer failure");
    }

    [Fact]
    public void TransferTwoLinesSameBatchFitting_ThenDeleteRestoresBoth()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 4), XItem(w.BrandId, "L1", 5)).IsSuccess);
        Assert.Equal(1, w.Shelf(w.ClinicA)); Assert.Equal(9, w.Shelf(w.ClinicB));
        w.Clean("two-line transfer");
        Assert.True(w.DeleteTransfer(w.TransferIds().First()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        Assert.Empty(w.TransferIds());
        w.Clean("two-line transfer reversed");
    }

    [Fact]
    public void TransferOfLotSpanningTwoBatches_ShouldDrawAcrossBatches()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        w.PostBill(w.ClinicA, 5, 10);
        var o = w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 8));
        Assert.True(o.IsSuccess, "transfer of 8 from a 10-unit lot split over two batches refused: " + o.Message);
        Assert.Equal(2, w.Shelf(w.ClinicA)); Assert.Equal(8, w.Shelf(w.ClinicB));
        w.Clean("cross batch transfer");
    }

    [Fact]
    public void SaleThenTransferThenReversalsInBothOrders()
    {
        foreach (bool saleFirst in new[] { true, false })
        {
            using var w = new StockWorld();
            w.PostBill(w.ClinicA, 10, 10);
            Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 3)).IsSuccess);
            Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 7)).IsSuccess);
            Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.Equal(7, w.Shelf(w.ClinicB));
            w.Clean("sale+transfer");
            if (saleFirst)
            {
                Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
                Assert.Equal(3, w.Shelf(w.ClinicA));
                Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
            }
            else
            {
                Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
                Assert.Equal(7, w.Shelf(w.ClinicA));
                Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
            }
            Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
            Assert.Equal(10, w.BaQty(w.ClinicA)); Assert.Equal(0, w.BaQty(w.ClinicB));
            Assert.Equal(10, w.Ledger().Sum(l => l.QuantityDelta));
            w.Clean("all reversed saleFirst=" + saleFirst);
        }
    }

    [Fact]
    public void TransferThenSellAtDestination_ThenReverseSale_ThenReverseTransfer()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 10)).IsSuccess);
        Assert.True(w.Sell(w.ClinicB, SaleItem(w.BrandId, "L1", 4)).IsSuccess);
        Assert.False(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        w.Clean("chain");
    }

    [Fact]
    public void TransferThenGiveAtSource_TransferReversalStillExact()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 6)).IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);            // doctor online clinic = A, batch has 4 left
        Assert.Equal(3, w.Shelf(w.ClinicA));
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.Equal(9, w.Shelf(w.ClinicA)); Assert.Equal(0, w.Shelf(w.ClinicB));
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA));
        w.Clean("give in the middle");
    }

    [Fact]
    public void TransferIntoClinicWithoutBrandAmount_CreatesProjection_MatchingShelf()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext()) { db.BrandAmounts.RemoveRange(db.BrandAmounts.Where(b => b.ClinicId == w.ClinicB)); db.SaveChanges(); }
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 6)).IsSuccess);
        Assert.Equal(6, w.BaQty(w.ClinicB));
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.Equal(0, w.BaQty(w.ClinicB));
        w.Clean("transfer to clinic w/o BA");
    }

    [Fact]
    public void TransferAndDestinationLotIsSameLotAsSource_TransferBackAndForthTenTimes()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        for (int i = 0; i < 10; i++)
        {
            var (from, to) = i % 2 == 0 ? (w.ClinicA, w.ClinicB) : (w.ClinicB, w.ClinicA);
            var o = w.Transfer(from, to, XItem(w.BrandId, "L1", 10));
            Assert.True(o.IsSuccess, $"hop {i}: {o.Message}");
            Assert.Equal(10, w.Shelf(to)); Assert.Equal(0, w.Shelf(from));
        }
        Assert.Equal(10, w.Ledger().Sum(l => l.QuantityDelta));
        w.Clean("ping pong");
    }
}

public class Adversarial_AdjustTests
{
    [Fact]
    public void AdjustIncrease_ThenDelete_Unconsumed_RestoresExactly()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Adjust(w.ClinicA, "Increase", 5, "ADJ").IsSuccess);
        Assert.Equal(15, w.Shelf(w.ClinicA)); Assert.Equal(15, w.BaQty(w.ClinicA));
        Assert.True(w.DeleteAdjust(w.AdjustIds().Single()).IsSuccess);
        Assert.Equal(10, w.Shelf(w.ClinicA)); Assert.Equal(10, w.BaQty(w.ClinicA));
        Assert.Equal(10, w.Ledger().Sum(l => l.QuantityDelta));
        w.Clean("increase+delete");
    }

    [Fact]
    public void AdjustIncrease_ThenConsumed_DeleteRejected_ZeroChange()
    {
        using var w = new StockWorld();
        Assert.True(w.Adjust(w.ClinicA, "Increase", 5, "ADJ").IsSuccess);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "ADJ", 2)).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.DeleteAdjust(w.AdjustIds().Single()).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.Equal(3, w.Shelf(w.ClinicA));
        w.Clean("consumed increase delete");
    }

    [Fact]
    public void AdjustIncrease_ThenGiven_DeleteRejected()
    {
        using var w = new StockWorld();
        Assert.True(w.Adjust(w.ClinicA, "Increase", 1, "ADJ").IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.DeleteAdjust(w.AdjustIds().Single()).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.True(w.DeleteAdjust(w.AdjustIds().Single()).IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA));
        w.Clean("give/ungive around adjust delete");
    }

    [Fact]
    public void AdjustLoss_AcrossTwoBatchesOfOneLot_ThenDeleteRestoresEachBatch()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.Adjust(w.ClinicA, "Loss", 8, "L1").IsSuccess);
        var st = w.Stocks();
        Assert.Equal(new[] { 0, 2 }, st.Select(s => s.Quantity).ToArray());
        Assert.Equal(2, w.BaQty(w.ClinicA));
        Assert.Equal(2, w.Ledger(InventoryTransactionType.AdjustLoss).Count);
        w.Clean("loss across batches");
        Assert.True(w.DeleteAdjust(w.AdjustIds().Single()).IsSuccess);
        st = w.Stocks();
        Assert.Equal(new[] { 5, 5 }, st.Select(s => s.Quantity).ToArray());
        Assert.All(st, s => Assert.False(s.IsClosed));
        Assert.Equal(10, w.BaQty(w.ClinicA));
        w.Clean("loss reversed");
    }

    [Fact]
    public void AdjustLoss_MoreThanAvailable_Rejected_ZeroChange_EvenAcrossBatches()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        w.PostBill(w.ClinicA, 5, 10);
        var snap = w.Snapshot();
        Assert.False(w.Adjust(w.ClinicA, "Loss", 11, "L1").IsSuccess);
        Assert.False(w.Adjust(w.ClinicA, "Loss", 1, "NOPE").IsSuccess);
        Assert.False(w.Adjust(w.ClinicB, "Loss", 1, "L1").IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("rejected losses");
    }

    [Fact]
    public void AdjustLoss_ExactlyAvailable_ClosesBatches_ThenNothingLeft()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.Adjust(w.ClinicA, "Loss", 5, "L1").IsSuccess);
        Assert.True(w.Stocks().Single().IsClosed);
        Assert.Equal(0, w.BaQty(w.ClinicA));
        var snap = w.Snapshot();
        Assert.False(w.Adjust(w.ClinicA, "Loss", 1, "L1").IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("loss to zero");
    }

    [Fact]
    public void DeleteAdjustmentTwice_SecondChangesNothing()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Adjust(w.ClinicA, "Loss", 3, "L1").IsSuccess);
        long id = w.AdjustIds().Single();
        Assert.True(w.DeleteAdjust(id).IsSuccess);
        var snap = w.Snapshot();
        Assert.False(w.DeleteAdjust(id).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.Equal(10, w.Shelf(w.ClinicA));
        w.Clean("double delete");
    }

    [Fact]
    public void AdjustInvalidInputs_Rejected_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        Assert.False(w.Adjust(w.ClinicA, "Increase", 0, "X").IsSuccess);
        Assert.False(w.Adjust(w.ClinicA, "Increase", -2, "X").IsSuccess);
        Assert.False(w.Adjust(w.ClinicA, "Increase", 2, "X", price: 0).IsSuccess);
        Assert.False(w.Adjust(w.ClinicA, "Increase", 2, "").IsSuccess);
        Assert.False(w.Adjust(w.ClinicA, "Weird", 2, "X").IsSuccess);
        Assert.False(w.Adjust(w.ClinicA, "Increase", 2, "X", brand: 99999).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
    }

    [Fact]
    public void AdjustIncrease_DoesNotInflateAnyPurchaseBill()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10, "L1");
        Assert.True(w.Adjust(w.ClinicA, "Increase", 5, "L1").IsSuccess);   // same lot label on purpose
        using var db = w.NewContext();
        var b = db.Bills.Include(x => x.Stocks).Single(x => x.Id == bill);
        Assert.Equal(10, b.Stocks.Sum(s => s.OriginalQuantity));
        Assert.Equal(15, db.Stocks.Sum(s => s.Quantity));
        w.Clean("increase separate batch");
    }

    [Fact]
    public void AdjustLossThenSaleThenDeleteLoss_LossReversalAlwaysPossible()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.Adjust(w.ClinicA, "Loss", 4, "L1").IsSuccess);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 6)).IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA));
        Assert.True(w.DeleteAdjust(w.AdjustIds().Single()).IsSuccess);
        Assert.Equal(4, w.Shelf(w.ClinicA)); Assert.Equal(4, w.BaQty(w.ClinicA));
        w.Clean("loss reversal after sale");
    }

    [Fact]
    public void AdjustLoss_LotAtOtherClinicUntouched()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        w.PostBill(w.ClinicB, 10, 10);
        Assert.True(w.Adjust(w.ClinicA, "Loss", 10, "L1").IsSuccess);
        Assert.Equal(0, w.Shelf(w.ClinicA)); Assert.Equal(10, w.Shelf(w.ClinicB));
        Assert.Equal(10, w.BaQty(w.ClinicB));
        w.Clean("loss clinic isolation");
    }
}
