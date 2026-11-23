using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Controllers;
using VaccineAPI.Tests.Infrastructure;
using Xunit;
using static VaccineAPI.Tests.Infrastructure.Kit;

namespace VaccineAPI.Tests.Cases;

// HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md "BACK-DATE WITH BRAND PERMISSION".
// PA with the permission OFF: brand give must be dated today; date of an already-given dose cannot change.
public class BackdateWithBrandTests
{
    static readonly DateTime Past = StockWorld.Today.AddDays(-3);

    static long NewPa(StockWorld w, bool backdate)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "0312" + Guid.NewGuid().ToString("N").Substring(0, 7), Password = "x", UserType = "PA" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Atif", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        db.PaPermissions.Add(new PaPermission { PaId = pa.Id, GiveVaccine = true, BulkGiveVaccines = true, BackdateWithBrand = backdate });
        db.SaveChanges();
        return pa.Id;
    }

    static (long, string?) Caller(Context db, long paId)
    {
        var uid = db.PersonalAssistant.AsNoTracking().Single(p => p.Id == paId).UserId;
        return (uid, db.Users.AsNoTracking().Single(u => u.Id == uid).SecurityStamp);
    }

    static Response<ScheduleDTO> PaGive(StockWorld w, long paId, long scheduleId, long? brand, DateTime date, bool? reRecord = null)
    {
        using var db = w.NewContext();
        var (uid, stamp) = Caller(db, paId);
        return w.Schedules(db).Update(new ScheduleDTO
        {
            CallerUserId = uid, SecurityStamp = stamp,
            Id = scheduleId, ChildId = w.ChildId, DoseId = (int)(scheduleId == w.Schedule1Id ? w.Dose1Id : w.Dose2Id),
            DoctorId = w.DoctorId, PaId = paId, IsDone = true, GivenDate = date, BrandId = brand, Date = StockWorld.Today,
            ConfirmUnbatchedGive = true, ReRecordHistorical = reRecord, IsPAApprove = true
        });
    }

    static Schedule Sch(StockWorld w, long id) { using var db = w.NewContext(); return db.Schedules.AsNoTracking().Single(s => s.Id == id); }

    [Fact]
    public void Off_BrandGive_BackDated_IsRefused_NothingGiven()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        var r = PaGive(w, pa, w.Schedule1Id, w.BrandId, Past, reRecord: true);
        Assert.False(r.IsSuccess);
        Assert.Contains("Back-dating with a brand", r.Message);
        Assert.False(Sch(w, w.Schedule1Id).IsDone);
        Assert.Equal(3, w.BaQty(w.ClinicA));
    }

    [Fact]
    public void Off_BrandGive_BackDated_Refused_EvenWithJustRecordingAnswer_And_WithFromStockAnswer()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        Assert.False(PaGive(w, pa, w.Schedule1Id, w.BrandId, Past, reRecord: true).IsSuccess);
        Assert.False(PaGive(w, pa, w.Schedule1Id, w.BrandId, Past, reRecord: false).IsSuccess);
        Assert.False(Sch(w, w.Schedule1Id).IsDone);
    }

    [Fact]
    public void Off_BrandGive_Today_Succeeds_AndDeductsStock()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        var r = PaGive(w, pa, w.Schedule1Id, w.BrandId, StockWorld.Today);
        Assert.True(r.IsSuccess, r.Message);
        Assert.True(Sch(w, w.Schedule1Id).IsDone);
        Assert.Equal(2, w.BaQty(w.ClinicA));
    }

    [Fact]
    public void Off_OhfGive_BackDated_StillAllowed()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        var r = PaGive(w, pa, w.Schedule1Id, null, Past);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Equal(Past, Sch(w, w.Schedule1Id).GivenDate!.Value.Date);
        Assert.Equal(3, w.BaQty(w.ClinicA));
    }

    [Fact]
    public void Off_OhfGivenPast_ThenResubmitWithBrandAndToday_IsRefused_DateCannotChange()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        Assert.True(PaGive(w, pa, w.Schedule1Id, null, Past).IsSuccess);
        var r = PaGive(w, pa, w.Schedule1Id, w.BrandId, StockWorld.Today);
        Assert.False(r.IsSuccess);
        Assert.Contains("cannot change the date", r.Message);
        Assert.Equal(Past, Sch(w, w.Schedule1Id).GivenDate!.Value.Date);
        Assert.Null(Sch(w, w.Schedule1Id).BrandId);
        Assert.Equal(3, w.BaQty(w.ClinicA));
    }

    [Fact]
    public void Off_OhfGivenPast_ThenResubmitWithBrandAndSamePastDate_IsRefused_BrandBackDated()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        Assert.True(PaGive(w, pa, w.Schedule1Id, null, Past).IsSuccess);
        var r = PaGive(w, pa, w.Schedule1Id, w.BrandId, Past, reRecord: false);
        Assert.False(r.IsSuccess);
        Assert.Contains("Back-dating with a brand", r.Message);
        Assert.Equal(3, w.BaQty(w.ClinicA));
    }

    [Fact]
    public void Off_OhfGivenPast_ResubmitSameDateNoBrand_Allowed()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        Assert.True(PaGive(w, pa, w.Schedule1Id, null, Past).IsSuccess);
        Assert.True(PaGive(w, pa, w.Schedule1Id, null, Past).IsSuccess);
    }

    [Fact]
    public void On_BrandGive_BackDated_Allowed()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, true);
        var r = PaGive(w, pa, w.Schedule1Id, w.BrandId, Past, reRecord: true);
        Assert.True(r.IsSuccess, r.Message);
        Assert.True(Sch(w, w.Schedule1Id).IsDone);
    }

    [Fact]
    public void NoPermissionRow_TreatedAsOff()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        using (var db = w.NewContext()) { db.PaPermissions.RemoveRange(db.PaPermissions.Where(p => p.PaId == pa)); db.SaveChanges(); }
        // GiveVaccine gate may reject first without a row; either way a back-dated brand dose must not be given.
        PaGive(w, pa, w.Schedule1Id, w.BrandId, Past, reRecord: true);
        Assert.False(Sch(w, w.Schedule1Id).IsDone);
    }

    [Fact]
    public void Doctor_BackDatedBrandGive_Unaffected()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var r = w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, w.BrandId, Past, reRecord: true);
        Assert.True(r.IsSuccess, r.Message);
    }

    [Fact]
    public void Off_BulkGive_BrandBackDated_Refused_NothingGiven()
    {
        using var w = new StockWorld(); w.PostBill(w.ClinicA, 3, 10);
        var pa = NewPa(w, false);
        using var db = w.NewContext();
        var (uid, stamp) = Caller(db, pa);
        var dto = new ScheduleDTO { CallerUserId = uid, SecurityStamp = stamp, Id = w.Schedule1Id, DoctorId = w.DoctorId, PaId = pa, IsDone = true, GivenDate = Past, Date = StockWorld.Today, ConfirmUnbatchedGive = true, ReRecordHistorical = true, IsPAApprove = true };
        dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)w.Schedule1Id, BrandId = w.BrandId });
        var r = w.Schedules(db).UpdateBulkInjection(dto);
        Assert.False(r.IsSuccess);
        Assert.Contains("Back-dating with a brand", r.Message);
        Assert.False(Sch(w, w.Schedule1Id).IsDone);
        Assert.False(Sch(w, w.Schedule2Id).IsDone);
    }

    [Fact]
    public void Upsert_PersistsFlag_OnInsertAndUpdate()
    {
        using var w = new StockWorld(); using var db = w.NewContext();
        var u = new User { MobileNumber = "0313" + Guid.NewGuid().ToString("N").Substring(0, 7), Password = "x", UserType = "PA" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { DoctorId = w.DoctorId, UserId = u.Id };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var c = new PaPermissionController(db);
        c.Upsert(pa.Id, new PaPermission { BackdateWithBrand = true });
        Assert.True(db.PaPermissions.AsNoTracking().First(x => x.PaId == pa.Id).BackdateWithBrand);
        c.Upsert(pa.Id, new PaPermission { BackdateWithBrand = false });
        Assert.False(db.PaPermissions.AsNoTracking().First(x => x.PaId == pa.Id).BackdateWithBrand);
    }
}
