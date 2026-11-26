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
/// Also covers the PA cancel redesign (PA_CANCEL_REDESIGN_SPEC.md §6): required reason, login-stamp
/// check, pa-move, the doctor's pa-cancelled pickup list, follow-up-closed and CancelledBy.
/// </summary>
public class PaAssignmentCancelTests
{
    private sealed class Fx
    {
        public long ChildId, AsgA, AsgB, A1, B1;
        public long PaId, PaUserId, OtherPaId, OtherPaUserId, DoctorUserId;
        public string PaStamp = "", OtherPaStamp = "", DoctorStamp = "";
    }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    private static (long PaId, long UserId, string Stamp) AddPa(Context db, long doctorId, string name)
    {
        var u = new User { MobileNumber = "34" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = name, DoctorId = doctorId, UserId = u.Id, IsVerified = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        return (pa.Id, u.Id, u.SecurityStamp);
    }

    // One child, two vaccine groups (two dates), one assignment per group, same real PA (with a login stamp).
    private static Fx Seed(StockWorld w)
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

        var pa = AddPa(db, w.DoctorId, "Atif");
        var other = AddPa(db, w.DoctorId, "Sapna");

        var asgA = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = pa.PaId, AssignedAt = DateTime.UtcNow.AddMinutes(-10) };
        var asgB = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = pa.PaId, AssignedAt = DateTime.UtcNow };
        db.PAAssignments.AddRange(asgA, asgB); db.SaveChanges();
        db.PAAssignmentSchedules.AddRange(
            new PAAssignmentSchedule { AssignmentId = asgA.Id, ScheduleId = a1.Id },
            new PAAssignmentSchedule { AssignmentId = asgB.Id, ScheduleId = b1.Id });
        db.SaveChanges();

        var doctorUserId = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx
        {
            ChildId = child.Id, AsgA = asgA.Id, AsgB = asgB.Id, A1 = a1.Id, B1 = b1.Id,
            PaId = pa.PaId, PaUserId = pa.UserId, PaStamp = pa.Stamp,
            OtherPaId = other.PaId, OtherPaUserId = other.UserId, OtherPaStamp = other.Stamp,
            DoctorUserId = doctorUserId, DoctorStamp = db.Users.Single(u => u.Id == doctorUserId).SecurityStamp
        };
    }

    private static IActionResult DoctorCancel(StockWorld w, long asgId, long doctorId)
    {
        using var db = w.NewContext();
        return Ctl(db).Cancel(asgId, new CancelAssignmentDto { CallerType = "DOCTOR", CallerId = doctorId, Reason = "test" }).GetAwaiter().GetResult();
    }

    private static IActionResult PaCancel(StockWorld w, Fx f, long asgId, string? code = "CANT_REACH", string? note = null,
                                          long? userId = null, string? stamp = null, long? paId = null)
    {
        using var db = w.NewContext();
        return Ctl(db).Cancel(asgId, new CancelAssignmentDto
        {
            CallerType = "PA", CallerId = paId ?? f.PaId, ReasonCode = code, Reason = note,
            CallerUserId = userId ?? f.PaUserId, SecurityStamp = stamp ?? f.PaStamp
        }).GetAwaiter().GetResult();
    }

    private static IActionResult PaMove(StockWorld w, Fx f, long asgId, DateTime date, long? userId = null, string? stamp = null)
    {
        using var db = w.NewContext();
        return Ctl(db).PaMove(asgId, new PaMoveDto { NewDate = date, CallerUserId = userId ?? f.PaUserId, SecurityStamp = stamp ?? f.PaStamp })
            .GetAwaiter().GetResult();
    }

    private static IActionResult CloseFollowUp(StockWorld w, Fx f, long asgId, long? userId = null, string? stamp = null)
    {
        using var db = w.NewContext();
        return Ctl(db).FollowUpClosed(asgId, new PAAssignmentController.VisitActionDto
        {
            DoctorId = w.DoctorId, CallerUserId = userId ?? f.DoctorUserId, SecurityStamp = stamp ?? f.DoctorStamp
        }).GetAwaiter().GetResult();
    }

    private static List<long> Pickups(StockWorld w, Fx f)
    {
        using var db = w.NewContext();
        var r = Ctl(db).GetPaCancelledPickups(w.DoctorId, f.DoctorUserId, f.DoctorStamp).GetAwaiter().GetResult();
        Assert.True(Result.Of(r).IsSuccess);
        var data = (System.Collections.IEnumerable)Result.Prop(r, "ResponseData")!;
        return data.Cast<object>().Select(x => (long)x.GetType().GetProperty("AssignmentId")!.GetValue(x)!).ToList();
    }

    private static PAAssignment Asg(StockWorld w, long asgId)
    {
        using var db = w.NewContext();
        return db.PAAssignments.Single(a => a.Id == asgId);
    }

    private static bool IsCancelled(StockWorld w, long asgId) => Asg(w, asgId).IsCancelled;

    private static void GiveA1(StockWorld w, Fx f)
    {
        using var db = w.NewContext();
        db.Schedules.Single(s => s.Id == f.A1).IsDone = true; db.SaveChanges();
    }

    // ---------------- Doctor cancel (unchanged rules)

    [Fact]
    public void Doctor_can_cancel_when_nothing_is_given_or_paid()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var r = Result.Of(DoctorCancel(w, f.AsgA, w.DoctorId));
        Assert.True(r.IsSuccess, r.Message);
        var a = Asg(w, f.AsgA);
        Assert.True(a.IsCancelled);
        Assert.Equal("DOCTOR", a.CancelledBy);
    }

    [Fact]
    public void Doctor_cannot_cancel_once_a_pinned_dose_is_given()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveA1(w, f);

        var r = Result.Of(DoctorCancel(w, f.AsgA, w.DoctorId));

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
        var f = Seed(w);
        using (var db = w.NewContext()) { db.Schedules.Single(s => s.Id == f.A1).IsPaymentCollected = true; db.SaveChanges(); }

        var r = Result.Of(DoctorCancel(w, f.AsgA, w.DoctorId));

        Assert.False(r.IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void A_given_dose_in_one_group_does_not_block_cancelling_the_other_group()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveA1(w, f);

        Assert.True(Result.Of(DoctorCancel(w, f.AsgB, w.DoctorId)).IsSuccess);   // group B is untouched
        Assert.True(IsCancelled(w, f.AsgB));
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void A_doctor_cannot_cancel_another_doctors_assignment()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var r = Result.Of(DoctorCancel(w, f.AsgA, w.DoctorId + 999));
        Assert.False(r.IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void Cancelling_twice_is_refused_the_second_time()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(DoctorCancel(w, f.AsgA, w.DoctorId)).IsSuccess);
        var again = Result.Of(DoctorCancel(w, f.AsgA, w.DoctorId));
        Assert.False(again.IsSuccess);
        Assert.Contains("already", again.Message);
    }

    // ---------------- PA cancel (spec §6 items 1-3)

    [Fact]
    public void PA_is_still_blocked_with_a_bad_request_once_a_dose_is_given()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveA1(w, f);

        Assert.IsType<BadRequestObjectResult>(PaCancel(w, f, f.AsgA));
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void PA_cancel_without_a_reason_code_is_refused()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var r = Result.Of(PaCancel(w, f, f.AsgA, code: null));
        Assert.False(r.IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));

        // "Family wants another day" is pa-move, not a cancel code.
        Assert.False(Result.Of(PaCancel(w, f, f.AsgA, code: "ANOTHER_DAY")).IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void PA_cancel_with_Other_and_no_note_is_refused()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.False(Result.Of(PaCancel(w, f, f.AsgA, code: "OTHER", note: "   ")).IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void PA_cancel_stores_the_label_and_note_marks_it_PA_and_bells_the_doctor_with_the_reason()
    {
        using var w = new StockWorld();
        var f = Seed(w);

        var r = Result.Of(PaCancel(w, f, f.AsgA, code: "OTHER", note: "  Father travelling  "));
        Assert.True(r.IsSuccess, r.Message);

        var a = Asg(w, f.AsgA);
        Assert.True(a.IsCancelled);
        Assert.Equal("PA", a.CancelledBy);
        Assert.Equal("Other: Father travelling", a.CancelReason);
        Assert.Null(a.FollowUpClosedAt);

        using var db = w.NewContext();
        var bell = db.Notifications.Single(n => n.Type == "PaCancelled" && n.RecipientType == "DOCTOR" && n.RecipientId == w.DoctorId);
        Assert.Contains("Other: Father travelling", bell.Message);
    }

    [Fact]
    public void PA_cancel_with_a_code_and_no_note_stores_just_the_label()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(PaCancel(w, f, f.AsgA, code: "NOT_AVAILABLE")).IsSuccess);
        Assert.Equal("I'm not available", Asg(w, f.AsgA).CancelReason);
    }

    [Fact]
    public void PA_cancel_with_a_wrong_or_missing_stamp_is_refused()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.False(Result.Of(PaCancel(w, f, f.AsgA, stamp: "not-the-stamp")).IsSuccess);
        Assert.False(Result.Of(PaCancel(w, f, f.AsgA, stamp: "")).IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    [Fact]
    public void Another_PA_cannot_cancel_even_with_their_own_valid_stamp()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        // Body claims the owning PA id, but the login is Sapna's.
        Assert.False(Result.Of(PaCancel(w, f, f.AsgA, userId: f.OtherPaUserId, stamp: f.OtherPaStamp)).IsSuccess);
        // Body claims Sapna's own id: refused by the ownership check.
        Assert.False(Result.Of(PaCancel(w, f, f.AsgA, userId: f.OtherPaUserId, stamp: f.OtherPaStamp, paId: f.OtherPaId)).IsSuccess);
        Assert.False(IsCancelled(w, f.AsgA));
    }

    // ---------------- pa-move (spec §6 item 4)

    [Fact]
    public void PA_move_changes_only_the_visit_date_clears_the_time_and_tells_the_doctor_and_parent()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var start = Pkt.Today().Date.AddDays(1);
        using (var db = w.NewContext())
        {
            var a = db.PAAssignments.Single(x => x.Id == f.AsgA);
            a.TargetDate = start; a.IsScheduledVisit = true; a.WindowStart = new TimeSpan(10, 0, 0);
            db.SaveChanges();
        }
        var to = Pkt.Today().Date.AddDays(4);

        var r = Result.Of(PaMove(w, f, f.AsgA, to));
        Assert.True(r.IsSuccess, r.Message);

        using var chk = w.NewContext();
        var moved = chk.PAAssignments.Single(x => x.Id == f.AsgA);
        Assert.Equal(to, moved.TargetDate);
        Assert.Null(moved.WindowStart);
        Assert.False(moved.IsCancelled);
        Assert.Equal(new DateTime(2026, 10, 19), chk.Schedules.Single(s => s.Id == f.A1).Date);   // dose due date untouched
        Assert.Contains(AssignmentAudit.Read(chk, f.ChildId, new[] { f.AsgA }), e => e.Action == "VisitMovedByPa");
        Assert.Single(chk.Notifications.Where(n => n.Type == "PaMoved" && n.RecipientType == "DOCTOR" && n.RecipientId == w.DoctorId));
        Assert.Single(chk.Notifications.Where(n => n.Type == "VisitChanged" && n.RecipientType == "PARENT"));
    }

    [Fact]
    public void PA_move_is_refused_for_a_past_date_or_the_same_date()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var day = Pkt.Today().Date.AddDays(2);
        using (var db = w.NewContext()) { db.PAAssignments.Single(x => x.Id == f.AsgA).TargetDate = day; db.SaveChanges(); }

        Assert.False(Result.Of(PaMove(w, f, f.AsgA, Pkt.Today().Date.AddDays(-1))).IsSuccess);
        Assert.False(Result.Of(PaMove(w, f, f.AsgA, day)).IsSuccess);
        Assert.Equal(day, Asg(w, f.AsgA).TargetDate);
    }

    [Fact]
    public void PA_move_is_refused_for_another_PA_a_given_dose_or_a_closed_assignment()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var to = Pkt.Today().Date.AddDays(3);

        Assert.False(Result.Of(PaMove(w, f, f.AsgA, to, userId: f.OtherPaUserId, stamp: f.OtherPaStamp)).IsSuccess);
        Assert.False(Result.Of(PaMove(w, f, f.AsgA, to, stamp: "bad")).IsSuccess);

        GiveA1(w, f);
        Assert.False(Result.Of(PaMove(w, f, f.AsgA, to)).IsSuccess);

        Assert.True(Result.Of(DoctorCancel(w, f.AsgB, w.DoctorId)).IsSuccess);
        Assert.False(Result.Of(PaMove(w, f, f.AsgB, to)).IsSuccess);

        Assert.Null(Asg(w, f.AsgA).TargetDate);
        Assert.Null(Asg(w, f.AsgB).TargetDate);
    }

    // ---------------- Needs-you pickups (spec §6 item 5)

    [Fact]
    public void A_PA_cancel_shows_in_pickups_and_a_doctor_cancel_does_not()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(PaCancel(w, f, f.AsgA)).IsSuccess);
        Assert.True(Result.Of(DoctorCancel(w, f.AsgB, w.DoctorId)).IsSuccess);

        Assert.Equal(new List<long> { f.AsgA }, Pickups(w, f));
    }

    [Fact]
    public void Pickup_clears_when_a_new_open_assignment_pins_the_dose()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(PaCancel(w, f, f.AsgA)).IsSuccess);
        using (var db = w.NewContext())
        {
            var again = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = f.ChildId, PersonalAssistantId = f.OtherPaId, AssignedAt = DateTime.UtcNow };
            db.PAAssignments.Add(again); db.SaveChanges();
            db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = again.Id, ScheduleId = f.A1 });
            db.SaveChanges();
        }
        Assert.Empty(Pickups(w, f));
    }

    [Fact]
    public void Pickup_clears_when_the_doses_are_given()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(PaCancel(w, f, f.AsgA)).IsSuccess);
        GiveA1(w, f);
        Assert.Empty(Pickups(w, f));
    }

    [Fact]
    public void Reassign_and_take_back_set_CancelledBy_and_never_show_as_pickups()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using (var db = w.NewContext())
        {
            var r = Ctl(db).Reassign(f.AsgA, new ReassignDto { NewPaId = f.OtherPaId, Reason = "PA unavailable", CallerUserId = f.DoctorUserId, SecurityStamp = f.DoctorStamp })
                .GetAwaiter().GetResult();
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }
        using (var db = w.NewContext()) { var b = db.Schedules.Single(s => s.Id == f.B1); b.IsDone = true; b.GivenByPaId = f.PaId; db.SaveChanges(); }   // Take it back needs a given dose
        using (var db = w.NewContext())
        {
            var r = Ctl(db).TakeBack(f.AsgB, new TakeBackDto { Reason = "Atif is unwell", CallerUserId = f.DoctorUserId, SecurityStamp = f.DoctorStamp })
                .GetAwaiter().GetResult();
            Assert.True(Result.Of(r).IsSuccess, Result.Of(r).Message);
        }

        Assert.Equal("REASSIGN", Asg(w, f.AsgA).CancelledBy);
        Assert.Equal("TAKEBACK", Asg(w, f.AsgB).CancelledBy);
        Assert.Empty(Pickups(w, f));
    }

    // ---------------- follow-up-closed (spec §6 item 6)

    [Fact]
    public void No_visit_needed_removes_the_pickup_and_is_idempotent()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(PaCancel(w, f, f.AsgA)).IsSuccess);

        Assert.True(Result.Of(CloseFollowUp(w, f, f.AsgA)).IsSuccess);
        var first = Asg(w, f.AsgA).FollowUpClosedAt;
        Assert.NotNull(first);
        Assert.Empty(Pickups(w, f));

        Assert.True(Result.Of(CloseFollowUp(w, f, f.AsgA)).IsSuccess);
        Assert.Equal(first, Asg(w, f.AsgA).FollowUpClosedAt);   // second tap changes nothing
    }

    [Fact]
    public void No_visit_needed_is_doctor_only_and_only_for_PA_cancels()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(PaCancel(w, f, f.AsgA)).IsSuccess);

        Assert.False(Result.Of(CloseFollowUp(w, f, f.AsgA, userId: f.PaUserId, stamp: f.PaStamp)).IsSuccess);   // a PA login
        Assert.False(Result.Of(CloseFollowUp(w, f, f.AsgA, stamp: "bad")).IsSuccess);
        Assert.Null(Asg(w, f.AsgA).FollowUpClosedAt);

        Assert.False(Result.Of(CloseFollowUp(w, f, f.AsgB)).IsSuccess);   // open assignment, not PA-cancelled
        Assert.True(Result.Of(DoctorCancel(w, f.AsgB, w.DoctorId)).IsSuccess);
        Assert.False(Result.Of(CloseFollowUp(w, f, f.AsgB)).IsSuccess);   // doctor's own cancel
    }

    [Fact]
    public void Pickups_list_is_refused_without_a_valid_doctor_login()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using var db = w.NewContext();
        Assert.False(Result.Of(Ctl(db).GetPaCancelledPickups(w.DoctorId, f.PaUserId, f.PaStamp).GetAwaiter().GetResult()).IsSuccess);
        Assert.False(Result.Of(Ctl(db).GetPaCancelledPickups(w.DoctorId, f.DoctorUserId, "bad").GetAwaiter().GetResult()).IsSuccess);
    }
}
