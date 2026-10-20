using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

public class TravelQuickTests
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

    static List<TravelQuickItemDTO> Items(ActionResult<Response<List<TravelQuickItemDTO>>> r) =>
        ((Response<List<TravelQuickItemDTO>>)((OkObjectResult)r.Result!).Value!).ResponseData!;

    [Fact]
    public void Save_ThenGet_ReturnsInSavedOrder_AndReplacesPreviousList()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, true);
        var c = new TravelQuickController(db);
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long> { d[2], d[0], d[1] }).Result);
        Assert.Equal(new[] { d[2], d[0], d[1] }, Items(c.Get(doc)).Select(x => x.DoseId).ToArray());
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long> { d[4] }).Result);
        Assert.Equal(new[] { d[4] }, Items(c.Get(doc)).Select(x => x.DoseId).ToArray());
        Assert.IsType<OkObjectResult>(c.Save(doc, new List<long>()).Result);
        Assert.Empty(Items(c.Get(doc)));
    }

    [Fact]
    public void Save_Rejects_MoreThanFour_Duplicates_UnknownDose_AndNoTravelPermission()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (doc, d) = Seed(db, true);
        var c = new TravelQuickController(db);
        Assert.IsType<BadRequestObjectResult>(c.Save(doc, d.ToList()).Result);                 // 5 > 4
        Assert.IsType<BadRequestObjectResult>(c.Save(doc, new List<long> { d[0], d[0] }).Result);
        Assert.IsType<BadRequestObjectResult>(c.Save(doc, new List<long> { 999999 }).Result);
        Assert.Empty(Items(c.Get(doc)));                                                        // nothing partially saved

        var (doc2, d2) = Seed(db, false);
        Assert.IsType<BadRequestObjectResult>(c.Save(doc2, new List<long> { d2[0] }).Result);
    }

    [Fact]
    public void Get_IsEmpty_WhenDoctorLosesTravelPermission_AndListsAreIsolatedPerDoctor()
    {
        using var t = new TestDb(); using var db = t.NewContext();
        var (docA, dA) = Seed(db, true);
        var (docB, dB) = Seed(db, true);
        var c = new TravelQuickController(db);
        c.Save(docA, new List<long> { dA[0] });
        c.Save(docB, new List<long> { dB[1] });
        Assert.Equal(new[] { dA[0] }, Items(c.Get(docA)).Select(x => x.DoseId).ToArray());
        Assert.Equal(new[] { dB[1] }, Items(c.Get(docB)).Select(x => x.DoseId).ToArray());

        db.Doctors.First(x => x.Id == docA).AllowTravel = false; db.SaveChanges();
        Assert.Empty(Items(c.Get(docA)));
    }
}
