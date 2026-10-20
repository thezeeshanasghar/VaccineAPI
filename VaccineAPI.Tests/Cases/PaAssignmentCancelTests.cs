using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Cancel only calls off the PA's task (PA_TRACKING_MERGE_SPEC.md section 2). Once a dose is given or
/// payment is recorded it must be refused for the doctor as well as the PA, so given doses and
/// collected cash are never left with no owner. Each assignment is judged on its own pins only.
/// </summary>
public class PaAssignmentCancelTests
{
    private sealed class Fx { public long ChildId, AsgA, AsgB, A1, B1; }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    // One child, two vaccine groups (two dates), one assignment per group, same PA.
    private static Fx Seed(StockWorld w, long pa)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "32" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Hania", ClinicId = w.ClinicA, UserId = parent.Id, DOB = new DateTime(2026, 7, 13) };
        db.Childs.Add(child); db.SaveChanges();
        var v = new Vaccine { Name = "V", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var d = new Dose { Name = "D", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 3650 };
        db.Doses.Add(d); db.SaveChanges();
        var a1 = new Schedule { ChildId = child.Id, DoseId = d.Id, Date = new DateTime(2026, 10, 19) };
        var b1 = new Schedule { ChildId = child.Id, DoseId = d.Id, Date = new DateTime(2026, 11, 16) };
        db.Schedules.AddRange(a1, b1); db.SaveChanges();
        var asgA = new PAAssignment { DoctorId = w.DoctorId, ChildId = child.Id, PersonalAssistantId = pa, AssignedAt = DateTime.UtcNow.AddMinutes(-10) };
        var asgB = new PAAssignment { DoctorId = w.DoctorId, ChildId = child.Id, PersonalAssistantId = pa, AssignedAt = DateTime.UtcNow };
        db.PAAssignments.AddRange(asgA, asgB); db.SaveChanges();
        db.PAAssignmentSchedules.AddRange(
            new PAAssignmentSchedule { AssignmentId = asgA.Id, ScheduleId = a1.Id },
            new PAAssignmentSchedule { AssignmentId = asgB.Id, ScheduleId = b1.Id });
        db.SaveChanges();
        return new Fx { ChildId = child.Id, AsgA = asgA.Id, AsgB = asgB.Id, A1 = a1.Id, B1 = b1.Id };
    }

    private static IActionResult Cancel(StockWorld w, long asgId, string callerType, long callerId)
    {
        using var db = w.NewContext();
        return Ctl(db).Cancel(asgId, new CancelAssignmentDto { CallerType = callerType, CallerId = callerId, Reason = "test" }).GetAwaiter().GetResult();
    }

    private static bool IsCancelled(StockWorld w, long asgId)
    {
        using var db = w.NewContext();
        return db.PAAssignments.Single(a => a.Id == asgId).IsCancelled;
    }

    [Fact]
    public void Doctor_can_cancel_when_nothing_is_given_or_paid()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        var r = Result.Of(Cancel(w, f.AsgA, "DOCTOR", w.DoctorId));
        Assert.True(r.IsSuccess, r.Message);
        Assert.True(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void Doctor_cannot_cancel_once_a_pinned_dose_is_given()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        using (var db = w.NewContext()) { db.Schedules.Single(s => s.Id == f.A1).IsDone = true; db.SaveChanges(); }

        var r = Result.Of(Cancel(w, f.AsgA, "DOCTOR", w.DoctorId));

        Assert.False(r.IsSuccess);
        Assert.Contains("Reassign", r.Message);
        Assert.False(IsCancelled(w, f.AsgA));
        using var chk = w.NewContext();
        Assert.True(chk.Schedules.Single(s => s.Id == f.A1).IsDone);               // nothing was touched
        Assert.Single(chk.PAAssignmentSchedules.Where(l => l.AssignmentId == f.AsgA));
    }

    [Fact]
    public void Doctor_cannot_cancel_once_payment_is_recorded()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        using (var db = w.NewContext()) { db.Schedules.Single(s => s.Id == f.A1).IsPaymentCollected = true; db.SaveChanges(); }

        var r = Result.Of(Cancel(w, f.AsgA, "DOCTOR", w.DoctorId));

        Assert.False(r.IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void PA_is_still_blocked_with_a_bad_request_once_a_dose_is_given()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        using (var db = w.NewContext()) { db.Schedules.Single(s => s.Id == f.A1).IsDone = true; db.SaveChanges(); }

        Assert.IsType<BadRequestObjectResult>(Cancel(w, f.AsgA, "PA", 17));
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void A_given_dose_in_one_group_does_not_block_cancelling_the_other_group()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        using (var db = w.NewContext()) { db.Schedules.Single(s => s.Id == f.A1).IsDone = true; db.SaveChanges(); }

        Assert.True(Result.Of(Cancel(w, f.AsgB, "DOCTOR", w.DoctorId)).IsSuccess);   // group B is untouched
        Assert.True(IsCancelled(w, f.AsgB));
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void A_doctor_cannot_cancel_another_doctors_assignment()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        var r = Result.Of(Cancel(w, f.AsgA, "DOCTOR", w.DoctorId + 999));
        Assert.False(r.IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void Cancelling_twice_is_refused_the_second_time()
    {
        using var w = new StockWorld();
        var f = Seed(w, pa: 17);
        Assert.True(Result.Of(Cancel(w, f.AsgA, "DOCTOR", w.DoctorId)).IsSuccess);
        var again = Result.Of(Cancel(w, f.AsgA, "DOCTOR", w.DoctorId));
        Assert.False(again.IsSuccess);
        Assert.Contains("already", again.Message);
    }
}
