using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Record-only approval (HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md, Approve design 2026-11-23): a PA entry with no
/// invoice, brand, fee or payment is a record, not a cash liability. The doctor approves it; the server decides
/// from the DB (never the screen) and the bin refuses to reverse it without an explicit flag.
/// </summary>
public class PaRecordApproveTests
{
    private sealed class Fx { public long ChildId, Asg, S1, S2; public long DoctorUserId; public string Stamp = ""; }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    // One child, one assignment pinning two GIVEN doses with no brand/stock/invoice/fee/payment (a record-only entry).
    private static Fx Seed(StockWorld w)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "35" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Ayra", ClinicId = w.ClinicA, UserId = parent.Id, DOB = new DateTime(2026, 1, 20) };
        db.Childs.Add(child); db.SaveChanges();
        var v = new Vaccine { Name = "V", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var d = new Dose { Name = "BCG", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 3650 };
        db.Doses.Add(d); db.SaveChanges();
        Schedule S(DateTime given) => new Schedule { ChildId = child.Id, DoseId = d.Id, Date = given, GivenDate = given, IsDone = true, GivenByPaId = 17, PaymentCollectorPaId = 17 };
        var s1 = S(new DateTime(2026, 2, 4)); var s2 = S(new DateTime(2026, 2, 4));
        db.Schedules.AddRange(s1, s2); db.SaveChanges();
        var asg = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = 17, IsAutoCreated = true, AssignedAt = DateTime.UtcNow };
        db.PAAssignments.Add(asg); db.SaveChanges();
        db.PAAssignmentSchedules.AddRange(
            new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = s1.Id },
            new PAAssignmentSchedule { AssignmentId = asg.Id, ScheduleId = s2.Id });
        db.SaveChanges();
        var doctorUserId = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx { ChildId = child.Id, Asg = asg.Id, S1 = s1.Id, S2 = s2.Id, DoctorUserId = doctorUserId,
                        Stamp = db.Users.Single(u => u.Id == doctorUserId).SecurityStamp };
    }

    private static IActionResult Approve(StockWorld w, Fx f, long? userId = null, string? stamp = null)
    {
        using var db = w.NewContext();
        return Ctl(db).ApproveRecord(f.Asg, new ApproveRecordDto { CallerUserId = userId ?? f.DoctorUserId, SecurityStamp = stamp ?? f.Stamp }).GetAwaiter().GetResult();
    }

    private static void Mutate(StockWorld w, long scheduleId, Action<Schedule> m)
    {
        using var db = w.NewContext();
        m(db.Schedules.Single(s => s.Id == scheduleId)); db.SaveChanges();
    }

    private static PAAssignment Asg(StockWorld w, long id) { using var db = w.NewContext(); return db.PAAssignments.Single(a => a.Id == id); }

    [Fact]
    public void Doctor_can_approve_a_record_only_entry_and_it_stays_uncancelled_and_unconfirmed()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var r = Result.Of(Approve(w, f));
        Assert.True(r.IsSuccess, r.Message);
        var a = Asg(w, f.Asg);
        Assert.NotNull(a.RecordApprovedAt);
        Assert.Equal(w.DoctorId, a.RecordApprovedBy);
        Assert.False(a.IsCancelled);
        Assert.False(a.IsCashConfirmedByDoctor);   // never writes the cash-confirm columns
    }

    [Fact]
    public void Approve_writes_an_audit_row()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(Approve(w, f)).IsSuccess);
        using var db = w.NewContext();
        Assert.Contains(db.PaActivityLogs.ToList(), l => l.ActionCode == "AsgRecordApproved" && l.PatientId == f.ChildId);
    }

    [Fact]
    public void Stale_screen_is_refused_when_an_invoice_appeared_after_the_page_loaded()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Mutate(w, f.S2, s => s.InvoiceSubmissionId = 4517);   // Atif downloaded an invoice after the doctor loaded the page
        var r = Result.Of(Approve(w, f));
        Assert.False(r.IsSuccess);
        Assert.Contains("money", r.Message);
        Assert.Null(Asg(w, f.Asg).RecordApprovedAt);
    }

    [Fact]
    public void A_brand_a_fee_a_payment_or_stock_each_block_approval()
    {
        var cases = new List<Action<Schedule>>
        {
            s => s.BrandId = 1,
            s => s.Amount = 500m,
            s => s.IsPaymentCollected = true,
            s => s.StockId = 9,
        };
        foreach (var m in cases)
        {
            using var w = new StockWorld();
            var f = Seed(w);
            Mutate(w, f.S1, m);
            Assert.False(Result.Of(Approve(w, f)).IsSuccess);
            Assert.Null(Asg(w, f.Asg).RecordApprovedAt);
        }
    }

    [Fact]
    public void A_dose_that_is_not_given_blocks_approval()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Mutate(w, f.S2, s => s.IsDone = false);
        Assert.False(Result.Of(Approve(w, f)).IsSuccess);
    }

    [Fact]
    public void An_assignment_with_no_pinned_doses_cannot_be_approved()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using (var db = w.NewContext()) { db.PAAssignmentSchedules.RemoveRange(db.PAAssignmentSchedules.Where(l => l.AssignmentId == f.Asg)); db.SaveChanges(); }
        Assert.False(Result.Of(Approve(w, f)).IsSuccess);
    }

    [Fact]
    public void Bad_stamp_or_wrong_user_is_refused()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.False(Result.Of(Approve(w, f, stamp: "not-the-stamp")).IsSuccess);
        Assert.False(Result.Of(Approve(w, f, userId: 999999)).IsSuccess);
        Assert.Null(Asg(w, f.Asg).RecordApprovedAt);
    }

    [Fact]
    public void Cancelled_cash_confirmed_or_already_approved_rows_are_refused()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(Approve(w, f)).IsSuccess);
        Assert.False(Result.Of(Approve(w, f)).IsSuccess);           // already approved

        using var w2 = new StockWorld();
        var g = Seed(w2);
        using (var db = w2.NewContext()) { db.PAAssignments.Single(a => a.Id == g.Asg).IsCancelled = true; db.SaveChanges(); }
        Assert.False(Result.Of(Approve(w2, g)).IsSuccess);

        using var w3 = new StockWorld();
        var h = Seed(w3);
        using (var db = w3.NewContext()) { db.PAAssignments.Single(a => a.Id == h.Asg).IsCashConfirmedByDoctor = true; db.SaveChanges(); }
        Assert.False(Result.Of(Approve(w3, h)).IsSuccess);
    }

    [Fact]
    public void Date_is_not_part_of_the_test_a_current_dated_entry_is_record_only_too()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Mutate(w, f.S1, s => s.GivenDate = DateTime.UtcNow);
        using var db = w.NewContext();
        Assert.True(PaOwnership.IsRecordOnly(db, f.Asg));
    }

    [Fact]
    public void Money_appearing_after_approval_makes_the_row_not_record_only_again()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(Approve(w, f)).IsSuccess);
        Mutate(w, f.S1, s => s.Amount = 800m);   // Charge Fee after approval
        using var db = w.NewContext();
        Assert.False(PaOwnership.IsRecordOnly(db, f.Asg));   // the recon row stops showing it as approved
    }

    // ---- bin: server-side refusal ----

    [Fact]
    public void Full_reset_of_a_record_only_row_is_refused_without_the_explicit_flag()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using var db = w.NewContext();
        var r = Result.Of(Ctl(db).DeleteAssignment(f.Asg, w.DoctorId, "FullReset", "test").GetAwaiter().GetResult());
        Assert.False(r.IsSuccess);
        Assert.Contains("no money", r.Message);
        using var chk = w.NewContext();
        Assert.True(chk.Schedules.Single(s => s.Id == f.S1).IsDone);   // nothing was un-given
    }

    [Fact]
    public void Full_reset_of_a_record_only_row_proceeds_with_the_explicit_flag()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using var db = w.NewContext();
        var r = Result.Of(Ctl(db).DeleteAssignment(f.Asg, w.DoctorId, "FullReset", "test", true, true).GetAwaiter().GetResult());
        Assert.True(r.IsSuccess, r.Message);
    }

    [Fact]
    public void Unassign_only_is_not_blocked_by_the_record_guard()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using var db = w.NewContext();
        var r = Result.Of(Ctl(db).DeleteAssignment(f.Asg, w.DoctorId, "UnassignOnly").GetAwaiter().GetResult());
        Assert.True(r.IsSuccess, r.Message);
    }

    // ---- reconciliation rows: what the Cash table reads ----

    private static Newtonsoft.Json.Linq.JToken AwaitingRow(StockWorld w, long asgId)
    {
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaCashHandoverController(db).GetReconciliation(w.DoctorId);
        var j = Newtonsoft.Json.Linq.JObject.FromObject(res.Value!);
        return j["ResponseData"]!.First(r => (string)r["RowType"]! == "AwaitingInvoice" && (long?)r["AssignmentId"] == asgId);
    }

    [Fact]
    public void Recon_row_is_flagged_record_only_with_its_doses_and_not_yet_approved()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var row = AwaitingRow(w, f.Asg);
        Assert.True((bool)row["IsRecordOnly"]!);
        Assert.False((bool)row["RecordApproved"]!);
        Assert.Equal(2, ((Newtonsoft.Json.Linq.JArray)row["RecordDoses"]!).Count);
        Assert.Equal("BCG", (string)row["RecordDoses"]![0]!["Name"]!);
        Assert.Equal("2026-02-04", (string)row["RecordDoses"]![0]!["GivenDate"]!);
    }

    [Fact]
    public void Recon_row_shows_approved_after_approval_and_stays_listed()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(Approve(w, f)).IsSuccess);
        var row = AwaitingRow(w, f.Asg);
        Assert.True((bool)row["IsRecordOnly"]!);
        Assert.True((bool)row["RecordApproved"]!);
        Assert.NotNull(row["RecordApprovedAt"]);
    }

    [Fact]
    public void Recon_row_loses_record_only_and_approval_when_a_fee_appears()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Assert.True(Result.Of(Approve(w, f)).IsSuccess);
        Mutate(w, f.S1, s => s.Amount = 800m);
        var row = AwaitingRow(w, f.Asg);
        Assert.False((bool)row["IsRecordOnly"]!);
        Assert.False((bool)row["RecordApproved"]!);   // approval voided, not shown as approved
    }

    [Fact]
    public void Recon_row_with_a_brand_is_never_record_only()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        Mutate(w, f.S2, s => s.BrandId = w.BrandId);
        Assert.False((bool)AwaitingRow(w, f.Asg)["IsRecordOnly"]!);
    }
}
