using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// PA_MULTI_ASSIGNMENT_SPEC.md §11. A DOCTOR ungive that leaves a PA visit with no given dose ends the
/// visit exactly like an approved "refused at home" request (Faran Ahmed Butt, 26 Oct 2026): assignment
/// removed, Approved refusal recorded, "Reversed" audit row, PA notified. A visit that still has a given
/// dose, a PA ungive and a cash-confirmed assignment are left alone. A recorded payment no longer blocks
/// the PA's refusal REQUEST; the doctor's approval reverses it. Every test checks the database afterwards.
/// </summary>
public class VisitReversalTests
{
    private sealed class Fx { public long AsgId, PaId, DoctorUserId; public string Stamp = ""; }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    private static Fx Seed(StockWorld w, bool pinBoth = true)
    {
        using var db = w.NewContext();
        var paUser = new User { MobileNumber = "35" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(paUser); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Atif Zubair", DoctorId = w.DoctorId, UserId = paUser.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var asg = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = w.ChildId, PersonalAssistantId = pa.Id, AssignedAt = DateTime.UtcNow.AddHours(-2) };
        db.PAAssignments.Add(asg); db.SaveChanges();
        db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = w.Schedule1Id });
        if (pinBoth) db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = w.Schedule2Id });
        db.SaveChanges();
        var duid = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx { AsgId = asg.Id, PaId = pa.Id, DoctorUserId = duid, Stamp = db.Users.Single(u => u.Id == duid).SecurityStamp };
    }

    private static void GiveVisit(StockWorld w)
    {
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id).IsSuccess);
    }

    private static void RecordPayment(StockWorld w, Fx f, long scheduleId)
    {
        using var db = w.NewContext();
        foreach (var s in db.Schedules.Where(x => x.Id == scheduleId)) s.Amount = 1500;
        db.SaveChanges();
        using var db2 = w.NewContext();
        var r = w.Schedules(db2).RecordPaymentMode(scheduleId, new ScheduleDTO { PaymentMode = "Cash", DoctorId = w.DoctorId, CallerUserId = f.DoctorUserId, SecurityStamp = f.Stamp });
        Assert.True(Result.Of(r).IsSuccess);
    }

    private static void AssertClosedAsRefusal(StockWorld w, Fx f)
    {
        using var db = w.NewContext();
        Assert.False(db.PAAssignments.Any(a => a.Id == f.AsgId));
        Assert.False(db.PAAssignmentSchedules.Any(l => l.AssignmentId == f.AsgId));
        var refusal = Assert.Single(db.VaccineRefusals.Where(r => r.AssignmentId == f.AsgId).ToList());
        Assert.Equal("Approved", refusal.Status);
        Assert.Equal(f.PaId, refusal.PaId);
        Assert.NotNull(refusal.ResolvedAt);
        Assert.Single(db.PaActivityLogs.Where(l => l.ActionCode == "AsgReversed" && l.PatientId == w.ChildId).ToList());
        Assert.Single(db.Notifications.Where(n => n.Type == "PaRefusalApproved" && n.RecipientId == f.PaId).ToList());
    }

    [Fact]
    public void Doctor_bulk_ungive_of_the_whole_visit_closes_it_like_an_approved_refusal()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveVisit(w);
        var u = w.BulkUngive(w.Schedule1Id, w.BrandId, true, w.Schedule1Id, w.Schedule2Id); Assert.True(u.IsSuccess, u.Message);
        AssertClosedAsRefusal(w, f);
    }

    [Fact]
    public void Doctor_single_ungive_of_the_only_given_dose_closes_the_visit()
    {
        using var w = new StockWorld();
        var f = Seed(w, pinBoth: false);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        var u = w.UngiveDose1(); Assert.True(u.IsSuccess, u.Message);
        AssertClosedAsRefusal(w, f);
        using var db = w.NewContext();   // the single-dose ungive is logged too, before the "Reversed" row
        var codes = db.PaActivityLogs.Where(l => l.PatientId == w.ChildId && l.ActionCode.StartsWith("Asg")).OrderBy(l => l.Id).Select(l => l.ActionCode).ToList();
        Assert.Equal(new[] { "AsgGiven", "AsgUngiven", "AsgReversed" }, codes);
    }

    [Fact]
    public void Faran_case_given_and_invoiced_then_ungiven_by_doctor_leaves_no_liability_and_no_open_visit()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveVisit(w);
        using (var db = w.NewContext())
        {
            var inv = new InvoiceSubmission { ChildId = w.ChildId, DoctorId = w.DoctorId, PaId = f.PaId, TotalAmount = 1500, InvoiceStatus = "Pending", ClinicId = w.ClinicA };
            db.InvoiceSubmissions.Add(inv); db.SaveChanges();
            foreach (var s in db.Schedules.Where(x => x.Id == w.Schedule1Id || x.Id == w.Schedule2Id)) s.InvoiceSubmissionId = inv.Id;
            db.SaveChanges();
        }
        Assert.True(w.BulkUngive(w.Schedule1Id, w.BrandId, true, w.Schedule1Id, w.Schedule2Id).IsSuccess);
        AssertClosedAsRefusal(w, f);
        using var chk = w.NewContext();
        Assert.False(chk.InvoiceSubmissions.Any(i => i.ChildId == w.ChildId));
        Assert.All(chk.Schedules.Where(x => x.Id == w.Schedule1Id || x.Id == w.Schedule2Id).ToList(),
            s => { Assert.False(s.IsDone); Assert.False(s.IsPaymentCollected); Assert.Null(s.Amount); });
    }

    [Fact]
    public void Payment_recorded_then_doctor_ungives_the_visit_closes_it_and_clears_the_payment()
    {
        using var w = new StockWorld();
        var f = Seed(w, pinBoth: false);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        RecordPayment(w, f, w.Schedule1Id);
        Assert.True(w.UngiveDose1().IsSuccess);
        AssertClosedAsRefusal(w, f);
        using var db = w.NewContext();
        var s = db.Schedules.Single(x => x.Id == w.Schedule1Id);
        Assert.False(s.IsPaymentCollected); Assert.Null(s.Amount); Assert.Null(s.PaymentCollectorPaId);
    }

    [Fact]
    public void Ungiving_one_of_two_given_doses_leaves_the_visit_open()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveVisit(w);
        var u = w.Ungive(w.Schedule2Id, w.ChildId, (int)w.Dose2Id); Assert.True(u.IsSuccess, u.Message);   // later dose first: chain rule
        using var db = w.NewContext();
        Assert.True(db.PAAssignments.Any(a => a.Id == f.AsgId));
        Assert.Equal(2, db.PAAssignmentSchedules.Count(l => l.AssignmentId == f.AsgId));
        Assert.False(db.VaccineRefusals.Any(r => r.AssignmentId == f.AsgId));
    }

    [Fact]
    public void A_cash_confirmed_assignment_is_never_closed_by_an_ungive()
    {
        using var w = new StockWorld();
        var f = Seed(w, pinBoth: false);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        using (var db = w.NewContext()) { db.PAAssignments.Single(a => a.Id == f.AsgId).IsCashConfirmedByDoctor = true; db.SaveChanges(); }
        w.UngiveDose1();
        using var chk = w.NewContext();
        Assert.True(chk.PAAssignments.Any(a => a.Id == f.AsgId));
        Assert.False(chk.VaccineRefusals.Any(r => r.AssignmentId == f.AsgId));
    }

    [Fact]
    public void PA_ungive_does_not_close_the_visit()
    {
        using var w = new StockWorld();
        var f = Seed(w, pinBoth: false);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        using (var db = w.NewContext())
            w.Schedules(db).Update(new ScheduleDTO { Id = w.Schedule1Id, ChildId = w.ChildId, DoseId = (int)w.Dose1Id, DoctorId = w.DoctorId, PaId = f.PaId, IsDone = false, IsSkip = false, Date = StockWorld.Today });
        using var chk = w.NewContext();
        Assert.True(chk.PAAssignments.Any(a => a.Id == f.AsgId));
        Assert.False(chk.VaccineRefusals.Any(r => r.AssignmentId == f.AsgId));
    }

    [Fact]
    public void PA_can_request_a_refusal_after_payment_is_recorded_and_approval_reverses_everything()
    {
        using var w = new StockWorld();
        var f = Seed(w, pinBoth: false);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        RecordPayment(w, f, w.Schedule1Id);

        using (var db = w.NewContext())
        {
            var ok = Assert.IsType<OkObjectResult>(Ctl(db).RequestRefusal(f.AsgId, new CancelAssignmentDto { CallerId = f.PaId, Reason = "Refused at the door" }).GetAwaiter().GetResult());
            Assert.True((bool)JObject.FromObject(ok.Value!)["IsSuccess"]!);
        }
        using (var db = w.NewContext())
        {
            var ok = Assert.IsType<OkObjectResult>(Ctl(db).ApproveRefusal(f.AsgId, w.DoctorId).GetAwaiter().GetResult());
            Assert.True((bool)JObject.FromObject(ok.Value!)["IsSuccess"]!);
        }
        using var chk = w.NewContext();
        Assert.False(chk.PAAssignments.Any(a => a.Id == f.AsgId));
        var s = chk.Schedules.Single(x => x.Id == w.Schedule1Id);
        Assert.False(s.IsDone); Assert.False(s.IsPaymentCollected); Assert.Null(s.Amount);
        Assert.Equal("Approved", chk.VaccineRefusals.Single(r => r.AssignmentId == f.AsgId).Status);
    }

    [Fact]
    public void A_refusal_request_is_refused_once_the_doctor_confirmed_the_cash()
    {
        using var w = new StockWorld();
        var f = Seed(w, pinBoth: false);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.GiveDose1().IsSuccess);
        using (var db = w.NewContext()) { db.PAAssignments.Single(a => a.Id == f.AsgId).IsCashConfirmedByDoctor = true; db.SaveChanges(); }
        using var db2 = w.NewContext();
        var ok = Assert.IsType<OkObjectResult>(Ctl(db2).RequestRefusal(f.AsgId, new CancelAssignmentDto { CallerId = f.PaId }).GetAwaiter().GetResult());
        Assert.False((bool)JObject.FromObject(ok.Value!)["IsSuccess"]!);
        using var chk = w.NewContext();
        Assert.False(chk.VaccineRefusals.Any(r => r.AssignmentId == f.AsgId));
    }

    private static JObject CreateFor(StockWorld w, Fx f)
    {
        using var db = w.NewContext();
        var r = Ctl(db).Create(new CreateAssignmentDto
        {
            DoctorId = w.DoctorId, ClinicId = w.ClinicA, PersonalAssistantId = f.PaId, ChildId = w.ChildId,
            ScheduleIds = new List<long> { w.Schedule1Id }, GroupScheduleIds = new List<long> { w.Schedule1Id },
            CallerUserId = f.DoctorUserId, SecurityStamp = f.Stamp
        }).GetAwaiter().GetResult();
        return JObject.FromObject(((OkObjectResult)r).Value!);
    }

    // Faran Ahmed Butt: the orphan already exists (nothing given, no invoice). A new assignment must clear it, not block on it.
    [Fact]
    public void Create_clears_an_orphaned_empty_assignment_instead_of_blocking()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var res = CreateFor(w, f);
        Assert.True((bool)res["IsSuccess"]!, res.ToString());
        using var db = w.NewContext();
        Assert.False(db.PAAssignments.Any(a => a.Id == f.AsgId));
        Assert.Single(db.VaccineRefusals.Where(r => r.AssignmentId == f.AsgId && r.Status == "Approved").ToList());
        Assert.Equal(1, db.PAAssignmentSchedules.Count(l => l.ScheduleId == w.Schedule1Id));
    }

    [Fact]
    public void Create_still_blocks_when_the_old_visit_has_a_given_dose()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        GiveVisit(w);
        var res = CreateFor(w, f);
        Assert.False((bool)res["IsSuccess"]!);
        Assert.True((bool)res["BlockedByAssignment"]!);
        using var db = w.NewContext();
        Assert.True(db.PAAssignments.Any(a => a.Id == f.AsgId));
    }
}
