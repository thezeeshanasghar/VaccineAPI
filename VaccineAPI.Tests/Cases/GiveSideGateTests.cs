using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Controllers;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

// HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md Enforcement A: a PA give of a dose with no brand/invoice/fee/payment
// creates no assignment, no pin and no collector stamp. GivenByPaId is kept. Brand gives are unchanged.
public class GiveSideGateTests
{
    static long NewPa(StockWorld w)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "0312" + Guid.NewGuid().ToString("N").Substring(0, 7), Password = "x", UserType = "PA" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Atif", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        db.PaPermissions.Add(new PaPermission { PaId = pa.Id, GiveVaccine = true, BulkGiveVaccines = true, BackdateWithBrand = true });
        db.SaveChanges();
        return pa.Id;
    }

    static Response<ScheduleDTO> PaGive(StockWorld w, long paId, long scheduleId, long? brand, DateTime date)
    {
        using var db = w.NewContext();
        var uid = db.PersonalAssistant.AsNoTracking().Single(p => p.Id == paId).UserId;
        var stamp = db.Users.AsNoTracking().Single(u => u.Id == uid).SecurityStamp;
        return w.Schedules(db).Update(new ScheduleDTO
        {
            CallerUserId = uid, SecurityStamp = stamp,
            Id = scheduleId, ChildId = w.ChildId, DoseId = (int)(scheduleId == w.Schedule1Id ? w.Dose1Id : w.Dose2Id),
            DoctorId = w.DoctorId, PaId = paId, IsDone = true, GivenDate = date, BrandId = brand, Date = StockWorld.Today,
            ConfirmUnbatchedGive = true, ReRecordHistorical = true, IsPAApprove = true
        });
    }

    static int Assignments(StockWorld w) { using var db = w.NewContext(); return db.PAAssignments.Count(a => a.ChildId == w.ChildId); }
    static int Pins(StockWorld w) { using var db = w.NewContext(); return db.PAAssignmentSchedules.Count(); }
    static Schedule Sch(StockWorld w, long id) { using var db = w.NewContext(); return db.Schedules.AsNoTracking().Single(s => s.Id == id); }

    [Fact]
    public void Ohf_PastDated_CreatesNoAssignment_NoPin_NoCollector_KeepsGivenBy()
    {
        using var w = new StockWorld();
        var pa = NewPa(w);
        var r = PaGive(w, pa, w.Schedule1Id, null, StockWorld.Today.AddDays(-3));
        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(0, Assignments(w));
        Assert.Equal(0, Pins(w));
        var s = Sch(w, w.Schedule1Id);
        Assert.True(s.IsDone);
        Assert.Null(s.PaymentCollectorPaId);
        Assert.Equal(pa, s.GivenByPaId);
    }

    [Fact]
    public void Ohf_CurrentDated_AlsoCreatesNoAssignment()
    {
        using var w = new StockWorld();
        var pa = NewPa(w);
        Assert.True(PaGive(w, pa, w.Schedule1Id, null, StockWorld.Today).IsSuccess);
        Assert.Equal(0, Assignments(w));
    }

    [Fact]
    public void BrandGive_StillCreatesAssignment_PinAndCollector()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w);
        var r = PaGive(w, pa, w.Schedule1Id, w.BrandId, StockWorld.Today);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(1, Assignments(w));
        Assert.Equal(1, Pins(w));
        Assert.Equal(pa, Sch(w, w.Schedule1Id).PaymentCollectorPaId);
    }

    [Fact]
    public void Ohf_TwoGroups_NoAwaitingRows()
    {
        using var w = new StockWorld();
        var pa = NewPa(w);
        Assert.True(PaGive(w, pa, w.Schedule1Id, null, StockWorld.Today.AddDays(-3)).IsSuccess);
        Assert.True(PaGive(w, pa, w.Schedule2Id, null, StockWorld.Today.AddDays(-3)).IsSuccess);
        Assert.Equal(0, Assignments(w));
    }
}
