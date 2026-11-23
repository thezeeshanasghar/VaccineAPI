using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Ungive after invoice (live case: Hakeem Ullah, 19 Nov 2026). A PA ungave a dose that had an invoice and cash, the
/// ungive wiped the dose's invoice link, the PA gave it again 7 seconds later, and the undecided "UngiveAfterPayment"
/// request stayed pending. Result: a leftover "Awaiting Invoice" row after Confirm, and an Approve button that would
/// have un-paid a given dose. These tests pin the fix.
/// </summary>
public class UngiveReversalGuardTests
{
    private sealed class Fx { public long ChildId, DoseId, Sched, Invoice, Log, Asg; public long DoctorUserId; public string Stamp = ""; }

    // One child, one dose, one active 9,500 invoice, one pending ungive request naming that dose and invoice.
    private static Fx Seed(StockWorld w, bool doseGiven, bool linked)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "35" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Hakeem Ullah", ClinicId = w.ClinicA, UserId = parent.Id, DOB = new DateTime(2024, 1, 20) };
        db.Childs.Add(child); db.SaveChanges();
        var v = new Vaccine { Name = "MenACWY", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var d = new Dose { Name = "MenACWY 1", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 36500 };
        db.Doses.Add(d); db.SaveChanges();
        var inv = new InvoiceSubmission
        {
            ChildId = child.Id, DoctorId = w.DoctorId, PaId = 17, ClinicId = w.ClinicA,
            InvoiceDate = DateTime.UtcNow.Date, SubmittedAt = DateTime.UtcNow,
            ConsultationFee = 1800m, TotalAmount = 9500m, InvoiceStatus = "Active"
        };
        db.InvoiceSubmissions.Add(inv); db.SaveChanges();
        var s = new Schedule
        {
            ChildId = child.Id, DoseId = d.Id, Date = DateTime.UtcNow.Date,
            IsDone = doseGiven, GivenDate = doseGiven ? DateTime.UtcNow.Date : (DateTime?)null,
            GivenByPaId = doseGiven ? 17 : (long?)null, PaymentCollectorPaId = doseGiven ? 17 : (long?)null,
            IsPaymentCollected = doseGiven, InvoiceSubmissionId = linked ? inv.Id : (long?)null
        };
        db.Schedules.Add(s); db.SaveChanges();
        var log = new PaActivityLog
        {
            DoctorId = w.DoctorId, PatientId = child.Id, ActionCode = "UngiveAfterPayment", IsReversal = true,
            Description = "MenACWY 1 ungiven after invoicing",
            Notes = $"Amount pending reversal: 7700.00 | ScheduleId: {s.Id} | InvoiceSubmissionId: {inv.Id}",
            ActionDate = DateTime.UtcNow
        };
        db.PaActivityLogs.Add(log);
        var asg = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = 17, IsAutoCreated = true, AssignedAt = DateTime.UtcNow };
        db.PAAssignments.Add(asg); db.SaveChanges();
        db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = s.Id });
        db.SaveChanges();
        var doctorUserId = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx { ChildId = child.Id, DoseId = d.Id, Sched = s.Id, Invoice = inv.Id, Log = log.Id, Asg = asg.Id, DoctorUserId = doctorUserId,
                        Stamp = db.Users.Single(u => u.Id == doctorUserId).SecurityStamp };
    }

    private static Schedule Dose(StockWorld w, long id) { using var db = w.NewContext(); return db.Schedules.Single(s => s.Id == id); }

    // ---- relink on re-give ----

    [Fact]
    public void Regive_puts_the_dose_back_on_its_invoice_while_the_request_is_pending()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: true, linked: false);   // the Hakeem state after ungive + re-give
        using (var db = w.NewContext())
        {
            var s = db.Schedules.Single(x => x.Id == f.Sched);
            Assert.True(UngiveReversalGuard.RelinkOnRegive(db, s));
            db.SaveChanges();
        }
        Assert.Equal(f.Invoice, Dose(w, f.Sched).InvoiceSubmissionId);
    }

    [Fact]
    public void Regive_does_not_relink_to_a_cancelled_invoice()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using var db = w.NewContext();
        db.InvoiceSubmissions.Single(i => i.Id == f.Invoice).InvoiceStatus = "Cancelled"; db.SaveChanges();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    [Fact]
    public void Regive_does_not_relink_once_the_doctor_has_decided_the_request()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using var db = w.NewContext();
        db.PaActivityLogs.Single(l => l.Id == f.Log).IsReversalRejected = true; db.SaveChanges();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    [Fact]
    public void Regive_leaves_a_dose_that_is_already_linked_alone()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, true);
        using var db = w.NewContext();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    [Fact]
    public void A_dose_that_is_not_given_is_never_relinked()
    {
        using var w = new StockWorld();
        var f = Seed(w, false, false);
        using var db = w.NewContext();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    // ---- approve is refused for a stale request ----

    [Fact]
    public void Approve_is_refused_when_the_dose_was_given_again_and_nothing_is_changed()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: true, linked: false);
        using (var db = w.NewContext())
        {
            var r = Result.Of(new PaActivityLogController(db).ApproveReversal(f.Log));
            Assert.False(r.IsSuccess);
            Assert.Contains("given again", r.Message);
        }
        Assert.True(Dose(w, f.Sched).IsPaymentCollected);   // the given, paid dose was not un-paid
        using var db2 = w.NewContext();
        Assert.False(db2.PaActivityLogs.Single(l => l.Id == f.Log).IsReversalApproved);
    }

    [Fact]
    public void Reject_still_works_on_a_stale_request()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using var db = w.NewContext();
        Assert.True(Result.Of(new PaActivityLogController(db).RejectReversal(f.Log)).IsSuccess);
    }

    // ---- pending list carries what the doctor needs ----

    [Fact]
    public void Pending_list_names_the_patient_dose_invoice_and_marks_a_regiven_dose_stale()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaActivityLogController(db).GetPendingReversals(w.DoctorId);
        var row = JObject.FromObject(res.Value!)["ResponseData"]!.First(r => (long)r["Id"]! == f.Log);
        Assert.Equal("Hakeem Ullah", (string)row["PatientName"]!);
        Assert.Equal("MenACWY 1", (string)row["DoseName"]!);
        Assert.Equal(f.Invoice, (long)row["InvoiceId"]!);
        Assert.Equal(9500m, (decimal)row["InvoiceAmount"]!);
        Assert.True((bool)row["IsStale"]!);
    }

    [Fact]
    public void Pending_list_does_not_mark_a_still_ungiven_dose_stale()
    {
        using var w = new StockWorld();
        var f = Seed(w, false, false);
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaActivityLogController(db).GetPendingReversals(w.DoctorId);
        var row = JObject.FromObject(res.Value!)["ResponseData"]!.First(r => (long)r["Id"]! == f.Log);
        Assert.False((bool)row["IsStale"]!);
    }

    // ---- invoice row flag and Confirm block ----

    private static JToken InvoiceRow(StockWorld w, long invoiceId)
    {
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaCashHandoverController(db).GetReconciliation(w.DoctorId);
        return JObject.FromObject(res.Value!)["ResponseData"]!
            .First(r => (string)r["RowType"]! == "Invoice" && (long?)r["InvoiceSubmissionId"] == invoiceId);
    }

    [Fact]
    public void Invoice_row_is_flagged_when_a_pending_ungive_leaves_its_dose_not_given()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: false, linked: false);
        var row = InvoiceRow(w, f.Invoice);
        Assert.True((bool)row["HasUngivePending"]!);
        Assert.Equal(f.Log, (long)row["UngivePendingLogId"]!);
    }

    [Fact]
    public void Invoice_row_is_not_flagged_when_the_dose_was_given_again()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        Assert.False((bool)InvoiceRow(w, f.Invoice)["HasUngivePending"]!);
    }

    [Fact]
    public void Confirm_is_blocked_while_a_pending_ungive_leaves_the_dose_not_given()
    {
        using var w = new StockWorld();
        var f = Seed(w, false, false);
        using var db = w.NewContext();
        var r = Result.Of(w.Schedules(db).ConfirmInvoice(f.Invoice, w.DoctorId, f.DoctorUserId, f.Stamp));
        Assert.False(r.IsSuccess);
        Assert.Contains("pending", r.Message);
        using var db2 = w.NewContext();
        Assert.False(db2.InvoiceSubmissions.Single(i => i.Id == f.Invoice).IsConfirmedByDoctor);
    }

    [Fact]
    public void Confirm_works_when_the_pending_request_is_stale_because_the_dose_is_given_again()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, true);
        using var db = w.NewContext();
        Assert.True(Result.Of(w.Schedules(db).ConfirmInvoice(f.Invoice, w.DoctorId, f.DoctorUserId, f.Stamp)).IsSuccess);
    }

    // ---- leftover Awaiting row ----

    private static JToken? AwaitingRow(StockWorld w, long asgId)
    {
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaCashHandoverController(db).GetReconciliation(w.DoctorId);
        return JObject.FromObject(res.Value!)["ResponseData"]!
            .FirstOrDefault(r => (string)r["RowType"]! == "AwaitingInvoice" && (long?)r["AssignmentId"] == asgId);
    }

    [Fact]
    public void A_cash_confirmed_assignment_is_never_listed_as_awaiting_an_invoice()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: true, linked: false);   // dose lost its invoice link, like Hakeem
        Assert.NotNull(AwaitingRow(w, f.Asg));              // open assignment: still shown
        using (var db = w.NewContext())
        {
            var a = db.PAAssignments.Single(x => x.Id == f.Asg);
            a.IsCashConfirmedByDoctor = true; a.CashConfirmedAt = DateTime.UtcNow; a.IsCompleted = true;
            db.SaveChanges();
        }
        Assert.Null(AwaitingRow(w, f.Asg));                 // closed: gone
    }

    // ---- hardening D1-D4 and guards ----

    [Fact]
    public void D1_an_old_request_is_never_used_to_relink()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using var db = w.NewContext();
        db.PaActivityLogs.Single(l => l.Id == f.Log).ActionDate = DateTime.UtcNow.AddDays(-3); db.SaveChanges();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    [Fact]
    public void D1_a_confirmed_invoice_is_never_relinked()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using var db = w.NewContext();
        db.InvoiceSubmissions.Single(i => i.Id == f.Invoice).IsConfirmedByDoctor = true; db.SaveChanges();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    [Fact]
    public void D1_a_request_from_a_different_PA_is_not_used()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);   // dose given by PA 17
        using var db = w.NewContext();
        var u = new User { MobileNumber = "36" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var other = new PersonalAssistant { Name = "Other PA", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(other); db.SaveChanges();
        db.PaActivityLogs.Single(l => l.Id == f.Log).PaId = other.Id;   // request written for another PA
        db.SaveChanges();
        Assert.False(UngiveReversalGuard.RelinkOnRegive(db, db.Schedules.Single(x => x.Id == f.Sched)));
    }

    [Fact]
    public void D3_relink_restores_the_dose_amount_and_unvoids_the_certificate_but_not_the_payment()
    {
        using var w = new StockWorld();
        var f = Seed(w, true, false);
        using (var db = w.NewContext())
        {
            db.Invoices.Add(new Invoice { DoseId = f.DoseId, ChildId = f.ChildId, DoctorId = w.DoctorId, IsVoided = true, SupersededBy = "UNGIVEN" });
            var s = db.Schedules.Single(x => x.Id == f.Sched);
            s.IsPaymentCollected = false; s.Amount = null;   // what a PA ungive leaves behind
            db.SaveChanges();
            Assert.True(UngiveReversalGuard.RelinkOnRegive(db, s));
            db.SaveChanges();
        }
        var d = Dose(w, f.Sched);
        Assert.Equal(f.Invoice, d.InvoiceSubmissionId);
        Assert.Equal(7700m, d.Amount);
        Assert.False(d.IsPaymentCollected);   // the PA re-records the payment
        using var db2 = w.NewContext();
        Assert.False(db2.Invoices.Single(i => i.DoseId == f.DoseId && i.ChildId == f.ChildId).IsVoided);
    }

    [Fact]
    public void D4_the_invoice_id_is_read_up_to_the_next_separator()
    {
        var p = UngiveReversalGuard.ParseNotes("Amount pending reversal: 7700 | ScheduleId: 5 | InvoiceSubmissionId: 35441 | PrevMode: Cash");
        Assert.Equal(5, p.ScheduleId);
        Assert.Equal(35441, p.InvoiceId);
        Assert.Equal(7700m, UngiveReversalGuard.ParseAmount("Ungiven by Manager (Id 3). Amount pending reversal: 7700.00 | ScheduleId: 5"));
        Assert.Equal(0m, UngiveReversalGuard.ParseAmount("no amount here"));
    }

    [Fact]
    public void D2_approve_reduces_the_invoice_by_the_amount_in_the_request_note()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: false, linked: false);   // the Yasir state: ungiven, Amount already nulled
        using (var db = w.NewContext())
            Assert.True(Result.Of(new PaActivityLogController(db).ApproveReversal(f.Log)).IsSuccess);
        using var db2 = w.NewContext();
        Assert.Equal(1800m, db2.InvoiceSubmissions.Single(i => i.Id == f.Invoice).TotalAmount);   // 9,500 - 7,700
    }

    [Fact]
    public void Confirm_is_refused_when_a_given_paid_dose_of_the_visit_has_no_invoice_link()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: true, linked: false);   // the Hakeem state
        using (var db = w.NewContext())
        {
            db.PAAssignments.Single(a => a.Id == f.Asg).InvoiceSubmissionId = f.Invoice;
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        var r = Result.Of(w.Schedules(db2).ConfirmInvoice(f.Invoice, w.DoctorId, f.DoctorUserId, f.Stamp));
        Assert.False(r.IsSuccess);
        Assert.Contains("not linked", r.Message);
        using var db3 = w.NewContext();
        Assert.False(db3.InvoiceSubmissions.Single(i => i.Id == f.Invoice).IsConfirmedByDoctor);
    }

    [Fact]
    public void Doctor_ungive_of_a_dose_on_a_confirmed_invoice_is_refused_and_the_invoice_survives()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: true, linked: true);
        using (var db = w.NewContext())
        {
            db.InvoiceSubmissions.Single(i => i.Id == f.Invoice).IsConfirmedByDoctor = true; db.SaveChanges();
        }
        using (var db = w.NewContext())
        {
            var r = w.Schedules(db).Update(new ScheduleDTO { Id = f.Sched, ChildId = f.ChildId, DoseId = (int)f.DoseId, DoctorId = w.DoctorId, IsDone = false, IsSkip = false, Date = StockWorld.Today });
            Assert.False(r.IsSuccess);
            Assert.Contains("confirmed", r.Message);
        }
        using var db2 = w.NewContext();
        Assert.True(db2.InvoiceSubmissions.Any(i => i.Id == f.Invoice));
        Assert.True(db2.Schedules.Single(s => s.Id == f.Sched).IsDone);
    }

    [Fact]
    public void Doctor_ungive_of_a_dose_on_an_unconfirmed_invoice_still_works()
    {
        using var w = new StockWorld();
        var f = Seed(w, doseGiven: true, linked: true);
        using (var db = w.NewContext())
            w.Schedules(db).Update(new ScheduleDTO { Id = f.Sched, ChildId = f.ChildId, DoseId = (int)f.DoseId, DoctorId = w.DoctorId, IsDone = false, IsSkip = false, Date = StockWorld.Today });
        using var db2 = w.NewContext();
        Assert.False(db2.Schedules.Single(s => s.Id == f.Sched).IsDone);   // behaviour unchanged for unconfirmed
    }
}
