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
/// The assignment history is a real log of who did what and when (PA_TRACKING_MERGE_SPEC.md Q9):
/// give, ungive, payment, invoice confirm and the lifecycle approvals each write an entry through
/// the real endpoints, the history names the person, folds one visit's per-dose rows into one line,
/// keeps the "given" line after an ungive, and never shows a derived copy of a logged event.
/// </summary>
public class AssignmentAuditTrailTests
{
    private sealed class Fx { public long AsgId, PaId, DoctorUserId; public string Stamp = ""; }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    // The world's child with both of its doses (same visit) pinned to one assignment of PA "Atif Zubair".
    private static Fx Seed(StockWorld w)
    {
        using var db = w.NewContext();
        var paUser = new User { MobileNumber = "35" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(paUser); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Atif Zubair", DoctorId = w.DoctorId, UserId = paUser.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var asg = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = w.ChildId, PersonalAssistantId = pa.Id, AssignedAt = DateTime.UtcNow.AddHours(-2) };
        db.PAAssignments.Add(asg); db.SaveChanges();
        db.PAAssignmentSchedules.AddRange(
            new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = w.Schedule1Id },
            new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = w.Schedule2Id });
        db.SaveChanges();
        var doctorUserId = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx { AsgId = asg.Id, PaId = pa.Id, DoctorUserId = doctorUserId, Stamp = db.Users.Single(u => u.Id == doctorUserId).SecurityStamp };
    }

    private static JArray History(StockWorld w, long asgId, long? managerId = null)
    {
        using var db = w.NewContext();
        var ok = Assert.IsType<OkObjectResult>(Ctl(db).GetHistory(asgId, managerId).GetAwaiter().GetResult());
        return (JArray)JObject.FromObject(ok.Value!)["ResponseData"]!;
    }

    private static void RecordPayment(StockWorld w, Fx f, long scheduleId, string mode)
    {
        using var db = w.NewContext();
        var r = w.Schedules(db).RecordPaymentMode(scheduleId, new ScheduleDTO { PaymentMode = mode, DoctorId = w.DoctorId, CallerUserId = f.DoctorUserId, SecurityStamp = f.Stamp });
        Assert.True(Result.Of(r).IsSuccess);
    }

    private static void SetAmounts(StockWorld w, decimal each)
    {
        using var db = w.NewContext();
        foreach (var s in db.Schedules.Where(x => x.Id == w.Schedule1Id || x.Id == w.Schedule2Id)) s.Amount = each;
        db.SaveChanges();
    }

    [Fact]
    public void Doctor_bulk_give_of_a_pinned_visit_logs_one_named_given_line_and_no_derived_copy()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        w.PostBill(w.ClinicA, 5, 10);
        var r = w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id); Assert.True(r.IsSuccess, r.Message);

        var given = History(w, f.AsgId).Where(x => x.Value<string>("Action") == "Given").ToList();
        var e = Assert.Single(given);
        Assert.Equal("log", e.Value<string>("Source"));
        Assert.Equal("DOCTOR", e.Value<string>("Actor"));
        Assert.Equal("Dr. Test", e.Value<string>("ActorName"));
        Assert.Equal("Dose 1, Dose 2 given", e.Value<string>("Text"));
    }

    [Fact]
    public void Given_line_survives_an_ungive_and_the_ungive_is_logged_too()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id).IsSuccess);
        var u = w.BulkUngive(w.Schedule1Id, w.BrandId, true, w.Schedule1Id, w.Schedule2Id); Assert.True(u.IsSuccess, u.Message);

        // The doctor ungave the whole visit, so the assignment is closed (spec §11): its history can no
        // longer be read through the endpoint, but the log keeps Given, Ungiven and Reversed.
        using var db = w.NewContext();
        Assert.False(db.PAAssignments.Any(a => a.Id == f.AsgId));
        var actions = db.PaActivityLogs.Where(l => l.PatientId == w.ChildId && l.ActionCode.StartsWith("Asg"))
            .OrderBy(l => l.Id).Select(l => l.ActionCode).Distinct().ToList();
        Assert.Equal(new[] { "AsgGiven", "AsgUngiven", "AsgReversed" }, actions);
    }

    [Fact]
    public void Two_payments_of_one_visit_fold_into_one_line_with_the_total_and_the_mode()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id).IsSuccess);
        SetAmounts(w, 3900);
        RecordPayment(w, f, w.Schedule1Id, "Cash");
        RecordPayment(w, f, w.Schedule2Id, "Cash");

        var pay = Assert.Single(History(w, f.AsgId).Where(x => x.Value<string>("Action") == "PaymentRecorded"));
        Assert.Equal("Dose 1, Dose 2 payment recorded: Cash, 7800", pay.Value<string>("Text"));
        Assert.Equal("Dr. Test", pay.Value<string>("ActorName"));
        using var db = w.NewContext();
        Assert.Equal(2, db.PaActivityLogs.Count(l => l.ActionCode == "AsgPaymentRecorded"));   // one stored row per dose
    }

    [Fact]
    public void Confirming_the_invoice_logs_who_confirmed_and_replaces_the_derived_line()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        long invId;
        using (var db = w.NewContext())
        {
            var inv = new InvoiceSubmission { ChildId = w.ChildId, DoctorId = w.DoctorId, InvoiceDate = StockWorld.Today, SubmittedAt = DateTime.UtcNow, PaId = f.PaId, TotalAmount = 7800 };
            db.InvoiceSubmissions.Add(inv); db.SaveChanges();
            foreach (var s in db.Schedules.Where(x => x.Id == w.Schedule1Id || x.Id == w.Schedule2Id))
            { s.IsDone = true; s.DoneAt = DateTime.UtcNow; s.Amount = 3900; s.InvoiceSubmissionId = inv.Id; s.IsPaymentCollected = true; }
            db.PAAssignments.Single(a => a.Id == f.AsgId).InvoiceSubmissionId = inv.Id;
            db.SaveChanges();
            invId = inv.Id;
        }
        using (var db = w.NewContext())
            Assert.True(Result.Of(w.Schedules(db).ConfirmInvoice(invId, w.DoctorId, f.DoctorUserId, f.Stamp)).IsSuccess);

        var conf = Assert.Single(History(w, f.AsgId).Where(x => x.Value<string>("Action") == "Confirmed"));
        Assert.Equal("log", conf.Value<string>("Source"));
        Assert.Equal("Dr. Test", conf.Value<string>("ActorName"));
        Assert.Equal("Invoice " + invId + " confirmed, 7800. Assignment closed.", conf.Value<string>("Text"));
    }

    [Fact]
    public void Manager_history_leaves_out_payment_events()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id).IsSuccess);
        SetAmounts(w, 3900);
        RecordPayment(w, f, w.Schedule1Id, "Cash");
        long managerId;
        using (var db = w.NewContext())
        {
            var mu = new User { MobileNumber = "36" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "MANAGER", CountryCode = "92" };
            db.Users.Add(mu); db.SaveChanges();
            var m = new Manager { Name = "Mgr", UserId = mu.Id, DoctorId = w.DoctorId };
            db.Manager.Add(m); db.SaveChanges();
            db.ManagerPermissions.Add(new ManagerPermission { ManagerId = m.Id, ViewPaAssignmentStatus = true });
            db.ManagerAccess.Add(new ManagerAccess { ManagerId = m.Id, ClinicId = w.ClinicA });
            db.SaveChanges();
            managerId = m.Id;
        }
        var actions = History(w, f.AsgId, managerId).Select(x => x.Value<string>("Action")).ToList();
        Assert.Contains("Given", actions);
        Assert.DoesNotContain("PaymentRecorded", actions);
    }

    [Fact]
    public void Target_date_change_and_PA_cancel_request_and_its_approval_are_logged_with_the_reason()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using (var db = w.NewContext())
            Assert.True(Result.Of(Ctl(db).SetTargetDate(f.AsgId, new SetTargetDateDto { DoctorId = w.DoctorId, TargetDate = new DateTime(2026, 10, 25) }).GetAwaiter().GetResult()).IsSuccess);
        using (var db = w.NewContext())
            Assert.True(Result.Of(Ctl(db).RequestCancel(f.AsgId, new CancelAssignmentDto { CallerType = "PA", CallerId = f.PaId, Reason = "Parent postponed" }).GetAwaiter().GetResult()).IsSuccess);
        using (var db = w.NewContext())
            Assert.True(Result.Of(Ctl(db).ApproveCancelRequest(f.AsgId, w.DoctorId).GetAwaiter().GetResult()).IsSuccess);

        var h = History(w, f.AsgId);
        Assert.Equal(new[] { "TargetDateSet", "CancelRequested", "CancelApproved" }, h.Select(x => x.Value<string>("Action")).ToArray());
        var req = h.Single(x => x.Value<string>("Action") == "CancelRequested");
        Assert.Equal("Atif Zubair", req.Value<string>("ActorName"));
        Assert.Equal("Parent postponed", req.Value<string>("Reason"));
        Assert.Equal("Parent postponed", h.Single(x => x.Value<string>("Action") == "CancelApproved").Value<string>("Reason"));
    }
}
