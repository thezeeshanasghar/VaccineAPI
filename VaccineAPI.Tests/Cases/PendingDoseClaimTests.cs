using Microsoft.EntityFrameworkCore;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Case 3 in full: doses given before the purchase exists, then allocated to the real batch.</summary>
public class PendingDoseClaimTests
{
    // three real children, each with a dose-1 schedule, all given with NO stock on the shelf
    private static List<(long child, long schedule)> GiveAtZero(StockWorld w, int n)
    {
        var list = new List<(long, long)>();
        for (int i = 0; i < n; i++)
        {
            var (c, s) = w.AddChild();
            var r = w.Give(s, c, (int)w.Dose1Id);
            Assert.True(r.IsSuccess, r.Message);
            list.Add((c, s));
        }
        return list;
    }

    private static List<long> PendingIds(StockWorld w)
    {
        using var db = w.NewContext();
        return db.UnbatchedUses.Where(u => u.Status == UnbatchedUseStatus.Pending).OrderBy(u => u.Id).Select(u => u.Id).ToList();
    }

    private static (string stamp, long userId) DoctorSession(StockWorld w)
    {
        using var db = w.NewContext();
        var d = db.Doctors.Include(x => x.User).First(x => x.Id == w.DoctorId);
        return (d.User.SecurityStamp, d.UserId);
    }

