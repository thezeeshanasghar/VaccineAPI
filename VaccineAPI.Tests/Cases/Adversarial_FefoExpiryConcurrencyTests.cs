using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

public class Adversarial_FefoTests
{
    static long[] StockIdsOfGives(StockWorld w) =>
        w.Ledger(InventoryTransactionType.Administer).Where(l => l.QuantityDelta == -1 && l.StockId != null).Select(l => (long)l.StockId!.Value).ToArray();

    [Fact]
    public void ThreeBatches_EarliestExpiryFirst_UndatedLast_OldestIdOnTies()
    {
        using var w = new StockWorld();
        Assert.True(w.Adjust(w.ClinicA, "Increase", 2, "UN", undated: true).IsSuccess);                       // id1 undated
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "D33", 2, 10, expiry: new DateTime(2033, 1, 1))).o.IsSuccess); // id2
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "D31", 2, 10, expiry: new DateTime(2031, 1, 1))).o.IsSuccess); // id3
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "D31B", 2, 10, expiry: new DateTime(2031, 1, 1))).o.IsSuccess); // id4 same expiry as id3
        var st = w.Stocks(); var id = (string lot) => st.Single(s => s.BatchLot == lot).Id;
        var kids = new List<(long c, long s)> { (w.ChildId, w.Schedule1Id) };
        for (int i = 0; i < 7; i++) kids.Add(w.AddChild());
        foreach (var k in kids) Assert.True(w.Give(k.s, k.c, (int)w.Dose1Id).IsSuccess);
        var order = StockIdsOfGives(w);
        Assert.Equal(new long[] { id("D31"), id("D31"), id("D31B"), id("D31B"), id("D33"), id("D33"), id("UN"), id("UN") }, order);
        Assert.Equal(0, w.BaQty(w.ClinicA));
        w.Clean("fefo order");
    }

    [Fact]
    public void FefoAfterReversal_RestoredEmptiedBatchIsPickedAgainBeforeLaterBatches()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "D31", 1, 10, expiry: new DateTime(2031, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "D33", 5, 10, expiry: new DateTime(2033, 1, 1))).o.IsSuccess);
        var (c2, s2) = w.AddChild();
        Assert.True(w.GiveDose1().IsSuccess);                                   // D31 emptied
        Assert.True(w.Stocks().Single(s => s.BatchLot == "D31").IsClosed);
        Assert.True(w.UngiveDose1().IsSuccess);                                 // restored
        Assert.False(w.Stocks().Single(s => s.BatchLot == "D31").IsClosed);
        Assert.True(w.Give(s2, c2, (int)w.Dose1Id).IsSuccess);                  // must take D31 again
        Assert.Equal(0, w.Stocks().Single(s => s.BatchLot == "D31").Quantity);
        Assert.Equal(5, w.Stocks().Single(s => s.BatchLot == "D33").Quantity);
        w.Clean("fefo after reversal");
    }

    [Fact]
    public void FefoAfterSaleReversalAndTransferReversal_EmptiedBatchesUsableAgain()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 3)).IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess); Assert.Equal(0, w.Shelf(w.ClinicA));   // sold out => pending
        Assert.True(w.UngiveDose1().IsSuccess);
        Assert.True(w.DeleteSale(w.SaleIds().Single()).IsSuccess);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 3)).IsSuccess);
        Assert.True(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(2, w.Shelf(w.ClinicA));
        Assert.Single(w.Ledger(InventoryTransactionType.Administer).Where(l => l.StockId != null).ToList());
        w.Clean("fefo after reversals");
    }

    [Fact]
    public void DepletedBatchIsSkipped_NextBatchUsed_NoNegative()
    {
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "E", 1, 10, expiry: new DateTime(2031, 1, 1))).o.IsSuccess);
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "F", 1, 10, expiry: new DateTime(2032, 1, 1))).o.IsSuccess);
        var kids = new List<(long c, long s)> { (w.ChildId, w.Schedule1Id), w.AddChild(), w.AddChild() };
        foreach (var k in kids) Assert.True(w.Give(k.s, k.c, (int)w.Dose1Id).IsSuccess);
        Assert.All(w.Stocks(), s => { Assert.Equal(0, s.Quantity); Assert.True(s.IsClosed); });
        Assert.Single(w.NewContext().UnbatchedUses.ToList());
        w.Clean("depleted");
    }
}

