using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// GROWTH_FLAG_SPEC.md: a PA must save at least one of weight/height/OFC (OFC only under 2 years) or tick
/// Not measured; the doctor's Pending Confirmation rows carry a display-only growth flag for PA-given
/// regular doses on/after Growth:FlagFromDate. The flag never counts doctor-given doses or past doses.
/// </summary>
public class GrowthFlagTests
{
    private static readonly DateTime Dob = new DateTime(2024, 6, 1);

    [Fact]
    public void Any_one_parameter_counts_as_growth()
    {
        var given = new DateTime(2025, 6, 1);
        Assert.True(GrowthRules.HasGrowth(9.5f, null, null, Dob, given));
        Assert.True(GrowthRules.HasGrowth(null, 80f, null, Dob, given));
        Assert.True(GrowthRules.HasGrowth(null, null, 45f, Dob, given));
        Assert.False(GrowthRules.HasGrowth(null, null, null, Dob, given));
        Assert.False(GrowthRules.HasGrowth(0f, 0f, 0f, Dob, given));
    }

    [Fact]
    public void Ofc_is_ignored_from_the_second_birthday()
    {
        var before = new DateTime(2026, 5, 31);
        var on = new DateTime(2026, 6, 1);
        Assert.True(GrowthRules.OfcAllowed(Dob, before));
        Assert.False(GrowthRules.OfcAllowed(Dob, on));
        Assert.True(GrowthRules.HasGrowth(null, null, 48f, Dob, before));
        Assert.False(GrowthRules.HasGrowth(null, null, 48f, Dob, on));
        Assert.True(GrowthRules.HasGrowth(12f, null, 48f, Dob, on));
    }

    [Fact]
    public void Flag_date_empty_or_bad_disables_the_flag()
    {
        Assert.Null(GrowthRules.FlagFromDate(null));
        Assert.Null(GrowthRules.FlagFromDate(Config("")));
        Assert.Null(GrowthRules.FlagFromDate(Config("not a date")));
        Assert.Equal(new DateTime(2026, 10, 26), GrowthRules.FlagFromDate(Config("2026-10-26")));
    }

    private static IConfiguration Config(string flagFrom) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { { "Growth:FlagFromDate", flagFrom } }).Build();

    private static (long invoiceId, long paId) Seed(StockWorld w, string childType, bool givenByPa, DateTime givenDate,
        float? weight, float? height, bool notMeasured)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Atif", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        var child = db.Childs.First(c => c.Id == w.ChildId);
        child.Type = childType; child.DOB = DateTime.UtcNow.AddMonths(-14);
        var inv = new InvoiceSubmission
        {
            ChildId = w.ChildId, DoctorId = w.DoctorId, PaId = pa.Id, ClinicId = w.ClinicA,
            InvoiceDate = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow, TotalAmount = 5000, PaymentMode = "Cash"
        };
        db.InvoiceSubmissions.Add(inv); db.SaveChanges();
        var s = db.Schedules.First(x => x.Id == w.Schedule1Id);
        s.IsDone = true; s.GivenDate = givenDate; s.InvoiceSubmissionId = inv.Id;
        s.GivenByPaId = givenByPa ? pa.Id : (long?)null;
        s.Weight = weight; s.Height = height; s.GrowthNotMeasured = notMeasured;
        db.SaveChanges();
        return (inv.Id, pa.Id);
    }

    private static Newtonsoft.Json.Linq.JToken InvoiceRow(StockWorld w, string flagFrom)
    {
        using var db = w.NewContext();
        var res = (OkObjectResult)new PaCashHandoverController(db, Config(flagFrom)).GetReconciliation(w.DoctorId);
        var j = Newtonsoft.Json.Linq.JObject.FromObject(res.Value!);
        return j["ResponseData"]!.First(r => (string)r["RowType"]! == "Invoice");
    }

    [Fact]
    public void Pa_given_regular_dose_without_growth_is_flagged()
    {
        using var w = new StockWorld();
        Seed(w, "regular", true, DateTime.UtcNow, null, null, false);
        var row = InvoiceRow(w, "2020-01-01");
        Assert.Equal(1, (int)row["GrowthTotal"]!);
        Assert.Equal(1, (int)row["GrowthMissing"]!);
        Assert.Equal(0, (int)row["GrowthNotMeasured"]!);
    }

    [Fact]
    public void Not_measured_is_reported_separately_from_missing()
    {
        using var w = new StockWorld();
        Seed(w, "regular", true, DateTime.UtcNow, null, null, true);
        var row = InvoiceRow(w, "2020-01-01");
        Assert.Equal(0, (int)row["GrowthMissing"]!);
        Assert.Equal(1, (int)row["GrowthNotMeasured"]!);
    }

    [Fact]
    public void Dose_with_growth_does_not_flag()
    {
        using var w = new StockWorld();
        Seed(w, "regular", true, DateTime.UtcNow, 9.8f, null, false);
        var row = InvoiceRow(w, "2020-01-01");
        Assert.Equal(0, (int)row["GrowthMissing"]!);
        Assert.Equal(0, (int)row["GrowthNotMeasured"]!);
    }

    [Fact]
    public void Doctor_given_non_regular_and_past_doses_never_flag()
    {
        using (var w = new StockWorld())
        {
            Seed(w, "regular", givenByPa: false, DateTime.UtcNow, null, null, false);
            Assert.Equal(0, (int)InvoiceRow(w, "2020-01-01")["GrowthTotal"]!);
        }
        using (var w = new StockWorld())
        {
            Seed(w, "travel", true, DateTime.UtcNow, null, null, false);
            Assert.Equal(0, (int)InvoiceRow(w, "2020-01-01")["GrowthTotal"]!);
        }
        using (var w = new StockWorld())
        {
            Seed(w, "regular", true, DateTime.UtcNow.AddDays(-30), null, null, false);
            Assert.Equal(0, (int)InvoiceRow(w, DateTime.UtcNow.ToString("yyyy-MM-dd"))["GrowthTotal"]!);
        }
    }

    [Fact]
    public void Flag_is_off_when_no_cutoff_is_configured()
    {
        using var w = new StockWorld();
        Seed(w, "regular", true, DateTime.UtcNow, null, null, false);
        Assert.Equal(0, (int)InvoiceRow(w, "")["GrowthTotal"]!);
    }
}
