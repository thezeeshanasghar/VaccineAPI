using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

// Custom (Customize) quick select. It must stay fully independent of Travel quick select:
// own table, own controller, own PA flag, no dependency on Doctor.AllowTravel.
public class CustomQuickTests
{
    static (long doctorId, List<long> doses) Seed(Context db, bool allowTravel)
    {
        var u = new User { MobileNumber = "0300" + Guid.NewGuid().ToString("N").Substring(0, 7), Password = "x", UserType = "DOCTOR" };
        db.Users.Add(u); db.SaveChanges();
        var doc = new Doctor { FirstName = "T", UserId = u.Id, AllowTravel = allowTravel };
        db.Doctors.Add(doc);
        var v = new Vaccine { Name = "V", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var doses = new List<long>();
        for (int i = 1; i <= 5; i++)
        {
            var d = new Dose { Name = "D" + i, VaccineId = v.Id, DoseOrder = i, MinAge = 0 };
            db.Doses.Add(d); db.SaveChanges(); doses.Add(d.Id);
        }
        return (doc.Id, doses);
    }

    static long[] Custom(CustomQuickController c, long doc) =>
        ((Response<List<CustomQuickItemDTO>>)((OkObjectResult)c.Get(doc).Result!).Value!).ResponseData!.Select(x => x.DoseId).ToArray();

    static long[] Travel(TravelQuickController c, long doc) =>
        ((Response<List<TravelQuickItemDTO>>)((OkObjectResult)c.Get(doc).Result!).Value!).ResponseData!.Select(x => x.DoseId).ToArray();

    [Fact]
    public void Save_ThenGet_ReturnsInSavedOrder_AndReplacesPreviousList()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, false);
        var c = new CustomQuickController(db);
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long> { d[2], d[0], d[1] }).Result);
        Assert.Equal(new[] { d[2], d[0], d[1] }, Custom(c, doc));
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long> { d[4] }).Result);
        Assert.Equal(new[] { d[4] }, Custom(c, doc));
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long>()).Result);
        Assert.Empty(Custom(c, doc));
    }

    [Fact]
    public void Save_Rejects_MoreThanFour_Duplicates_UnknownDose_AndUnknownDoctor_WithoutPartialSave()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, false);
        var c = new CustomQuickController(db);
        c.Save(doc, new List<long> { d[0] });
        Assert.IsType<BadRequestObjectResult>(c.Save(doc, d.ToList()).Result);                 // 5 > 4
        Assert.IsType<BadRequestObjectResult>(c.Save(doc, new List<long> { d[1], d[1] }).Result);
        Assert.IsType<BadRequestObjectResult>(c.Save(doc, new List<long> { 999999 }).Result);
        Assert.IsType<NotFoundObjectResult>(c.Save(987654, new List<long> { d[0] }).Result);
        Assert.Equal(new[] { d[0] }, Custom(c, doc));                                           // earlier list intact
        Assert.IsType<NotFoundObjectResult>(c.Get(987654).Result);
    }

    [Fact]
    public void Works_ForDoctorWithoutTravelPermission_AndSurvivesTravelPermissionChanges()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, false);                                                          // AllowTravel = false
        var c = new CustomQuickController(db);
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long> { d[0], d[1] }).Result);
        Assert.Equal(new[] { d[0], d[1] }, Custom(c, doc));

        db.Doctors.First(x => x.Id == doc).AllowTravel = true; db.SaveChanges();
        Assert.Equal(new[] { d[0], d[1] }, Custom(c, doc));
        db.Doctors.First(x => x.Id == doc).AllowTravel = false; db.SaveChanges();
        Assert.Equal(new[] { d[0], d[1] }, Custom(c, doc));                                     // revoking Travel never hides Custom
    }

    [Fact]
    public void CustomAndTravelLists_AreIndependent_InBothDirections_AndMayShareDoses()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, true);
        var custom = new CustomQuickController(db);
        var travel = new TravelQuickController(db);

        travel.Save(doc, new List<long> { d[0], d[1] });
        custom.Save(doc, new List<long> { d[1], d[2] });                                         // d[1] in both lists
        Assert.Equal(new[] { d[0], d[1] }, Travel(travel, doc));
        Assert.Equal(new[] { d[1], d[2] }, Custom(custom, doc));

        custom.Save(doc, new List<long> { d[4] });                                               // editing Custom leaves Travel alone
        Assert.Equal(new[] { d[0], d[1] }, Travel(travel, doc));
        travel.Save(doc, new List<long>());                                                      // clearing Travel leaves Custom alone
        Assert.Equal(new[] { d[4] }, Custom(custom, doc));
        travel.Save(doc, new List<long> { d[3] });
        custom.Save(doc, new List<long>());
        Assert.Equal(new[] { d[3] }, Travel(travel, doc));
    }

    [Fact]
    public void Lists_AreIsolatedPerDoctor()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (a, dA) = Seed(db, false);
        var (b, dB) = Seed(db, false);
        var c = new CustomQuickController(db);
        c.Save(a, new List<long> { dA[0] });
        c.Save(b, new List<long> { dB[1] });
        Assert.Equal(new[] { dA[0] }, Custom(c, a));
        Assert.Equal(new[] { dB[1] }, Custom(c, b));
    }

    [Fact]
    public void TravelController_StillGatedByAllowTravel_AndIgnoresCustomRows()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, false);
        var travel = new TravelQuickController(db);
        new CustomQuickController(db).Save(doc, new List<long> { d[0] });
        Assert.IsType<BadRequestObjectResult>(travel.Save(doc, new List<long> { d[0] }).Result); // Travel gate unchanged
        Assert.Empty(Travel(travel, doc));                                                       // custom rows never leak into Travel
    }

    [Fact]
    public void PaPermission_UseCustomQuickSelect_IsSeparateFlag_OnInsertAndUpdate()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, _) = Seed(db, true);
        var pu = new User { MobileNumber = "0311" + Guid.NewGuid().ToString("N").Substring(0, 7), Password = "x", UserType = "PA" };
        db.Users.Add(pu); db.SaveChanges();
        var pa = new PersonalAssistant { DoctorId = doc, UserId = pu.Id };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var c = new PaPermissionController(db);

        c.Upsert(pa.Id, new PaPermission { UseTravelQuickSelect = true, UseCustomQuickSelect = false });
        var p = db.PaPermissions.AsNoTracking().First(x => x.PaId == pa.Id);
        Assert.True(p.UseTravelQuickSelect); Assert.False(p.UseCustomQuickSelect);

        c.Upsert(pa.Id, new PaPermission { UseTravelQuickSelect = false, UseCustomQuickSelect = true });
        p = db.PaPermissions.AsNoTracking().First(x => x.PaId == pa.Id);
        Assert.False(p.UseTravelQuickSelect); Assert.True(p.UseCustomQuickSelect);

        c.Upsert(pa.Id, new PaPermission());                                                     // defaults: both off
        p = db.PaPermissions.AsNoTracking().First(x => x.PaId == pa.Id);
        Assert.False(p.UseTravelQuickSelect); Assert.False(p.UseCustomQuickSelect);
    }
}
