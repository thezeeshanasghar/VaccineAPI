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
/// Take it back, Reassign's collector rule, the audit trail, and the doctor/manager read model
/// (PA_TRACKING_MERGE_SPEC.md Q4, Q7, Q9, Q10). Every test checks the database afterwards, not just
/// the HTTP result, and that the sibling vaccine group of the same child is never touched.
/// </summary>
public class PaAssignmentLifecycleTests
{
    private sealed class Fx { public long ChildId, AsgA, AsgB, A1, A2, B1, DoctorUserId; public string Stamp = ""; }

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    // One child, group A (two doses, 19 Oct) and group B (one dose, 16 Nov), one assignment each, same PA 17.
    private static Fx Seed(StockWorld w)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Hania", ClinicId = w.ClinicA, UserId = parent.Id, DOB = new DateTime(2026, 7, 13) };
        db.Childs.Add(child); db.SaveChanges();
        var v = new Vaccine { Name = "V", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var d = new Dose { Name = "Penta-1", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 3650 };
        db.Doses.Add(d); db.SaveChanges();
        Schedule S(DateTime dt) => new Schedule { ChildId = child.Id, DoseId = d.Id, Date = dt, PaymentCollectorPaId = 17 };
        var a1 = S(new DateTime(2026, 10, 19)); var a2 = S(new DateTime(2026, 10, 19)); var b1 = S(new DateTime(2026, 11, 16));
        db.Schedules.AddRange(a1, a2, b1); db.SaveChanges();
        var asgA = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = 17, AssignedAt = DateTime.UtcNow.AddDays(-4) };
        var asgB = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = 17, AssignedAt = DateTime.UtcNow.AddDays(-3) };
        db.PAAssignments.AddRange(asgA, asgB); db.SaveChanges();
        db.PAAssignmentSchedules.AddRange(
            new PAAssignmentSchedule { AssignmentId = asgA.Id, ScheduleId = a1.Id },
            new PAAssignmentSchedule { AssignmentId = asgA.Id, ScheduleId = a2.Id },
            new PAAssignmentSchedule { AssignmentId = asgB.Id, ScheduleId = b1.Id });
        db.SaveChanges();
        var doctorUserId = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
        return new Fx { ChildId = child.Id, AsgA = asgA.Id, AsgB = asgB.Id, A1 = a1.Id, A2 = a2.Id, B1 = b1.Id,
                        DoctorUserId = doctorUserId, Stamp = db.Users.Single(u => u.Id == doctorUserId).SecurityStamp };
    }

    private static void GiveGroupA(StockWorld w, Fx f, bool invoice = false, bool confirmedInvoice = false, bool alsoOnB = false)
    {
        using var db = w.NewContext();
        foreach (var id in new[] { f.A1, f.A2 })
        {
            var s = db.Schedules.Single(x => x.Id == id);
            s.IsDone = true; s.GivenByPaId = 17; s.DoneAt = DateTime.UtcNow.AddDays(-3); s.Amount = 4000;
        }
        if (invoice)
        {
            var inv = new InvoiceSubmission { ChildId = f.ChildId, DoctorId = w.DoctorId, InvoiceDate = new DateTime(2026, 10, 19), SubmittedAt = DateTime.UtcNow.AddDays(-3),
                                              PaId = 17, TotalAmount = 8000, IsConfirmedByDoctor = confirmedInvoice, ConfirmedAt = confirmedInvoice ? DateTime.UtcNow : (DateTime?)null };
            db.InvoiceSubmissions.Add(inv); db.SaveChanges();
            foreach (var id in new[] { f.A1, f.A2 }) db.Schedules.Single(x => x.Id == id).InvoiceSubmissionId = inv.Id;
            if (alsoOnB) db.Schedules.Single(x => x.Id == f.B1).InvoiceSubmissionId = inv.Id;
            db.PAAssignments.Single(a => a.Id == f.AsgA).InvoiceSubmissionId = inv.Id;
        }
        db.SaveChanges();
    }

    private static IActionResult TakeBack(StockWorld w, long asgId, long userId, string stamp, string? reason = "Atif is unwell")
    {
        using var db = w.NewContext();
        return Ctl(db).TakeBack(asgId, new TakeBackDto { Reason = reason, CallerUserId = userId, SecurityStamp = stamp }).GetAwaiter().GetResult();
    }

    private static IActionResult Reassign(StockWorld w, Fx f, long asgId, long newPa, string? reason = "PA unavailable")
    {
        using var db = w.NewContext();
        return Ctl(db).Reassign(asgId, new ReassignDto { NewPaId = newPa, Reason = reason, CallerUserId = f.DoctorUserId, SecurityStamp = f.Stamp }).GetAwaiter().GetResult();
    }

    private static List<AssignmentAudit.Entry> Audit(StockWorld w, long childId, params long[] asgIds)
    {
        using var db = w.NewContext();
        return AssignmentAudit.Read(db, childId, asgIds);
    }

    // ---------------- Take it back (Q4)

    [Fact]
    public void TakeBack_keeps_doses_given_removes_the_PAs_credit_and_closes_only_this_assignment()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true);

        var r = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp));
        Assert.True(r.IsSuccess, r.Message);

        using var db = w.NewContext();
        var a = db.PAAssignments.Single(x => x.Id == f.AsgA);
        Assert.True(a.IsCancelled);
        Assert.StartsWith("Taken back by the doctor", a.CancelReason);
        foreach (var id in new[] { f.A1, f.A2 })
        {
            var s = db.Schedules.Single(x => x.Id == id);
            Assert.True(s.IsDone);                       // nothing was ungiven
            Assert.Null(s.PaymentCollectorPaId);          // the PA is no longer credited
            Assert.Equal(17, s.GivenByPaId);              // the fact that the PA gave it is kept
            Assert.Equal(4000, s.Amount);
        }
        Assert.Null(db.InvoiceSubmissions.Single().PaId);   // the invoice belongs to the doctor now
        Assert.Equal(2, db.PAAssignmentSchedules.Count(l => l.AssignmentId == f.AsgA));   // history of what it pinned is kept

        // the other group is untouched
        Assert.False(db.PAAssignments.Single(x => x.Id == f.AsgB).IsCancelled);
        Assert.Equal(17, db.Schedules.Single(x => x.Id == f.B1).PaymentCollectorPaId);

        var entry = Assert.Single(Audit(w, f.ChildId, f.AsgA));
        Assert.Equal("TakenBack", entry.Action);
        Assert.Equal("DOCTOR", entry.Actor);
        Assert.Equal("Atif is unwell", entry.Reason);
    }

    [Fact]
    public void TakeBack_is_refused_once_payment_is_recorded_and_changes_nothing()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true);
        using (var db = w.NewContext()) { db.Schedules.Single(x => x.Id == f.A1).IsPaymentCollected = true; db.SaveChanges(); }

        var r = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp));

        Assert.False(r.IsSuccess);
        Assert.Contains("Payment is already recorded", r.Message);
        using var chk = w.NewContext();
        Assert.False(chk.PAAssignments.Single(x => x.Id == f.AsgA).IsCancelled);
        Assert.Equal(17, chk.Schedules.Single(x => x.Id == f.A2).PaymentCollectorPaId);
        Assert.Equal(17, chk.InvoiceSubmissions.Single().PaId);
        Assert.Empty(Audit(w, f.ChildId, f.AsgA));
    }

    [Fact]
    public void TakeBack_is_refused_when_nothing_is_given_yet()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var r = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp));
        Assert.False(r.IsSuccess);
        Assert.Contains("use Cancel", r.Message);
    }

    [Fact]
    public void TakeBack_is_refused_for_someone_who_is_not_the_owning_doctor()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f);
        var wrongStamp = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, "not-the-stamp"));
        var wrongUser = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId + 99, f.Stamp));
        Assert.False(wrongStamp.IsSuccess);
        Assert.False(wrongUser.IsSuccess);
        using var chk = w.NewContext();
        Assert.False(chk.PAAssignments.Single(x => x.Id == f.AsgA).IsCancelled);
    }

    [Fact]
    public void TakeBack_twice_is_refused_the_second_time_and_logs_once()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f);
        Assert.True(Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp)).IsSuccess);
        var again = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp));
        Assert.False(again.IsSuccess);
        Assert.Contains("already cancelled", again.Message);
        Assert.Single(Audit(w, f.ChildId, f.AsgA));
    }

    [Fact]
    public void TakeBack_is_refused_after_the_cash_is_confirmed()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f);
        using (var db = w.NewContext()) { db.PAAssignments.Single(x => x.Id == f.AsgA).IsCashConfirmedByDoctor = true; db.SaveChanges(); }
        var r = Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp));
        Assert.False(r.IsSuccess);
        Assert.Contains("Amendment", r.Message);
    }

    [Fact]
    public void TakeBack_does_not_relabel_a_confirmed_invoice_or_one_shared_with_the_other_group()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true, confirmedInvoice: true);
        Assert.True(Result.Of(TakeBack(w, f.AsgA, f.DoctorUserId, f.Stamp)).IsSuccess);
        using (var chk = w.NewContext()) Assert.Equal(17, chk.InvoiceSubmissions.Single().PaId);    // confirmed: left alone

        using var w2 = new StockWorld();
        var g = Seed(w2); GiveGroupA(w2, g, invoice: true, alsoOnB: true);                          // legacy invoice spanning both groups
        Assert.True(Result.Of(TakeBack(w2, g.AsgA, g.DoctorUserId, g.Stamp)).IsSuccess);
        using var chk2 = w2.NewContext();
        Assert.Equal(17, chk2.InvoiceSubmissions.Single().PaId);                                     // shared: left alone
    }

    // ---------------- Reassign (Q10) and the audit trail (Q9)

    [Fact]
    public void Reassign_moves_unpaid_credit_but_collected_cash_stays_with_the_old_PA()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f);
        using (var db = w.NewContext()) { db.Schedules.Single(x => x.Id == f.A1).IsPaymentCollected = true; db.SaveChanges(); }   // A1 paid to PA 17, A2 not

        var r = Result.Of(Reassign(w, f, f.AsgA, newPa: 19));
        Assert.True(r.IsSuccess, r.Message);

        using var db2 = w.NewContext();
        Assert.Equal(17, db2.Schedules.Single(x => x.Id == f.A1).PaymentCollectorPaId);   // cash already collected stays with Atif
        Assert.Equal(19, db2.Schedules.Single(x => x.Id == f.A2).PaymentCollectorPaId);   // unpaid credit moves
        var newAsg = db2.PAAssignments.Single(x => x.ReassignedFromAssignmentId == f.AsgA);
        Assert.Equal(19, newAsg.PersonalAssistantId);
        Assert.Equal(2, db2.PAAssignmentSchedules.Count(l => l.AssignmentId == newAsg.Id));
        Assert.True(db2.PAAssignments.Single(x => x.Id == f.AsgA).IsCancelled);

        // the sibling group keeps its own assignment, PA and credit
        var b = db2.PAAssignments.Single(x => x.Id == f.AsgB);
        Assert.False(b.IsCancelled);
        Assert.Equal(17, b.PersonalAssistantId);
        Assert.Equal(17, db2.Schedules.Single(x => x.Id == f.B1).PaymentCollectorPaId);

        var e = Assert.Single(Audit(w, f.ChildId, newAsg.Id));
        Assert.Equal("Reassigned", e.Action);
        Assert.Equal("PA unavailable", e.Reason);
    }

    [Fact]
    public void Cancel_writes_an_audit_entry_with_the_reason_and_who_did_it()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using (var db = w.NewContext())
            Assert.True(Result.Of(Ctl(db).Cancel(f.AsgA, new CancelAssignmentDto { CallerType = "DOCTOR", CallerId = w.DoctorId, Reason = "Parent postponed" }).GetAwaiter().GetResult()).IsSuccess);
        var e = Assert.Single(Audit(w, f.ChildId, f.AsgA));
        Assert.Equal("Cancelled", e.Action);
        Assert.Equal("DOCTOR", e.Actor);
        Assert.Equal(w.DoctorId, e.ActorId);
        Assert.Equal("Parent postponed", e.Reason);
        Assert.Empty(Audit(w, f.ChildId, f.AsgB));            // only this assignment's trail
    }

    [Fact]
    public void Removing_an_assignment_deletes_the_row_but_its_audit_entry_survives()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        using (var db = w.NewContext())
            Assert.True(Result.Of(Ctl(db).DeleteAssignment(f.AsgA, w.DoctorId, "UnassignOnly").GetAwaiter().GetResult()).IsSuccess);
        using var chk = w.NewContext();
        Assert.False(chk.PAAssignments.Any(x => x.Id == f.AsgA));
        Assert.Empty(chk.PAAssignmentSchedules.Where(l => l.AssignmentId == f.AsgA));     // pins go with it
        Assert.True(chk.PAAssignments.Any(x => x.Id == f.AsgB));
        Assert.Equal(1, chk.PAAssignmentSchedules.Count(l => l.AssignmentId == f.AsgB));
        var e = Assert.Single(Audit(w, f.ChildId, f.AsgA));
        Assert.Equal("Removed", e.Action);
    }

    // ---------------- Read model: stage, age, history, manager stripping (Q7)

    private static long AddManager(StockWorld w, bool canView)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "34" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "MANAGER", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var m = new Manager { Name = "M", DoctorId = w.DoctorId, UserId = u.Id };
        db.Manager.Add(m); db.SaveChanges();
        db.ManagerPermissions.Add(new ManagerPermission { ManagerId = m.Id, ViewPaAssignmentStatus = canView });
        db.ManagerAccess.Add(new ManagerAccess { ManagerId = m.Id, ClinicId = w.ClinicA });
        db.SaveChanges();
        return m.Id;
    }

    private static JObject RowFor(IActionResult r, long asgId)
    {
        var ok = Assert.IsType<OkObjectResult>(r);
        var arr = (JArray)JObject.FromObject(ok.Value!)["ResponseData"]!;
        return (JObject)arr.Single(x => x.Value<long>("AssignmentId") == asgId);
    }

    [Fact]
    public void Doctor_list_reports_the_real_stage_and_age_and_the_cash_amount()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true);
        using (var db = w.NewContext())
        {
            foreach (var id in new[] { f.A1, f.A2 }) db.Schedules.Single(x => x.Id == id).IsPaymentCollected = true;
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        var rowA = RowFor(Ctl(db2).GetForDoctor(w.DoctorId).GetAwaiter().GetResult(), f.AsgA);
        Assert.Equal(4, rowA.Value<int>("Stage"));
        Assert.Equal("Paid", rowA.Value<string>("StageLabel"));
        Assert.Equal(3, rowA.Value<int>("StageAgeDays"));          // invoice was submitted 3 days ago
        Assert.Equal(8000m, rowA.Value<decimal>("CashAmount"));
        var rowB = RowFor(Ctl(db2).GetForDoctor(w.DoctorId).GetAwaiter().GetResult(), f.AsgB);
        Assert.Equal(1, rowB.Value<int>("Stage"));
    }

    [Fact]
    public void Manager_list_never_carries_invoice_payment_or_cash_data()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true);
        using (var db = w.NewContext())
        {
            foreach (var id in new[] { f.A1, f.A2 }) db.Schedules.Single(x => x.Id == id).IsPaymentCollected = true;
            db.SaveChanges();
        }
        var managerId = AddManager(w, canView: true);
        using var db2 = w.NewContext();
        var raw = Ctl(db2).GetForDoctor(w.DoctorId, requestingManagerId: managerId).GetAwaiter().GetResult();
        var row = RowFor(raw, f.AsgA);
        Assert.Equal(2, row.Value<int>("Stage"));                  // collapsed from Paid to Given
        Assert.Equal("Given", row.Value<string>("StageLabel"));
        Assert.Equal(JTokenType.Null, row["CashAmount"]!.Type);
        var json = JObject.FromObject(((OkObjectResult)raw).Value!).ToString();
        Assert.DoesNotContain("8000", json);                       // the amount is nowhere in the payload
        Assert.DoesNotContain("IsPaymentCollected", json);
        Assert.DoesNotContain("Paid", json);
    }

    [Fact]
    public void Manager_without_the_view_permission_gets_nothing()
    {
        using var w = new StockWorld();
        var f = Seed(w);
        var managerId = AddManager(w, canView: false);
        using var db = w.NewContext();
        var r = Result.Of(Ctl(db).GetForDoctor(w.DoctorId, requestingManagerId: managerId).GetAwaiter().GetResult());
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public void History_merges_log_entries_with_given_and_invoice_events_and_follows_a_reassign_chain()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true);
        Assert.True(Result.Of(Reassign(w, f, f.AsgA, newPa: 19, reason: "PA unavailable")).IsSuccess);
        long newId;
        using (var db = w.NewContext()) newId = db.PAAssignments.Single(x => x.ReassignedFromAssignmentId == f.AsgA).Id;

        using var db2 = w.NewContext();
        var ok = Assert.IsType<OkObjectResult>(Ctl(db2).GetHistory(newId).GetAwaiter().GetResult());
        var arr = (JArray)JObject.FromObject(ok.Value!)["ResponseData"]!;
        var actions = arr.Select(x => x.Value<string>("Action")).ToList();
        Assert.Contains("Reassigned", actions);
        Assert.Contains("Given", actions);
        Assert.Contains("Invoiced", actions);
        Assert.Equal("PA unavailable", arr.Single(x => x.Value<string>("Action") == "Reassigned").Value<string>("Reason"));
        Assert.Equal("log", arr.Single(x => x.Value<string>("Action") == "Reassigned").Value<string>("Source"));
        Assert.Equal("derived", arr.First(x => x.Value<string>("Action") == "Given").Value<string>("Source"));
    }

    [Fact]
    public void History_for_a_manager_leaves_out_invoice_and_cash_events()
    {
        using var w = new StockWorld();
        var f = Seed(w); GiveGroupA(w, f, invoice: true, confirmedInvoice: true);
        var managerId = AddManager(w, canView: true);
        using var db = w.NewContext();
        var ok = Assert.IsType<OkObjectResult>(Ctl(db).GetHistory(f.AsgA, managerId).GetAwaiter().GetResult());
        var actions = ((JArray)JObject.FromObject(ok.Value!)["ResponseData"]!).Select(x => x.Value<string>("Action")).ToList();
        Assert.Contains("Given", actions);
        Assert.DoesNotContain("Invoiced", actions);
        Assert.DoesNotContain("Confirmed", actions);
    }
}