public class Adversarial_ExpiryTests
{
    static readonly DateTime Past = new DateTime(2020, 1, 1);

    static void WithFlag(bool value, Action body)
    {
        var old = InventoryTransactionService.ExcludeExpiredFromFefo;
        InventoryTransactionService.ExcludeExpiredFromFefo = value;
        try { body(); }
        finally { InventoryTransactionService.ExcludeExpiredFromFefo = old; }
    }

    [Fact]
    public void DefaultIsOn()
    {
        Assert.True(InventoryTransactionService.ExcludeExpiredFromFefo);
    }

    [Fact]
    public void FlagOff_ExpiredBatchIsStillUsedByFefo()
    {
        WithFlag(false, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: Past)).o.IsSuccess);
            Assert.True(w.GiveDose1().IsSuccess);
            Assert.Equal(1, w.Shelf(w.ClinicA));
            Assert.Empty(w.NewContext().UnbatchedUses.ToList());
            w.Clean("flag off");
        });
    }

    [Fact]
    public void FlagOn_ExpiredBatchSkipped_ValidBatchUsed()
    {
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: Past)).o.IsSuccess);
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "NEW", 2, 10, expiry: new DateTime(2035, 1, 1))).o.IsSuccess);
            Assert.True(w.GiveDose1().IsSuccess);
            Assert.Equal(2, w.Stocks().Single(s => s.BatchLot == "OLD").Quantity);
            Assert.Equal(1, w.Stocks().Single(s => s.BatchLot == "NEW").Quantity);
            Assert.Equal(3, w.BaQty(w.ClinicA));     // BrandAmount stays the physical total (design)
            w.Clean("flag on");
        });
    }

    [Fact]
    public void FlagOn_OnlyExpiredBatch_GiveBecomesPending_ExpiredUnitsUntouched()
    {
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: Past)).o.IsSuccess);
            Assert.True(w.GiveDose1().IsSuccess);           // client confirmed "record as unbatched"
            Assert.Equal(2, w.Shelf(w.ClinicA)); Assert.Equal(2, w.BaQty(w.ClinicA));
            Assert.Single(w.NewContext().UnbatchedUses.Where(u => u.Status == UnbatchedUseStatus.Pending).ToList());
            w.Clean("flag on, only expired");
        });
    }

    [Fact]
    public void FlagOn_OnlyExpiredBatch_GiveWithoutConfirmation_MustAskFirst_NotSilentlyBecomePending()
    {
        // The dry-run (HasFillableBatch) must agree with the real FEFO pick, otherwise the
        // "no stock batch found - record as unbatched?" prompt is skipped and the dose is
        // silently recorded as pending.
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: Past)).o.IsSuccess);
            var r = w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, confirmUnbatched: null);
            Assert.False(r.IsSuccess, "give accepted with no usable batch and no confirmation: " + r.Message);
        });
    }

    [Fact]
    public void FlagOn_BoundaryExpiryToday_UsableExpiryYesterday_Not()
    {
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "YEST", 1, 10, expiry: StockWorld.Today.AddDays(-1))).o.IsSuccess);
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "TODAY", 1, 10, expiry: StockWorld.Today)).o.IsSuccess);
            Assert.True(w.GiveDose1().IsSuccess);
            Assert.Equal(1, w.Stocks().Single(s => s.BatchLot == "YEST").Quantity);
            Assert.Equal(0, w.Stocks().Single(s => s.BatchLot == "TODAY").Quantity);
            w.Clean("expiry boundary");
        });
    }

    [Fact]
    public void FlagOn_BackdatedGive_UsesBatchThatWasValidOnThatDay()
    {
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "YEST", 1, 10, expiry: StockWorld.Today.AddDays(-1))).o.IsSuccess);
            var r = w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, date: StockWorld.Today.AddDays(-2), reRecord: false);
            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(0, w.Shelf(w.ClinicA));
            w.Clean("backdated fefo");
        });
    }

    [Fact]
    public void FlagOn_SaleTransferAdjustLoss_StillWorkOnExpiredBatch()
    {
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 6, 10, expiry: Past)).o.IsSuccess);
            Assert.True(w.Sell(w.ClinicA, SaleItemX(w.BrandId, "OLD", 1, Past)).IsSuccess);
            Assert.True(w.Adjust(w.ClinicA, "Loss", 2, "OLD").IsSuccess);          // write-off of expired stock must work
            Assert.True(w.Transfer(w.ClinicA, w.ClinicB, new StockTransferItemDTO { BrandId = w.BrandId, BatchLot = "OLD", ExpiryDate = Past, Quantity = 1, UnitPrice = 10 }).IsSuccess);
            Assert.Equal(2, w.Shelf(w.ClinicA)); Assert.Equal(1, w.Shelf(w.ClinicB));
            w.Clean("expired ops");
        });
    }

    [Fact]
    public void FlagOn_UngiveOfDoseFromNowExpiredBatch_StillRestoresIt()
    {
        WithFlag(false, () => { });
        using var w = new StockWorld();
        Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: Past)).o.IsSuccess);
        Assert.True(w.GiveDose1().IsSuccess);
        WithFlag(true, () =>
        {
            Assert.True(w.UngiveDose1().IsSuccess);
            Assert.Equal(2, w.Shelf(w.ClinicA));
            w.Clean("ungive restores expired batch");
        });
    }

    [Fact]
    public void FlagOn_BulkGive_OnlyExpired_AllPending_NothingNegative()
    {
        WithFlag(true, () =>
        {
            using var w = new StockWorld();
            Assert.True(w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "OLD", 2, 10, expiry: Past)).o.IsSuccess);
            var (c, s1) = w.AddChild(); var s2 = w.AddOneDoseVaccine(c, "BV").schedule;
            Assert.True(w.BulkGive(s1, w.BrandId, s1, s2).IsSuccess);
            Assert.Equal(2, w.Shelf(w.ClinicA));
            Assert.Equal(2, w.NewContext().UnbatchedUses.Count(u => u.Status == UnbatchedUseStatus.Pending));
            w.Clean("bulk expired");
        });
    }
}

