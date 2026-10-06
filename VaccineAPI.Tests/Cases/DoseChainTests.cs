using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Step 6 (ungive cascade + block) and the add-dose MinGap floor for batches sent out of order.
/// Chain: 3 doses, MinGap 28. Registered plan for DOB 2026-06-01 is D1 07-13, D2 08-10, D3 09-07.
/// Giving D1 late on 08-03 pushed D2 to 08-31 and D3 to 09-28.
/// </summary>
public class DoseChainTests
{
    private static readonly DateTime Dob = new DateTime(2026, 6, 1);
    private static DateTime D(int m, int d) => new DateTime(2026, m, d);

    private sealed class Fx { public long ChildId; public long[] Dose = new long[3]; public long[] Sch = new long[3]; }

    private static Fx Seed(StockWorld w, bool d1Given, bool d2Given, DateTime d2Date, DateTime d3Date)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "31" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "ChainKid", ClinicId = w.ClinicA, UserId = parent.Id, DOB = Dob };
        db.Childs.Add(child); db.SaveChanges();
        var v = new Vaccine { Name = "Chain3", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var doses = new[]
        {
            new Dose { Name = "Chain Dose 1", VaccineId = v.Id, DoseOrder = 1, MinAge = 42, MaxAge = 3650 },
            new Dose { Name = "Chain Dose 2", VaccineId = v.Id, DoseOrder = 2, MinAge = 70, MinGap = 28, MaxAge = 3650 },
            new Dose { Name = "Chain Dose 3", VaccineId = v.Id, DoseOrder = 3, MinAge = 98, MinGap = 28, MaxAge = 3650 },
        };
        db.Doses.AddRange(doses); db.SaveChanges();
        var s = new[]
        {
            new Schedule { ChildId = child.Id, DoseId = doses[0].Id, Date = D(7, 13), IsDone = d1Given, GivenDate = d1Given ? D(8, 3) : (DateTime?)null },
            new Schedule { ChildId = child.Id, DoseId = doses[1].Id, Date = d2Date, IsDone = d2Given, GivenDate = d2Given ? D(9, 1) : (DateTime?)null },
            new Schedule { ChildId = child.Id, DoseId = doses[2].Id, Date = d3Date },
        };
        db.Schedules.AddRange(s); db.SaveChanges();
        return new Fx { ChildId = child.Id, Dose = doses.Select(x => x.Id).ToArray(), Sch = s.Select(x => x.Id).ToArray() };
    }

    private static Response<ScheduleDTO> Ungive(StockWorld w, Fx fx, int idx)
    {
        using var db = w.NewContext();
        return w.Schedules(db).Update(new ScheduleDTO
        {
            Id = fx.Sch[idx], ChildId = fx.ChildId, DoseId = (int)fx.Dose[idx], DoctorId = w.DoctorId,
            IsDone = false, IsSkip = false, Date = D(7, 13),
        });
    }

    private static Schedule Row(StockWorld w, long id)
    {
        using var db = w.NewContext();
        return db.Schedules.AsNoTracking().Single(x => x.Id == id);
    }

    [Fact]
    public void Ungive_TakesBackThePushItCaused()
    {
        using var w = new StockWorld();
        var fx = Seed(w, d1Given: true, d2Given: false, d2Date: D(8, 31), d3Date: D(9, 28));

        var r = Ungive(w, fx, 0);

        Assert.True(r.IsSuccess, r.Message);
        Assert.False(Row(w, fx.Sch[0]).IsDone);
        Assert.Equal(D(8, 10), Row(w, fx.Sch[1]).Date);
        Assert.Equal(D(9, 7), Row(w, fx.Sch[2]).Date);
    }

    [Fact]
    public void Ungive_LeavesAHandMovedDoseAlone()
    {
        using var w = new StockWorld();
        var fx = Seed(w, d1Given: true, d2Given: false, d2Date: D(9, 15), d3Date: D(9, 28));

        var r = Ungive(w, fx, 0);

        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(D(9, 15), Row(w, fx.Sch[1]).Date);
        Assert.Equal(D(9, 28), Row(w, fx.Sch[2]).Date);
    }

    [Fact]
    public void Ungive_IsBlockedWhileALaterDoseIsStillGiven()
    {
        using var w = new StockWorld();
        var fx = Seed(w, d1Given: true, d2Given: true, d2Date: D(8, 31), d3Date: D(9, 28));

        var r = Ungive(w, fx, 0);

        Assert.False(r.IsSuccess);
        Assert.Contains("Chain Dose 2", r.Message);
        Assert.True(Row(w, fx.Sch[0]).IsDone);
        Assert.Equal(D(9, 28), Row(w, fx.Sch[2]).Date);
    }

    [Fact]
    public void Ungive_LatestDoseFirstThenEarlier_BothWork()
    {
        using var w = new StockWorld();
        var fx = Seed(w, d1Given: true, d2Given: true, d2Date: D(8, 31), d3Date: D(9, 29));

        Assert.True(Ungive(w, fx, 1).IsSuccess);
        var r = Ungive(w, fx, 0);

        Assert.True(r.IsSuccess, r.Message);
        Assert.False(Row(w, fx.Sch[0]).IsDone);
        Assert.False(Row(w, fx.Sch[1]).IsDone);
    }

    [Fact]
    public void Post_BatchSentOutOfOrder_StillFloorsEachDoseAgainstTheEarlierOne()
    {
        using var w = new StockWorld();
        var fx = Seed(w, d1Given: true, d2Given: false, d2Date: D(8, 31), d3Date: D(9, 28));
        using (var db = w.NewContext())
        {   // dose 2 and 3 were never scheduled for this child
            db.Schedules.RemoveRange(db.Schedules.Where(x => x.Id == fx.Sch[1] || x.Id == fx.Sch[2]));
            db.SaveChanges();
        }

        using var db2 = w.NewContext();
        var sent = new[]
        {
            new ScheduleDTO { ChildId = fx.ChildId, DoseId = (int)fx.Dose[2], DoctorId = w.DoctorId },
            new ScheduleDTO { ChildId = fx.ChildId, DoseId = (int)fx.Dose[1], DoctorId = w.DoctorId },
        };
        var r = w.Schedules(db2).Post(sent);

        Assert.True(r.IsSuccess, r.Message);
        // dose 1 was given 08-03 -> dose 2 floor 08-31, dose 3 floor 09-28
        Assert.Equal(D(8, 31), sent[1].Date.Date);
        Assert.Equal(D(9, 28), sent[0].Date.Date);
    }
}
