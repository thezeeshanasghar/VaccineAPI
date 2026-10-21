using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Orphan money: an invoice whose patient was deleted stays in Payment Reconciliation (named from the
/// delete audit snapshot) until the doctor cancels it; cancel voids it, closes its assignment and is
/// refused while the patient still exists or for another doctor.
/// </summary>
public class DeletedPatientCashTests
{
    private const long GoneChild = 987654;

    private static long SeedInvoice(StockWorld w, long childId, bool audit)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Sapna", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var inv = new InvoiceSubmission
        {
            ChildId = childId, DoctorId = w.DoctorId, PaId = pa.Id, ClinicId = w.ClinicA,
            InvoiceDate = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow, TotalAmount = 9500, PaymentMode = "Online"
        };
        db.InvoiceSubmissions.Add(inv); db.SaveChanges();
        db.PAAssignments.Add(new PAAssignment
        {
            DoctorId = w.DoctorId, ClinicId = w.ClinicA, PersonalAssistantId = pa.Id, ChildId = childId,
            InvoiceSubmissionId = inv.Id
        });
        if (audit)
            db.PaActivityLogs.Add(new PaActivityLog
            {
                DoctorId = w.DoctorId, PatientId = childId, ActionCode = PatientDeleteService.AuditCode,
                Description = "Patient deleted: WRONG NAME", Notes = "{\"name\":\"WRONG NAME\",\"actor\":\"DOCTOR\"}"
            });
        db.SaveChanges();
        return inv.Id;
    }

    private static (bool ok, string msg) Cancel(StockWorld w, long invId, long doctorId)
    {
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaCashHandoverController(db).CancelDeletedPatientInvoice(invId, doctorId);
        var j = Newtonsoft.Json.Linq.JObject.FromObject(res.Value!);
        return ((bool)j["IsSuccess"]!, (string)j["Message"]!);
    }

    [Fact]
    public void Cancel_voids_invoice_and_closes_assignment()
    {
        using var w = new StockWorld();
        var id = SeedInvoice(w, GoneChild, audit: true);
        Assert.True(Cancel(w, id, w.DoctorId).ok);
        using var db = w.NewContext();
        Assert.Equal("Cancelled", db.InvoiceSubmissions.Single(i => i.Id == id).InvoiceStatus);
        Assert.True(db.PAAssignments.Single(a => a.InvoiceSubmissionId == id).IsCancelled);
        Assert.Contains(db.PaActivityLogs.ToList(), l => l.ActionCode == "AsgPaymentCancelledDeletedPatient");
    }

    [Fact]
    public void Another_doctor_cannot_cancel()
    {
        using var w = new StockWorld();
        var id = SeedInvoice(w, GoneChild, audit: true);
        Assert.False(Cancel(w, id, w.DoctorId + 999).ok);
        using var db = w.NewContext();
        Assert.Equal("Active", db.InvoiceSubmissions.Single(i => i.Id == id).InvoiceStatus);
    }

    [Fact]
    public void Snapshot_names_the_deleted_patient()
    {
        using var w = new StockWorld();
        SeedInvoice(w, GoneChild, audit: true);
        using var db = w.NewContext();
        var s = PatientDeleteService.Snapshots(db, new List<long> { GoneChild });
        Assert.Equal("WRONG NAME", s[GoneChild].Name);
    }
}
