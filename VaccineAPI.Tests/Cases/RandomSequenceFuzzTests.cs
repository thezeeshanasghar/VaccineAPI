using Microsoft.EntityFrameworkCore;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace VaccineAPI.Tests.Cases;

public class RandomSequenceFuzzTests
{
    private readonly ITestOutputHelper O;
    public RandomSequenceFuzzTests(ITestOutputHelper o) { O = o; }

    static List<string> Semantic(Context db)
    {
        var v = new List<string>();
        var ledger = db.InventoryTransactions.AsNoTracking().ToList();
        var reversed = ledger.Where(l => l.ReversesTransactionId != null).Select(l => l.ReversesTransactionId!.Value).ToHashSet();
        var scheds = db.Schedules.AsNoTracking().ToList();
        foreach (var s in scheds)
        {
            var open = ledger.Where(l => l.SourceType == InventoryTransactionType.Administer && l.SourceId == s.Id && l.ConsumesStock && l.QuantityDelta != 0 && !reversed.Contains(l.Id)).ToList();
            if (open.Count > 1) v.Add($"S2 schedule {s.Id} has {open.Count} unreversed consuming gives");
            if (!s.IsDone && open.Count > 0) v.Add($"S2b schedule {s.Id} NOT given but has {open.Count} unreversed consuming give(s) (leaked unit)");
            if (s.IsDone && s.BrandId != null && open.Count == 0)
            {
                var use = db.UnbatchedUses.AsNoTracking().Any(u => u.ScheduleId == s.Id && u.ActiveScheduleKey != null);
                var zero = ledger.Any(l => l.SourceType == InventoryTransactionType.Administer && l.SourceId == s.Id && !reversed.Contains(l.Id));
                if (!use && !zero) v.Add($"S1 schedule {s.Id} given with brand {s.BrandId} but has NO stock effect, no pending use, no audit row");
            }
        }
        foreach (var st in db.Stocks.AsNoTracking())
        {
            if (st.OriginalQuantity < 0) v.Add($"S4 batch {st.Id} OriginalQuantity negative {st.OriginalQuantity}");
            if (st.OriginalQuantity < st.Quantity) v.Add($"S3 batch {st.Id} Quantity {st.Quantity} > OriginalQuantity {st.OriginalQuantity}");
        }
        foreach (var u in db.UnbatchedUses.AsNoTracking().Where(u => u.Status == UnbatchedUseStatus.Pending))
        {
            var s = scheds.FirstOrDefault(x => x.Id == u.ScheduleId);
            if (s == null || !s.IsDone) v.Add($"S5 pending use {u.Id} for schedule {u.ScheduleId} that is not given");
        }
        return v;
    }

