using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

// HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md Point C: a money-free PA give skips assignment; when an invoice later
// covers the dose, the PA who gave it becomes responsible (assignment + pin + collector + invoice.PaId).
[Collection("AuthStatics")]
public class PointCInvoiceLaterTests
{
    static long NewPa(StockWorld w)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "0312" + Guid.NewGuid().ToString("N").Substring(0, 7), Password = "x", UserType = "PA" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Atif", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        db.PaPermissions.Add(new PaPermission { PaId = pa.Id, GiveVaccine = true, BulkGiveVaccines = true });
        db.SaveChanges();
        return pa.Id;
    }

    static void PaGiveOhf(StockWorld w, long paId, long scheduleId)
    {
        using var db = w.NewContext();
        var uid = db.PersonalAssistant.AsNoTracking().Single(p => p.Id == paId).UserId;
        var stamp = db.Users.AsNoTracking().Single(u => u.Id == uid).SecurityStamp;
        var r = w.Schedules(db).Update(new ScheduleDTO
        {
            CallerUserId = uid, SecurityStamp = stamp,
            Id = scheduleId, ChildId = w.ChildId, DoseId = (int)w.Dose1Id, DoctorId = w.DoctorId, PaId = paId,
            IsDone = true, GivenDate = StockWorld.Today.AddDays(-2), BrandId = null, Date = StockWorld.Today,
            ConfirmUnbatchedGive = true, ReRecordHistorical = true, IsPAApprove = true
        });
        Assert.True(r.IsSuccess, r.Message);
    }

    static Response<object> Invoice(StockWorld w)
    {
        AuthContext.Accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        using var db = w.NewContext();
        return w.Schedules(db).updateInvoice(new BulkInvoiceSubmitDTO
        {
            ChildId = w.ChildId, DoctorId = w.DoctorId, ClinicId = w.ClinicA, InvoiceDate = DateTime.UtcNow.Date, ConsultationFee = 0,
            Schedules = new List<ScheduleAmountItem> { new() { Id = w.Schedule1Id, Amount = 4517 } }
        });
    }

    [Fact]
    public void InvoiceLater_ForPaGivenOhfDose_MakesTheGivingPaOwner()
    {
        using var w = new StockWorld();
        var pa = NewPa(w);
        PaGiveOhf(w, pa, w.Schedule1Id);
        using (var db = w.NewContext()) Assert.Empty(db.PAAssignments.Where(a => a.ChildId == w.ChildId));

        var r = Invoice(w);
        Assert.True(r.IsSuccess, r.Message);

        using var chk = w.NewContext();
        var a = Assert.Single(chk.PAAssignments.Where(x => x.ChildId == w.ChildId));
        Assert.Equal(pa, a.PersonalAssistantId);
        Assert.Single(chk.PAAssignmentSchedules.Where(l => l.AssignmentId == a.Id && l.ScheduleId == w.Schedule1Id));
        var s = chk.Schedules.AsNoTracking().Single(x => x.Id == w.Schedule1Id);
        Assert.Equal(pa, s.PaymentCollectorPaId);
        Assert.Equal(pa, s.GivenByPaId);
        var inv = chk.InvoiceSubmissions.Single(i => i.Id == s.InvoiceSubmissionId);
        Assert.Equal(pa, inv.PaId);
        Assert.False(PaOwnership.SkipsPaOwnership(s));
    }

    [Fact]
    public void InvoiceLater_ForDoseWithNoGivingPa_StaysUnowned()
    {
        using var w = new StockWorld();
        var pa = NewPa(w);
        PaGiveOhf(w, pa, w.Schedule1Id);
        using (var db = w.NewContext())
        {
            var s = db.Schedules.Single(x => x.Id == w.Schedule1Id);
            s.GivenByPaId = null; db.SaveChanges();   // a doctor/manager gave it
        }
        Assert.True(Invoice(w).IsSuccess);
        using var chk = w.NewContext();
        Assert.Empty(chk.PAAssignments.Where(x => x.ChildId == w.ChildId));
        Assert.Null(chk.Schedules.AsNoTracking().Single(x => x.Id == w.Schedule1Id).PaymentCollectorPaId);
    }
}