// Injects a "competing writer" into the middle of a request: right before the first save that
// modifies a Stock row, on the SAME connection/transaction, bump every Stock.RowVersion (as a
// concurrent committed writer would). The request's UPDATE ... WHERE RowVersion = old then
// affects 0 rows => DbUpdateConcurrencyException.
sealed class CompetingWriter : SaveChangesInterceptor
{
    public bool Armed = true;
    public int Fired;
    void Fire(Microsoft.EntityFrameworkCore.DbContext? ctx)
    {
        if (!Armed || ctx == null) return;
        if (!ctx.ChangeTracker.Entries<Stock>().Any(e => e.State == EntityState.Modified)) return;
        Armed = false; Fired++;
        ctx.Database.ExecuteSqlRaw("UPDATE \"Stocks\" SET \"RowVersion\" = \"RowVersion\" + 1");
    }
    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> r) { Fire(e.Context); return r; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default) { Fire(e.Context); return new(r); }
}

public class Adversarial_ConcurrencyAndFailureTests
{
    static Context Racing(StockWorld w, CompetingWriter cw)
        => new Context(new DbContextOptionsBuilder<Context>(w.Db.Options).AddInterceptors(cw).Options);

    // ------------------------------------------------------------ RowVersion conflict handling
    [Fact]
    public void RowVersionConflict_OnGive_ReturnsRetryMessage_NoException_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 1, 10);
        var snap = w.Snapshot();
        var cw = new CompetingWriter();
        Response<ScheduleDTO> r;
        using (var db = Racing(w, cw)) r = w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, ctx: db);
        Assert.Equal(1, cw.Fired);
        Assert.False(r.IsSuccess);
        Assert.Contains("another action", r.Message ?? "");
        Assert.Equal(snap, w.Snapshot());
        w.Clean("conflict give");
    }

    [Fact]
    public void RowVersionConflict_OnUngive_ReturnsRetryMessage_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 2, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        var snap = w.Snapshot();
        var cw = new CompetingWriter();
        Response<ScheduleDTO> r;
        using (var db = Racing(w, cw))
            r = w.Schedules(db).Update(new ScheduleDTO { Id = w.Schedule1Id, ChildId = w.ChildId, DoseId = (int)w.Dose1Id, DoctorId = w.DoctorId, IsDone = false, IsSkip = false, Date = StockWorld.Today });
        Assert.Equal(1, cw.Fired);
        Assert.False(r.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("conflict ungive");
    }

    [Fact]
    public void RowVersionConflict_OnBulkGive_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var (c, s1) = w.AddChild(); var s2 = w.AddOneDoseVaccine(c, "BV").schedule;
        var snap = w.Snapshot();
        var cw = new CompetingWriter();
        Response<ScheduleDTO> r;
        using (var db = Racing(w, cw))
        {
            var dto = new ScheduleDTO { Id = s1, DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, Date = StockWorld.Today, ConfirmUnbatchedGive = true };
            foreach (var s in new[] { s1, s2 }) dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)s, BrandId = w.BrandId });
            r = w.Schedules(db).UpdateBulkInjection(dto);
        }
        Assert.False(r.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        w.Clean("conflict bulk");
    }

    [Fact]
    public void RowVersionConflict_OnSaleTransferAdjustBillEdit_ZeroChange_NoThrow()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();

        var cw = new CompetingWriter();
        using (var db = Racing(w, cw))
        {
            var dto = new DirectSaleCreateDTO { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ClientName = "C", SaleDate = StockWorld.Today };
            dto.Items.Add(SaleItem(w.BrandId, "L1", 2));
            Assert.False(Result.Of(w.Sales(db).Create(dto).GetAwaiter().GetResult()).IsSuccess);
        }
        Assert.Equal(1, cw.Fired); Assert.Equal(snap, w.Snapshot());

        cw = new CompetingWriter();
        using (var db = Racing(w, cw))
        {
            var dto = new StockTransferCreateDTO { DoctorId = w.DoctorId, FromClinicId = w.ClinicA, ToClinicId = w.ClinicB, TransferDate = StockWorld.Today, Reason = "t" };
            dto.Items.Add(XItem(w.BrandId, "L1", 2));
            Assert.False(Result.Of(w.Transfers(db).Create(dto).GetAwaiter().GetResult()).IsSuccess);
        }
        Assert.Equal(1, cw.Fired); Assert.Equal(snap, w.Snapshot());

        cw = new CompetingWriter();
        using (var db = Racing(w, cw))
            Assert.False(Result.Of(w.Adjusts(db).Create(new AdjustStockCreateDTO { DoctorId = w.DoctorId, ClinicId = w.ClinicA, BrandId = w.BrandId, Quantity = 2, Type = "Loss", BatchLot = "L1", Date = StockWorld.Today, Reason = "r" }).GetAwaiter().GetResult()).IsSuccess);
        Assert.Equal(1, cw.Fired); Assert.Equal(snap, w.Snapshot());

        cw = new CompetingWriter();
        using (var db = Racing(w, cw))
        {
            var dto = new BillUpdateDTO { BillNo = "E", BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SupplierName = "S" };
            dto.Lines.Add(Line(w.BrandId, "L1", 15, 10));
            Assert.False(Result.Of(w.Bills(db).Update(bill, dto).GetAwaiter().GetResult()).IsSuccess);
        }
        Assert.Equal(1, cw.Fired); Assert.Equal(snap, w.Snapshot());

        cw = new CompetingWriter();
        using (var db = Racing(w, cw))
            Assert.False(Result.Of(w.Bills(db).Reverse(bill, false).GetAwaiter().GetResult()).IsSuccess);
        Assert.Equal(1, cw.Fired); Assert.Equal(snap, w.Snapshot());
        w.Clean("conflicts everywhere");
    }

    // ------------------------------------------------------------ forced DB failure mid-request
    static void Trigger(StockWorld w, string when)
    {
        using var db = w.NewContext();
        db.Database.ExecuteSqlRaw($"CREATE TRIGGER boom BEFORE INSERT ON \"InventoryTransactions\" WHEN {when} BEGIN SELECT RAISE(ABORT,'boom'); END;");
    }
    static void Untrigger(StockWorld w) { using var db = w.NewContext(); db.Database.ExecuteSqlRaw("DROP TRIGGER IF EXISTS boom;"); }

    [Fact]
    public void ForcedFailure_MidSale_MidTransfer_MidAdjust_LeavesZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 20, 10);
        var snap = w.Snapshot();
        Trigger(w, "NEW.\"QuantityDelta\" = -7");
        Assert.False(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 7)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.False(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 7)).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Assert.False(w.Adjust(w.ClinicA, "Loss", 7, "L1").IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        w.Clean("forced failures");
        Assert.True(w.Sell(w.ClinicA, SaleItem(w.BrandId, "L1", 7)).IsSuccess);   // system healthy afterwards
        Assert.Equal(13, w.Shelf(w.ClinicA));
        w.Clean("healthy after");
    }

    [Fact]
    public void ForcedFailure_MidBillCreate_SecondLine_NoBillNoStockNoLedger()
    {
        using var w = new StockWorld();
        var snap = w.Snapshot();
        Trigger(w, "NEW.\"QuantityDelta\" = 7");
        var (o, id) = w.CreateBill(w.ClinicA, 0, Line(w.BrandId, "A1", 5, 10), Line(w.BrandId, "A2", 7, 10));
        Assert.False(o.IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        w.Clean("bill create failure");
    }

    [Fact]
    public void ForcedFailure_MidBillEditAndReverse_LeavesZeroChange()
    {
        using var w = new StockWorld();
        int bill = w.PostBill(w.ClinicA, 10, 10);
        var snap = w.Snapshot();
        Trigger(w, "NEW.\"QuantityDelta\" = 7");
        Assert.False(w.EditBill(bill, w.ClinicA, 0, Line(w.BrandId, "L1", 17, 12)).IsSuccess);   // +7, price change too
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        Trigger(w, "NEW.\"QuantityDelta\" = -10");
        Assert.False(w.ReverseBill(bill).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        w.Clean("edit/reverse failure");
        Assert.True(w.ReverseBill(bill).IsSuccess);
    }

    [Fact]
    public void ForcedFailure_MidGive_NoStockNoIsDone_ThenRetryWorks()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var snap = w.Snapshot();
        Trigger(w, "NEW.\"QuantityDelta\" = -1");
        for (int i = 0; i < 3; i++)
        {
            try { var r = w.GiveDose1(); Assert.False(r.IsSuccess); } catch (Exception) { /* infrastructure failure surfaces as exception */ }
            Assert.Equal(snap, w.Snapshot());
        }
        Untrigger(w);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.Equal(2, w.Shelf(w.ClinicA));
        Assert.Single(w.Ledger(InventoryTransactionType.Administer));
        w.Clean("retry after failure");
    }

    [Fact]
    public void ForcedFailure_MidUngiveAndMidTransferDelete_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 10, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        Assert.True(w.Transfer(w.ClinicA, w.ClinicB, XItem(w.BrandId, "L1", 4)).IsSuccess);
        var snap = w.Snapshot();
        Trigger(w, "NEW.\"QuantityDelta\" = 1");
        try { w.UngiveDose1(); } catch (Exception) { }
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        Trigger(w, "NEW.\"QuantityDelta\" = 4");
        Assert.False(w.DeleteTransfer(w.TransferIds().Single()).IsSuccess);
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        w.Clean("undo failures");
    }

    [Fact]
    public void ForcedFailure_MidBulkGive_ZeroChange()
    {
        using var w = new StockWorld();
        w.PostBill(w.ClinicA, 3, 10);
        var (c, s1) = w.AddChild(); var s2 = w.AddOneDoseVaccine(c, "BV").schedule;
        var snap = w.Snapshot();
        Trigger(w, "NEW.\"QuantityDelta\" = -1");
        try { w.BulkGive(s1, w.BrandId, s1, s2); } catch (Exception) { }
        Assert.Equal(snap, w.Snapshot());
        Untrigger(w);
        w.Clean("bulk failure");
    }

    // ------------------------------------------------------------ real concurrency, file DB
    [Fact]
    public void TwoContextsRacingForLastUnit_ExactlyOneConsumes_NeverNegative()
    {
        int consumedTotal = 0, threw = 0;
        for (int round = 0; round < 6; round++)
        {
            using var w = new StockWorld();
            w.PostBill(w.ClinicA, 1, 10);
            var (c2, s2) = w.AddChild();
            using var file = new FileDb(w);
            using (var db = file.NewContext()) db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            var results = new bool?[2];
            using var barrier = new Barrier(2);
            Task Run(int i, long sid, long cid) => Task.Run(() =>
            {
                try
                {
                    using var db = file.NewContext();
                    barrier.SignalAndWait();
                    var r = w.Schedules(db).Update(new ScheduleDTO { Id = sid, ChildId = cid, DoseId = (int)w.Dose1Id, DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, BrandId = w.BrandId, Date = StockWorld.Today, ConfirmUnbatchedGive = true, IsPAApprove = true });
                    results[i] = r.IsSuccess;
                }
                catch (Exception) { results[i] = null; Interlocked.Increment(ref threw); }
            });
            Task.WaitAll(Run(0, w.Schedule1Id, w.ChildId), Run(1, s2, c2));
            using var check = file.NewContext();
            var stock = check.Stocks.AsNoTracking().Single();
            int gives = check.InventoryTransactions.Count(t => t.SourceType == InventoryTransactionType.Administer && t.QuantityDelta == -1);
            consumedTotal += gives;
            Assert.True(stock.Quantity >= 0, "shelf went negative");
            Assert.True(gives <= 1, $"round {round}: {gives} units consumed from a 1-unit shelf");
            Assert.Equal(1 - gives, stock.Quantity);
            InventoryInvariants.AssertClean(check, "after race round " + round);
        }
        Console.WriteLine($"RACE give: consumed={consumedTotal}/6 rounds, loser threw={threw}");
    }

    [Fact]
    public void TwoContextsRacingSaleOfLastFive_AtMostOneSucceeds()
    {
        for (int round = 0; round < 6; round++)
        {
            using var w = new StockWorld();
            w.PostBill(w.ClinicA, 5, 10);
            using var file = new FileDb(w);
            using (var db = file.NewContext()) db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            int ok = 0;
            using var barrier = new Barrier(2);
            Task Run() => Task.Run(() =>
            {
                try
                {
                    using var db = file.NewContext();
                    barrier.SignalAndWait();
                    var dto = new DirectSaleCreateDTO { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ClientName = "C", SaleDate = StockWorld.Today };
                    dto.Items.Add(SaleItem(w.BrandId, "L1", 5));
                    if (Result.Of(w.Sales(db).Create(dto).GetAwaiter().GetResult()).IsSuccess) Interlocked.Increment(ref ok);
                }
                catch (Exception) { }
            });
            Task.WaitAll(Run(), Run());
            using var check = file.NewContext();
            Assert.True(ok <= 1, $"round {round}: {ok} sales of the same 5 units succeeded");
            Assert.Equal(5 - ok * 5, check.Stocks.AsNoTracking().Single().Quantity);
            InventoryInvariants.AssertClean(check, "after sale race " + round);
        }
    }

    [Fact]
    public void TwoContextsRacingTransferAndSaleOfSameBatch_NeverOversell()
    {
        for (int round = 0; round < 4; round++)
        {
            using var w = new StockWorld();
            w.PostBill(w.ClinicA, 5, 10);
            using var file = new FileDb(w);
            using (var db = file.NewContext()) db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            using var barrier = new Barrier(2);
            Task Sale() => Task.Run(() => { try { using var db = file.NewContext(); barrier.SignalAndWait();
                var dto = new DirectSaleCreateDTO { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ClientName = "C", SaleDate = StockWorld.Today }; dto.Items.Add(SaleItem(w.BrandId, "L1", 4));
                w.Sales(db).Create(dto).GetAwaiter().GetResult(); } catch (Exception) { } });
            Task Xfer() => Task.Run(() => { try { using var db = file.NewContext(); barrier.SignalAndWait();
                var dto = new StockTransferCreateDTO { DoctorId = w.DoctorId, FromClinicId = w.ClinicA, ToClinicId = w.ClinicB, TransferDate = StockWorld.Today, Reason = "t" }; dto.Items.Add(XItem(w.BrandId, "L1", 4));
                w.Transfers(db).Create(dto).GetAwaiter().GetResult(); } catch (Exception) { } });
            Task.WaitAll(Sale(), Xfer());
            using var check = file.NewContext();
            Assert.True(check.Stocks.AsNoTracking().Where(s => s.ClinicId == w.ClinicA).Sum(s => s.Quantity) >= 0);
            InventoryInvariants.AssertClean(check, "after sale/transfer race " + round);
            int out4 = check.DirectSales.Count() + check.StockTransfers.Count();
            Assert.True(out4 <= 1, $"round {round}: both a sale and a transfer of 4 from 5 units were recorded");
        }
    }

    [Fact]
    public void SixContextsGivingAgainstTwoUnits_NeverConsumeMoreThanTwo_NeverNegative()
    {
        for (int round = 0; round < 3; round++)
        {
            using var w = new StockWorld();
            w.PostBill(w.ClinicA, 2, 10);
            var kids = new List<(long c, long s)> { (w.ChildId, w.Schedule1Id) };
            for (int i = 0; i < 5; i++) kids.Add(w.AddChild());
            using var file = new FileDb(w);
            using (var db = file.NewContext()) db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            using var barrier = new Barrier(kids.Count);
            int threw = 0;
            var tasks = kids.Select(k => Task.Run(() =>
            {
                try
                {
                    using var db = file.NewContext();
                    barrier.SignalAndWait();
                    w.Schedules(db).Update(new ScheduleDTO { Id = k.s, ChildId = k.c, DoseId = (int)w.Dose1Id, DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, BrandId = w.BrandId, Date = StockWorld.Today, ConfirmUnbatchedGive = true, IsPAApprove = true });
                }
                catch (Exception) { Interlocked.Increment(ref threw); }
            })).ToArray();
            Task.WaitAll(tasks);
            using var check = file.NewContext();
            var stock = check.Stocks.AsNoTracking().Single();
            int gives = check.InventoryTransactions.Count(t => t.SourceType == InventoryTransactionType.Administer && t.QuantityDelta == -1);
            Console.WriteLine($"RACE6 round {round}: consumed={gives} shelf={stock.Quantity} threw={threw}");
            Assert.InRange(gives, 0, 2);
            Assert.Equal(2 - gives, stock.Quantity);
            InventoryInvariants.AssertClean(check, "after 6-way race " + round);
        }
    }
}