    public static IEnumerable<object[]> Seeds() { for (int i = 100; i < 260; i++) yield return new object[] { i }; }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz(int seed)
    {
        var rnd = new Random(seed);
        using var w = new StockWorld();
        var b2 = w.AddBrand("B2");
        var kids = new List<(long child, long sched)> { (w.ChildId, w.Schedule1Id) };
        for (int i = 0; i < 3; i++) kids.Add(w.AddChild());
        var log = new List<string>();
        var bills = new List<int>();
        var hard = new List<string>();      // INV1-4 (documented invariants)
        var soft = new List<string>();      // semantic
        string[] lots = { "L1", "L2", "L3" };
        for (int step = 0; step < 45; step++)
        {
            int op = rnd.Next(0, 23);
            long clinic = rnd.Next(2) == 0 ? w.ClinicA : w.ClinicB;
            long brand = rnd.Next(4) == 0 ? b2 : w.BrandId;
            string lot = lots[rnd.Next(lots.Length)];
            string desc = "";
            try
            {
                switch (op)
                {
                    case 0: case 1:
                    {
                        var qty = rnd.Next(1, 8);
                        var (o, id) = w.CreateBill(clinic, 0, Kit.Line(brand, lot, qty, 10, expiry: StockWorld.Today.AddDays(rnd.Next(-5, 400))));
                        desc = $"bill create c{clinic} b{brand} {lot} q{qty} -> {o.IsSuccess}"; if (o.IsSuccess) bills.Add(id); break;
                    }
                    case 2:
                    {
                        if (bills.Count == 0) break;
                        var id = bills[rnd.Next(bills.Count)];
                        using var db = w.NewContext(); var b = db.Bills.Include(x => x.Stocks).FirstOrDefault(x => x.Id == id); if (b == null) break;
                        var lines = b.Stocks.Select(s => Kit.Line(s.BrandId, s.BatchLot!, Math.Max(1, s.OriginalQuantity + rnd.Next(-3, 4)), 10, s.Id, s.Expiry)).ToArray();
                        if (lines.Length == 0) break;
                        var o = w.EditBill(id, b.ClinicId, 0, lines); desc = $"bill edit {id} -> {o.IsSuccess} {o.Message}"; break;
                    }
                    case 3:
                    {
                        if (bills.Count == 0) break;
                        var id = bills[rnd.Next(bills.Count)]; var o = w.ReverseBill(id); desc = $"bill reverse {id} -> {o.IsSuccess}"; if (o.IsSuccess) bills.Remove(id); break;
                    }
                    case 4: case 5:
                    {
                        var o = w.Sell(clinic, Kit.SaleItemX(brand, lot, rnd.Next(1, 4), null)); desc = $"sale c{clinic} b{brand} {lot} -> {o.IsSuccess}"; break;
                    }
                    case 6:
                    {
                        var ids = w.SaleIds(); if (ids.Count == 0) break; var id = ids[rnd.Next(ids.Count)]; var o = w.DeleteSale(id); desc = $"sale delete {id} -> {o.IsSuccess}"; break;
                    }
                    case 7:
                    {
                        var o = w.Transfer(clinic, clinic == w.ClinicA ? w.ClinicB : w.ClinicA, new StockTransferItemDTO { BrandId = brand, BatchLot = lot, Quantity = rnd.Next(1, 4), UnitPrice = 10 });
                        desc = $"transfer from {clinic} b{brand} {lot} -> {o.IsSuccess}"; break;
                    }
                    case 8:
                    {
                        var ids = w.TransferIds(); if (ids.Count == 0) break; var id = ids[rnd.Next(ids.Count)]; var o = w.DeleteTransfer(id); desc = $"transfer delete {id} -> {o.IsSuccess}"; break;
                    }
                    case 9:
                    {
                        var o = w.Adjust(clinic, rnd.Next(2) == 0 ? "Increase" : "Loss", rnd.Next(1, 4), lot, 10, brand, undated: rnd.Next(2) == 0); desc = $"adjust c{clinic} b{brand} {lot} -> {o.IsSuccess}"; break;
                    }
                    case 10:
                    {
                        var ids = w.AdjustIds(); if (ids.Count == 0) break; var id = ids[rnd.Next(ids.Count)]; var o = w.DeleteAdjust(id); desc = $"adjust delete {id} -> {o.IsSuccess}"; break;
                    }
                    case 11: case 12: case 13: case 14:
                    {
                        var k = kids[rnd.Next(kids.Count)]; var r = w.Give(k.sched, k.child, (int)w.Dose1Id, brand); desc = $"give s{k.sched} b{brand} -> {r.IsSuccess} {r.Message}"; break;
                    }
                    case 15: case 16: case 17:
                    {
                        var k = kids[rnd.Next(kids.Count)]; var r = w.Ungive(k.sched, k.child, (int)w.Dose1Id); desc = $"ungive s{k.sched} -> {r.IsSuccess} {r.Message}"; break;
                    }
                    case 18:
                    {
                        var r = w.BulkGive(w.Schedule1Id, brand, w.Schedule1Id, w.Schedule2Id); desc = $"bulk give b{brand} -> {r.IsSuccess} {r.Message}"; break;
                    }
                    case 19:
                    {
                        var r = w.BulkUngive(w.Schedule1Id, brand, rnd.Next(2) == 0, w.Schedule1Id, w.Schedule2Id); desc = $"bulk ungive -> {r.IsSuccess} {r.Message}"; break;
                    }
                    case 20:
                    {
                        using var db = w.NewContext();
                        var use = db.UnbatchedUses.Where(u => u.Status == UnbatchedUseStatus.Pending).OrderBy(u => u.Id).FirstOrDefault(); if (use == null) break;
                        var st = db.Stocks.Where(s => s.BrandId == use.BrandId && s.Quantity > 0 && (s.ClinicId == use.ClinicId)).FirstOrDefault(); if (st == null) break;
                        var res = new InventoryTransactionService(db).ClaimUnbatched(w.DoctorId, st.Id, new List<long> { use.Id });
                        if (res.IsSuccess) { try { db.SaveChanges(); } catch (Exception e) { desc += " SAVEFAIL " + e.Message; } }
                        desc = $"claim use {use.Id} -> {res.IsSuccess} {res.Message}" + desc; break;
                    }
                    case 22:
                    {
                        using var db = w.NewContext();
                        var st = db.Stocks.Where(x => x.BillId != null && x.OriginalQuantity > x.Quantity).OrderBy(x => x.Id).FirstOrDefault(); if (st == null) break;
                        var r = w.Bills(db).SplitConsumed(st.BillId!.Value, st.Id).GetAwaiter().GetResult(); desc = $"split bill {st.BillId} stock {st.Id} -> {Result.Of(r).IsSuccess}"; break;
                    }
                    case 21:
                    {
                        using var db = w.NewContext();
                        var use = db.UnbatchedUses.Where(u => u.Status == UnbatchedUseStatus.Pending).OrderBy(u => u.Id).FirstOrDefault(); if (use == null) break;
                        var res = new InventoryTransactionService(db).DismissUnbatched(w.DoctorId, use.Id, "test");
                        if (res.IsSuccess) db.SaveChanges();
                        desc = $"dismiss {use.Id} -> {res.IsSuccess}"; break;
                    }
                }
            }
            catch (Exception ex) { desc += " EXC " + ex.GetType().Name + ": " + ex.Message; }
            log.Add($"{step}: {desc}");
            using var chk = w.NewContext();
            foreach (var x in InventoryInvariants.Check(chk)) hard.Add($"step {step} [{desc}] {x}");
            foreach (var x in Semantic(chk)) soft.Add($"step {step} [{desc}] {x}");
            if (hard.Count > 0) break;
        }
        foreach (var l in log) O.WriteLine(l);
        foreach (var h in hard.Take(5)) O.WriteLine("HARD: " + h);
        foreach (var h in soft.GroupBy(x => x.Substring(x.IndexOf(']') + 2, 2)).Select(g => g.First())) O.WriteLine("SOFT seed " + seed + ": " + h);
        Assert.True(hard.Count == 0, string.Join("\n", hard.Take(3)));
        Assert.True(soft.Count == 0, "SOFT first: " + soft.FirstOrDefault());
    }
}
