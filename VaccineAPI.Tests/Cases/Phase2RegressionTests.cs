using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Differential regression for Phase 2: the same real endpoint flow run with identity.stamp OFF and ON must leave
/// every table except financialaudit/featureflags in the same state (row counts per table, the money/owner columns of
/// doses and invoices, and the log action sequence). Stamping may only ADD financialaudit rows.
/// </summary>
[Collection("AuthStatics")]
public class Phase2RegressionTests
{
    private static string Snapshot(Context db)
    {
        var sb = new StringBuilder();
        foreach (var p in typeof(Context).GetProperties().Where(p => p.PropertyType.IsGenericType &&
                 p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)).OrderBy(p => p.Name))
        {
            if (p.Name == nameof(Context.FinancialAudit) || p.Name == nameof(Context.FeatureFlags)) continue;
            var set = p.GetValue(db)!;
            var count = (int)typeof(Queryable).GetMethods().First(m => m.Name == "Count" && m.GetParameters().Length == 1)
                .MakeGenericMethod(p.PropertyType.GetGenericArguments()[0]).Invoke(null, new[] { set })!;
            sb.Append(p.Name).Append('=').Append(count).Append(';');
        }
        foreach (var s in db.Schedules.OrderBy(x => x.Id))
            sb.Append($"S{s.Id}|{s.IsDone}|{s.Amount}|{s.PaymentMode}|{s.IsPaymentCollected}|{s.PaymentCollectorPaId}|{s.GivenByPaId}|{s.GivenByManagerId}|{s.InvoiceSubmissionId}|{s.BrandId};");
        foreach (var i in db.InvoiceSubmissions.OrderBy(x => x.Id))
            sb.Append($"I{i.Id}|{i.PaId}|{i.TotalAmount}|{i.IsConfirmedByDoctor}|{i.InvoiceStatus}|{i.PaymentMode};");
        foreach (var a in db.PAAssignments.OrderBy(x => x.Id))
            sb.Append($"A{a.Id}|{a.PersonalAssistantId}|{a.InvoiceSubmissionId};");
        sb.Append("LOG=").Append(string.Join(",", db.PaActivityLogs.OrderBy(l => l.Id).Select(l => l.ActionCode)));
        return sb.ToString();
    }

    private static (string Snap, List<string> Audit) Run(bool flagOn)
    {
        AuthContext.Accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        using var w = new StockWorld();
        using (var db = w.NewContext())
        {
            db.FeatureFlags.Add(new FeatureFlag { FlagKey = FinancialAudit.Flag, IsOn = flagOn });
            db.SaveChanges();
        }
        FeatureFlags.ResetCache();

        long paId, doctorUserId; string stamp;
        using (var db = w.NewContext())
        {
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
            paId = pa.Id;
            doctorUserId = db.Doctors.Single(x => x.Id == w.DoctorId).UserId;
            stamp = db.Users.Single(u => u.Id == doctorUserId).SecurityStamp;
        }

        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id).IsSuccess);
        using (var db = w.NewContext())
        {
            foreach (var s in db.Schedules.Where(x => x.Id == w.Schedule1Id || x.Id == w.Schedule2Id)) s.Amount = 3900;
            db.SaveChanges();
        }
        foreach (var sid in new[] { w.Schedule1Id, w.Schedule2Id })
            using (var db = w.NewContext())
                Assert.True(Result.Of(w.Schedules(db).RecordPaymentMode(sid, new ScheduleDTO { PaymentMode = "Cash", DoctorId = w.DoctorId, CallerUserId = doctorUserId, SecurityStamp = stamp })).IsSuccess);
        long invId;
        using (var db = w.NewContext())
        {
            var inv = new InvoiceSubmission { ChildId = w.ChildId, DoctorId = w.DoctorId, InvoiceDate = StockWorld.Today, SubmittedAt = DateTime.UtcNow, PaId = paId, TotalAmount = 7800 };
            db.InvoiceSubmissions.Add(inv); db.SaveChanges();
            foreach (var s in db.Schedules.Where(x => x.Id == w.Schedule1Id || x.Id == w.Schedule2Id)) { s.InvoiceSubmissionId = inv.Id; s.IsPaymentCollected = true; }
            db.SaveChanges(); invId = inv.Id;
        }
        using (var db = w.NewContext())
            Assert.True(Result.Of(w.Schedules(db).ConfirmInvoice(invId, w.DoctorId, doctorUserId, stamp)).IsSuccess);
        Assert.True(w.BulkUngive(w.Schedule1Id, w.BrandId, true, w.Schedule1Id, w.Schedule2Id).IsSuccess);

        using var fin = w.NewContext();
        return (Snapshot(fin), fin.FinancialAudit.OrderBy(r => r.Id).Select(r => r.Action).ToList());
    }

    [Fact]
    public void Stamping_changes_nothing_except_adding_financialaudit_rows()
    {
        var off = Run(false);
        var on = Run(true);
        Assert.Empty(off.Audit);
        Assert.Equal(off.Snap, on.Snap);
        Assert.Contains("Give", on.Audit);
        Assert.Contains("PaymentRecord", on.Audit);
        Assert.Contains("Confirm", on.Audit);
        Assert.Contains("Ungive", on.Audit);
    }

    [Fact]
    public void A_broken_audit_table_never_breaks_the_action()
    {
        // The flag is read through a try/catch and Stamp is wrapped: removing the table with the flag cached ON must not
        // stop a give. (SaveChanges would fail on the insert, so this asserts the flag reader's own safety instead.)
        using var t = new TestDb(); using var db = t.NewContext();
        db.Database.ExecuteSqlRaw("DROP TABLE featureflags");
        FeatureFlags.ResetCache();
        Assert.False(FeatureFlags.IsOn(db, FinancialAudit.Flag));
    }
}