    private static (Result.Outcome o, int billId) BillWithClaim(StockWorld w, int qty, List<long>? claim)
    {
        using var db = w.NewContext();
        var dto = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S",
            BillNo = "B-" + Guid.NewGuid().ToString("N")[..6], ClaimUnbatchedUseIds = claim };
        dto.Lines.Add(Kit.Line(w.BrandId, "L1", qty, 10));
        var r = w.Bills(db).Create(dto).GetAwaiter().GetResult();
        var o = Result.Of(r);
        return (o, o.IsSuccess ? (int)Result.Prop(r, "ResponseData", "Id")! : 0);
    }

    [Fact]
    public void Case3_Full_ThreeDosesBeforePurchase_PurchaseOf10WithClaim_LeavesSeven()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 3);
        w.Clean("after 3 gives at zero");
        Assert.Equal(3, PendingIds(w).Count);
        Assert.Equal(0, w.BaQty(w.ClinicA));

        var (o, _) = BillWithClaim(w, 10, PendingIds(w));
        Assert.True(o.IsSuccess, o.Message);

        Assert.Equal(7, w.Shelf(w.ClinicA));              // physical shelf
        Assert.Equal(7, w.BaQty(w.ClinicA));              // counter follows
        using var db = w.NewContext();
        Assert.Equal(0, db.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Pending));
        Assert.Equal(3, db.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Claimed));
        var batch = db.Stocks.Single();
        Assert.Equal(7, batch.Quantity);
        Assert.Equal(10, batch.OriginalQuantity);          // purchased 10, 3 consumed
        foreach (var s in db.Schedules.Where(s => s.IsDone))
        {
            Assert.Equal(batch.Id, s.StockId);
            Assert.Equal("L1", s.Lot);
            Assert.Equal(batch.StockAmount, s.VaccineCost);
        }
        Assert.Equal(3, db.InventoryTransactions.Count(t => t.SourceType == InventoryTransactionType.Administer && t.StockId == batch.Id && t.QuantityDelta == -1));
        w.Clean("after the claim");
    }

    [Fact]
    public void PurchaseWithoutOptIn_LeavesDosesPending_StockFull()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 3);
        var (o, _) = BillWithClaim(w, 10, null);
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(10, w.Shelf(w.ClinicA));
        Assert.Equal(3, PendingIds(w).Count);
        w.Clean("purchase without claim");
    }

    [Fact]
    public void PartialClaim_BatchSmallerThanPending_ClaimsOldestFirst_RestStayPending()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 5);
        var all = PendingIds(w);
        var (o, _) = BillWithClaim(w, 3, all);
        Assert.True(o.IsSuccess, o.Message);
        Assert.Equal(0, w.Shelf(w.ClinicA));
        var pending = PendingIds(w);
        Assert.Equal(2, pending.Count);
        Assert.Equal(all.Skip(3).ToList(), pending);   // oldest three were claimed
        w.Clean("after partial claim");
    }

    [Fact]
    public void DoctorClaimEndpoint_AllocatesToExistingBatch()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 2);
        var (o, _) = BillWithClaim(w, 5, null);
        Assert.True(o.IsSuccess);
        int stockId; using (var db = w.NewContext()) stockId = db.Stocks.Single().Id;
        var (stamp, userId) = DoctorSession(w);

        using var db2 = w.NewContext();
        var ctl = new UnbatchedUseController(db2, new InventoryTransactionService(db2));
        var res = ctl.Claim(new UnbatchedUseController.ClaimDTO { DoctorId = w.DoctorId, StockId = stockId, UseIds = PendingIds(w),
            CallerUserId = userId, SecurityStamp = stamp }).GetAwaiter().GetResult();
        Assert.True(Result.Of(res).IsSuccess, Result.Of(res).Message);
        Assert.Equal(3, w.Shelf(w.ClinicA));
        w.Clean("after the claim endpoint");
    }

    [Fact]
    public void ClaimEndpoint_RejectsWrongSessionAndChangesNothing()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 2);
        BillWithClaim(w, 5, null);
        int stockId; using (var db = w.NewContext()) stockId = db.Stocks.Single().Id;
        var before = w.Snapshot();

        using var db2 = w.NewContext();
        var ctl = new UnbatchedUseController(db2, new InventoryTransactionService(db2));
        var bad = ctl.Claim(new UnbatchedUseController.ClaimDTO { DoctorId = w.DoctorId, StockId = stockId, UseIds = PendingIds(w),
            CallerUserId = 1, SecurityStamp = "wrong" }).GetAwaiter().GetResult();
        Assert.False(Result.Of(bad).IsSuccess);
        Assert.Equal(before, w.Snapshot());
    }

    [Fact]
    public void ClaimingTheSameDoseTwice_SecondAttemptRejected_NoDoubleDeduction()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 2);
        var ids = PendingIds(w);
        BillWithClaim(w, 5, ids);
        Assert.Equal(3, w.Shelf(w.ClinicA));
        int stockId; using (var db = w.NewContext()) stockId = db.Stocks.Single().Id;
        var (stamp, userId) = DoctorSession(w);
        using var db2 = w.NewContext();
        var res = new UnbatchedUseController(db2, new InventoryTransactionService(db2))
            .Claim(new UnbatchedUseController.ClaimDTO { DoctorId = w.DoctorId, StockId = stockId, UseIds = ids, CallerUserId = userId, SecurityStamp = stamp })
            .GetAwaiter().GetResult();
        Assert.False(Result.Of(res).IsSuccess);
        Assert.Equal(3, w.Shelf(w.ClinicA));
        w.Clean("after a double claim attempt");
    }

    [Fact]
    public void UngiveOfClaimedDose_ReturnsUnitToTheSameBatch_AndVoidsTheUse()
    {
        using var w = new StockWorld();
        var kids = GiveAtZero(w, 2);
        BillWithClaim(w, 5, PendingIds(w));
        Assert.Equal(3, w.Shelf(w.ClinicA));

        var r = w.Ungive(kids[0].schedule, kids[0].child, (int)w.Dose1Id);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(4, w.Shelf(w.ClinicA));
        using var db = w.NewContext();
        Assert.Equal(1, db.UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Voided));
        w.Clean("after ungiving a claimed dose");
    }

    [Fact]
    public void UngiveOfPendingDose_VoidsIt_NoStockEffect()
    {
        using var w = new StockWorld();
        var kids = GiveAtZero(w, 1);
        var r = w.Ungive(kids[0].schedule, kids[0].child, (int)w.Dose1Id);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Empty(PendingIds(w));
        BillWithClaim(w, 4, null);
        Assert.Equal(4, w.Shelf(w.ClinicA));
        w.Clean("after voiding a pending dose");
    }

    [Fact]
    public void Dismiss_RequiresDoctorAndReason_AndLeavesStockUntouched()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 1);
        var id = PendingIds(w).Single();
        var (stamp, userId) = DoctorSession(w);

        using (var db = w.NewContext())
        {
            var ctl = new UnbatchedUseController(db, new InventoryTransactionService(db));
            var noReason = ctl.Dismiss(id, new UnbatchedUseController.DismissDTO { DoctorId = w.DoctorId, Reason = " ", CallerUserId = userId, SecurityStamp = stamp }).GetAwaiter().GetResult();
            Assert.False(Result.Of(noReason).IsSuccess);
            var wrong = ctl.Dismiss(id, new UnbatchedUseController.DismissDTO { DoctorId = w.DoctorId, Reason = "x", CallerUserId = userId, SecurityStamp = "bad" }).GetAwaiter().GetResult();
            Assert.False(Result.Of(wrong).IsSuccess);
        }
        using (var db = w.NewContext())
        {
            var ok = new UnbatchedUseController(db, new InventoryTransactionService(db))
                .Dismiss(id, new UnbatchedUseController.DismissDTO { DoctorId = w.DoctorId, Reason = "vial supplied by parent", CallerUserId = userId, SecurityStamp = stamp })
                .GetAwaiter().GetResult();
            Assert.True(Result.Of(ok).IsSuccess, Result.Of(ok).Message);
        }
        Assert.Empty(PendingIds(w));
        BillWithClaim(w, 6, null);
        Assert.Equal(6, w.Shelf(w.ClinicA));
        w.Clean("after dismissing");
    }

    [Fact]
    public void AdjustIncrease_WithPendingDoses_AsksFirst_ThenYesReallyDeducts()
    {
        using var w = new StockWorld();
        GiveAtZero(w, 2);

        using (var d0 = w.NewContext())
        {
            var asked = Result.Of(w.Adjusts(d0).Create(new AdjustStockCreateDTO
            {
                DoctorId = w.DoctorId, ClinicId = w.ClinicA, BrandId = w.BrandId, Quantity = 5, Type = "Increase", Price = 10,
                BatchLot = "AL1", Date = StockWorld.Today, ExpiryDate = StockWorld.Expiry, ClearUnbatchedBacklog = null
            }).GetAwaiter().GetResult());
            Assert.False(asked.IsSuccess);
            Assert.Contains("recorded as given with no batch", asked.Message);
        }
        Assert.Equal(0, w.Shelf(w.ClinicA));

        using var db = w.NewContext();
        var res = w.Adjusts(db).Create(new AdjustStockCreateDTO
        {
            DoctorId = w.DoctorId, ClinicId = w.ClinicA, BrandId = w.BrandId, Quantity = 5, Type = "Increase", Price = 10,
            BatchLot = "AL1", Date = StockWorld.Today, ExpiryDate = StockWorld.Expiry, ClearUnbatchedBacklog = true
        }).GetAwaiter().GetResult();
        Assert.True(Result.Of(res).IsSuccess, Result.Of(res).Message);
        Assert.Equal(3, w.Shelf(w.ClinicA));               // 5 added, 2 already used
        Assert.Empty(PendingIds(w));
        w.Clean("after the adjust-increase claim");
    }

    [Fact]
    public void TransferIn_OptInClaim_AllocatesDestinationPendingDoses()
    {
        using var w = new StockWorld();
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "TL", 10, 10));
        // pending dose at clinic B: a PA/doctor whose online clinic is B gives with nothing on the shelf
        using (var db = w.NewContext()) { var a = db.Clinics.Find(w.ClinicA)!; a.IsOnline = false; db.Clinics.Find(w.ClinicB)!.IsOnline = true; db.SaveChanges(); }
        var (c, s) = w.AddChild();
        Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess);
        var pending = PendingIds(w);
        Assert.Single(pending);

        using var db2 = w.NewContext();
        var r = w.Transfers(db2).Create(new StockTransferCreateDTO
        {
            DoctorId = w.DoctorId, FromClinicId = w.ClinicA, ToClinicId = w.ClinicB, TransferDate = StockWorld.Today, Reason = "t",
            Items = { Kit.XItem(w.BrandId, "TL", 4) }, ClaimUnbatchedUseIds = pending
        }).GetAwaiter().GetResult();
        Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        Assert.Equal(6, w.Shelf(w.ClinicA));
        Assert.Equal(3, w.Shelf(w.ClinicB));               // 4 received, 1 already used
        Assert.Empty(PendingIds(w));
        w.Clean("after transfer-in claim");
    }
}

