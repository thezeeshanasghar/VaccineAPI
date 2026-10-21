using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// A cash handover is confirmed only when the cash the doctor counted equals what the PA declared
/// (PA_TRACKING_MERGE_SPEC.md Q3). A mismatch is refused and leaves the handover pending so it can
/// be sent back; the doctor must also own the handover.
/// </summary>
public class PaCashHandoverCountedTests
{
    private static long SeedHandover(StockWorld w, decimal amount)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Asif", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var h = new PaCashHandover { PaId = pa.Id, ClinicId = w.ClinicA, DoctorId = w.DoctorId, Amount = amount };
        db.PaCashHandovers.Add(h); db.SaveChanges();
        return h.Id;
    }

    private static (bool ok, string msg) Confirm(StockWorld w, long id, long doctorId, decimal? counted)
    {
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaCashHandoverController(db).Confirm(id, doctorId, counted);
        var j = Newtonsoft.Json.Linq.JObject.FromObject(res.Value!);
        return ((bool)j["IsSuccess"]!, (string)j["Message"]!);
    }

    private static string Status(StockWorld w, long id)
    {
        using var db = w.NewContext();
        return db.PaCashHandovers.Single(h => h.Id == id).Status;
    }

    [Fact]
    public void Matching_count_confirms()
    {
        using var w = new StockWorld();
        var id = SeedHandover(w, 4500);
        Assert.True(Confirm(w, id, w.DoctorId, 4500).ok);
        Assert.Equal("Confirmed", Status(w, id));
    }

    [Fact]
    public void Mismatched_count_is_refused_and_stays_pending()
    {
        using var w = new StockWorld();
        var id = SeedHandover(w, 4500);
        var r = Confirm(w, id, w.DoctorId, 4400);
        Assert.False(r.ok);
        Assert.Contains("does not match", r.msg);
        Assert.Equal("Pending", Status(w, id));
    }

    [Fact]
    public void Another_doctor_cannot_confirm()
    {
        using var w = new StockWorld();
        var id = SeedHandover(w, 4500);
        Assert.False(Confirm(w, id, w.DoctorId + 999, 4500).ok);
        Assert.Equal("Pending", Status(w, id));
    }
}
