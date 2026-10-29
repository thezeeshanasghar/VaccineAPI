using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// PAAssignmentController.Create must not hand a PA a dose that cannot legally be given on the visit's
/// target date (Faran Ahmed Butt, 2026-10-28: flu due 26 Oct 2027 assigned for 28 Oct 2026). The
/// test is "givable on the target date", never "due on or before today" — a dose due 10 Jan may be
/// assigned for 3 Jan. Every test checks the database afterwards, not just the HTTP result.
/// </summary>
public class DoseEligibilityTests
{
    private const long Pa = 17;
    private static readonly DateTime D = new DateTime(2026, 10, 26);

    private sealed class Fx { public long ChildId, DoctorUserId; public string Stamp = ""; public StockWorld W = null!; }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    private static Fx NewChild(StockWorld w, DateTime dob)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Faran", ClinicId = w.ClinicA, UserId = parent.Id, DOB = dob };
        db.Childs.Add(child); db.SaveChanges();
        var duid = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx { W = w, ChildId = child.Id, DoctorUserId = duid, Stamp = db.Users.Single(u => u.Id == duid).SecurityStamp };
    }

    // Two-dose chain: dose 1 given on `givenOn`, dose 2 (MinGap code) planned on `dose2Date`. Returns dose-2 schedule id.
    private static long Chain(Fx f, DateTime givenOn, int minGap, DateTime dose2Date, bool exact = false, bool dose1Given = true, bool disease = false, int minAge2 = 0)
    {
        using var db = f.W.NewContext();
        var v = new Vaccine { Name = "Flu-" + Guid.NewGuid().ToString("N")[..4], Validity = 12, ExactIntervalRequired = exact };
        db.Vaccines.Add(v); db.SaveChanges();
        var d1 = new Dose { Name = "Flu 1", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 36500 };
        var d2 = new Dose { Name = "Flu 2", VaccineId = v.Id, DoseOrder = 2, MinAge = minAge2, MinGap = minGap, MaxAge = 36500 };
        db.Doses.AddRange(d1, d2); db.SaveChanges();
        db.Schedules.Add(new Schedule { ChildId = f.ChildId, DoseId = d1.Id, Date = givenOn, IsDone = dose1Given, GivenDate = dose1Given ? givenOn : (DateTime?)null });
        var s2 = new Schedule { ChildId = f.ChildId, DoseId = d2.Id, Date = dose2Date, IsDisease = disease ? true : (bool?)null };
        db.Schedules.Add(s2); db.SaveChanges();
        return s2.Id;
    }

    private static JObject Create(Fx f, IEnumerable<long> ids, DateTime? target, IEnumerable<long>? group = null)
    {
        using var db = f.W.NewContext();
        var r = Ctl(db).Create(new CreateAssignmentDto
        {
            DoctorId = f.W.DoctorId, ClinicId = f.W.ClinicA, PersonalAssistantId = Pa, ChildId = f.ChildId,
            ScheduleIds = ids.ToList(), GroupScheduleIds = (group ?? ids).ToList(), TargetDate = target,
            CallerUserId = f.DoctorUserId, SecurityStamp = f.Stamp
        }).GetAwaiter().GetResult();
        return JObject.FromObject(((OkObjectResult)r).Value!);
    }

    private static int Pins(Fx f, long scheduleId) { using var db = f.W.NewContext(); return db.PAAssignmentSchedules.Count(l => l.ScheduleId == scheduleId); }
    private static int Assignments(Fx f) { using var db = f.W.NewContext(); return db.PAAssignments.Count(a => a.ChildId == f.ChildId); }

    // ── The reported bug ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Flu_due_a_year_ahead_cannot_be_assigned_for_today()
    {
        using var w = new StockWorld(); var f = NewChild(w, D.AddYears(-30));
        var flu2 = Chain(f, givenOn: D, minGap: 3001, dose2Date: D.AddYears(1));   // 3001 = 1 year
        var r = Create(f, new[] { flu2 }, target: D.AddDays(2));                   // 28 Oct 2026
        Assert.False((bool)r["IsSuccess"]!);
        Assert.True((bool?)r["DoseNotGivable"] ?? false);
        Assert.Contains("26-10-2027", (string)r["Message"]!);
        Assert.Equal(0, Assignments(f));
        Assert.Equal(0, Pins(f, flu2));
    }

    [Fact]
    public void Default_target_date_is_today_so_a_far_dose_is_refused_with_no_target_given()
    {
        using var w = new StockWorld(); var f = NewChild(w, StockWorld.Today.AddYears(-30));
        var flu2 = Chain(f, givenOn: StockWorld.Today.AddDays(-2), minGap: 3001, dose2Date: StockWorld.Today.AddYears(1).AddDays(-2));
        Assert.False((bool)Create(f, new[] { flu2 }, target: null)["IsSuccess"]!);
        Assert.Equal(0, Assignments(f));
    }

    // ── The legitimate case that a "due after today" rule would have broken ─────────────────

    [Theory]
    [InlineData(2027, 1, 3, true)]    // booked early: exactly the minimum-gap floor, due date is 10 Jan
    [InlineData(2027, 1, 10, true)]   // on the due date
    [InlineData(2027, 1, 2, true)]    // 1 day early: inside the CDC 4-day grace
    [InlineData(2026, 12, 30, true)]  // 4 days early: last grace day
    [InlineData(2026, 12, 29, false)] // 5 days early: refused
    [InlineData(2026, 11, 1, false)]  // way early
    public void A_dose_due_10_Jan_can_be_assigned_for_3_Jan_but_only_inside_its_legal_window(int y, int m, int d, bool allowed)
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        var s = Chain(f, givenOn: new DateTime(2026, 12, 6), minGap: 28, dose2Date: new DateTime(2027, 1, 10));  // floor = 3 Jan
        var r = Create(f, new[] { s }, target: new DateTime(y, m, d));
        Assert.Equal(allowed, (bool)r["IsSuccess"]!);
        Assert.Equal(allowed ? 1 : 0, Pins(f, s));
        Assert.Equal(allowed ? 1 : 0, Assignments(f));
    }

    [Fact]
    public void Exact_interval_vaccines_get_no_grace()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        var s = Chain(f, new DateTime(2026, 12, 6), 28, new DateTime(2027, 1, 10), exact: true);
        Assert.False((bool)Create(f, new[] { s }, new DateTime(2027, 1, 2))["IsSuccess"]!);
        Assert.True((bool)Create(f, new[] { s }, new DateTime(2027, 1, 3))["IsSuccess"]!);
    }

    [Fact]
    public void Min_age_not_reached_on_the_target_date_is_refused()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2026, 10, 1));
        var s = Chain(f, new DateTime(2026, 10, 1), 28, new DateTime(2026, 11, 1), minAge2: 42);   // 42 days old on 12 Nov
        var r = Create(f, new[] { s }, new DateTime(2026, 11, 5));
        Assert.False((bool)r["IsSuccess"]!);
        Assert.Contains("12-11-2026", (string)r["Message"]!);
        Assert.True((bool)Create(f, new[] { s }, new DateTime(2026, 11, 12))["IsSuccess"]!);
    }

    // ── Things the guard must NOT touch ─────────────────────────────────────────────────────

    [Fact]
    public void Unrecorded_predecessor_is_not_blocked_at_assign_time()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        var s = Chain(f, new DateTime(2026, 12, 6), 28, new DateTime(2027, 1, 10), dose1Given: false);
        Assert.True((bool)Create(f, new[] { s }, new DateTime(2027, 1, 10))["IsSuccess"]!);   // give-time still enforces it
    }

    [Fact]
    public void Disease_entries_are_never_gated()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        var s = Chain(f, new DateTime(2026, 12, 6), 3001, new DateTime(2027, 12, 6), disease: true);
        Assert.True((bool)Create(f, new[] { s }, new DateTime(2026, 12, 7))["IsSuccess"]!);
    }

    [Fact]
    public void Assignment_without_doses_is_untouched()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        Assert.True((bool)Create(f, new long[0], StockWorld.Today)["IsSuccess"]!);
    }

    // ── Mixed groups ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Group_with_one_givable_and_one_blocked_dose_pins_only_the_givable_one_and_says_so()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        var ok = Chain(f, new DateTime(2026, 12, 6), 28, new DateTime(2027, 1, 10));
        var bad = Chain(f, new DateTime(2026, 12, 6), 3001, new DateTime(2027, 12, 6));
        var r = Create(f, new[] { ok, bad }, new DateTime(2027, 1, 3));
        Assert.True((bool)r["IsSuccess"]!);
        Assert.Equal(1, Pins(f, ok));
        Assert.Equal(0, Pins(f, bad));
        Assert.Single((JArray)r["ResponseData"]!["SkippedDoses"]!);
    }

    [Fact]
    public void Blocked_undone_dose_does_not_stop_assigning_a_group_that_already_has_a_given_dose_to_collect()
    {
        using var w = new StockWorld(); var f = NewChild(w, new DateTime(2020, 1, 1));
        var bad = Chain(f, new DateTime(2026, 12, 6), 3001, new DateTime(2027, 12, 6));
        long givenId;
        using (var db = w.NewContext())
        {
            var v = new Vaccine { Name = "G", Validity = 12 }; db.Vaccines.Add(v); db.SaveChanges();
            var dg = new Dose { Name = "G1", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 36500 }; db.Doses.Add(dg); db.SaveChanges();
            var g = new Schedule { ChildId = f.ChildId, DoseId = dg.Id, Date = new DateTime(2026, 12, 7), IsDone = true, GivenDate = new DateTime(2026, 12, 7), GivenByPaId = Pa, Amount = 1000 };
            db.Schedules.Add(g); db.SaveChanges(); givenId = g.Id;
        }
        var r = Create(f, new[] { bad }, new DateTime(2026, 12, 7), group: new[] { bad, givenId });
        Assert.True((bool)r["IsSuccess"]!);
        Assert.Equal(0, Pins(f, bad));
    }
}