public class PendingDoseConcurrencyTests
{
    [Fact]
    public void TwoDoctorsClaimingTheSameDoseAtOnce_OnlyOneWins_NoDoubleDeduction()
    {
        using var w = new StockWorld();
        var (c, s) = w.AddChild();
        Assert.True(w.Give(s, c, (int)w.Dose1Id).IsSuccess);
        w.CreateBill(w.ClinicA, 0, Kit.Line(w.BrandId, "L1", 5, 10));
        long useId; int stockId;
        using (var db = w.NewContext()) { useId = db.UnbatchedUses.Single().Id; stockId = db.Stocks.Single().Id; }

        // Two requests both read the dose as Pending, then both try to claim it.
        using var dbA = w.NewContext(); using var dbB = w.NewContext();
        var svcA = new InventoryTransactionService(dbA); var svcB = new InventoryTransactionService(dbB);
        Assert.True(svcA.ClaimUnbatched(w.DoctorId, stockId, new[] { useId }).IsSuccess);
        Assert.True(svcB.ClaimUnbatched(w.DoctorId, stockId, new[] { useId }).IsSuccess);   // B validated before A saved
        svcA.SaveAndAssert();
        Assert.ThrowsAny<Exception>(() => svcB.SaveAndAssert());   // B's write is rejected by the row-version checks

        Assert.Equal(4, w.Shelf(w.ClinicA));      // 5 bought, exactly ONE dose deducted
        w.Clean("after the racing claims");
    }
}
