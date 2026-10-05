using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// BulkReschedule with a routine dose and an infinite dose (Flu, Max Age 90d) due the same day.
/// Reproduces the 2026-08-13 case: target is valid for Hep B Dose 2 but past Flu's Max Age.
/// </summary>
public class BulkRescheduleTests
{
    private static readonly DateTime Dob = new DateTime(2024, 1, 1);
    private static readonly DateTime Due = new DateTime(2024, 3, 1);

    private sealed class Fixture
    {
        public long ChildId, HepDose2Schedule, FluSchedule;
    }

    private static Fixture Seed(StockWorld w, bool fluFirst)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "31" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "BulkKid", ClinicId = w.ClinicA, UserId = parent.Id, DOB = Dob };
        db.Childs.Add(child); db.SaveChanges();

        var hep = new Vaccine { Name = "HepB", Validity = 12 };
        var flu = new Vaccine { Name = "Flu", Validity = 12 };
        db.Vaccines.AddRange(hep, flu); db.SaveChanges();
        var h1 = new Dose { Name = "Hep B Dose 1", VaccineId = hep.Id, DoseOrder = 1, MinAge = 0, MaxAge = 3650 };
        var h2 = new Dose { Name = "Hep B Dose 2", VaccineId = hep.Id, DoseOrder = 2, MinAge = 0, MinGap = 28, MaxAge = 3650 };
        var f = new Dose { Name = "Flu", VaccineId = flu.Id, DoseOrder = 1, MinAge = 0, MaxAge = 90 };
        db.Doses.AddRange(h1, h2, f); db.SaveChanges();

        var s1 = new Schedule { ChildId = child.Id, DoseId = h1.Id, Date = new DateTime(2024, 2, 1), IsDone = true, GivenDate = new DateTime(2024, 2, 1) };
        db.Schedules.Add(s1); db.SaveChanges();
        var sh2 = new Schedule { ChildId = child.Id, DoseId = h2.Id, Date = Due };
        var sf = new Schedule { ChildId = child.Id, DoseId = f.Id, Date = Due };
        if (fluFirst) { db.Schedules.Add(sf); db.SaveChanges(); db.Schedules.Add(sh2); }
        else { db.Schedules.Add(sh2); db.SaveChanges(); db.Schedules.Add(sf); }
        db.SaveChanges();
        return new Fixture { ChildId = child.Id, HepDose2Schedule = sh2.Id, FluSchedule = sf.Id };
    }

    private static Response<ScheduleDTO> Bulk(StockWorld w, Fixture fx, DateTime target)
    {
        using var db = w.NewContext();
        return w.Schedules(db).BulkReschedule(new ScheduleDTO { Id = fx.HepDose2Schedule, ChildId = fx.ChildId, Date = target });
    }

    private static DateTime DateOf(StockWorld w, long scheduleId)
    {
        using var db = w.NewContext();
        return db.Schedules.AsNoTracking().Single(s => s.Id == scheduleId).Date;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfiniteDosePastMaxAge_IsSkippedAndNamed_RoutineDoseStillMoves(bool fluFirst)
    {
        using var w = new StockWorld();
        var fx = Seed(w, fluFirst);
        var target = new DateTime(2024, 4, 15);   // Flu Max Age floor is 2024-03-31

        var r = Bulk(w, fx, target);

        Assert.True(r.IsSuccess, r.Message);
        Assert.Contains("1 of 2 moved", r.Message);
        Assert.Contains("Flu", r.Message);
        Assert.Equal(target, DateOf(w, fx.HepDose2Schedule));
        Assert.Equal(Due, DateOf(w, fx.FluSchedule));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetValidForBoth_MovesBoth(bool fluFirst)
    {
        using var w = new StockWorld();
        var fx = Seed(w, fluFirst);
        var target = new DateTime(2024, 3, 20);

        var r = Bulk(w, fx, target);

        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal("schedule updated successfully.", r.Message);
        Assert.Equal(target, DateOf(w, fx.HepDose2Schedule));
        Assert.Equal(target, DateOf(w, fx.FluSchedule));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingValid_FailsWithRuleCode_AndWritesNothing(bool fluFirst)
    {
        using var w = new StockWorld();
        var fx = Seed(w, fluFirst);

        var r = Bulk(w, fx, new DateTime(2023, 12, 1));   // before DOB: every row blocked

        Assert.False(r.IsSuccess);
        Assert.Equal(Due, DateOf(w, fx.HepDose2Schedule));
        Assert.Equal(Due, DateOf(w, fx.FluSchedule));
    }

    [Fact]
    public void UnknownSchedule_ReturnsNotFound_InsteadOfThrowing()
    {
        using var w = new StockWorld();
        var fx = Seed(w, false);
        using var db = w.NewContext();

        var r = w.Schedules(db).BulkReschedule(new ScheduleDTO { Id = 999999, ChildId = fx.ChildId, Date = Due });

        Assert.False(r.IsSuccess);
        Assert.Equal("Schedule not found", r.Message);
    }
}
