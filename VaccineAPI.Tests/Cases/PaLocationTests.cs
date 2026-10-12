using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

public class PaLocationTests
{
    static KmFix F(long shift, double lat, double lon, double? acc, DateTime at) =>
        new KmFix { ShiftId = shift, Lat = lat, Lon = lon, Accuracy = acc, At = at };
    static readonly DateTime T0 = new DateTime(2026, 10, 12, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Pkt_DayWindow_IsFixedPlusFive()
    {
        Assert.True(Pkt.TryParseDay("2026-10-12", out var d));
        Assert.Equal(new DateTime(2026, 10, 11, 19, 0, 0), Pkt.DayStartUtc(d));
        Assert.False(Pkt.TryParseDay("12/10/2026", out _));
        Assert.Equal("2026-10-13", Pkt.DayKey(new DateTime(2026, 10, 12, 19, 30, 0)));
        Assert.Equal(TimeSpan.FromHours(5), Pkt.ToPkt(new DateTime(2026, 1, 1, 0, 0, 0)).Offset);
    }

    [Fact]
    public void Km_JitterDoesNotAccumulate_AndRealMoveCounts()
    {
        // 20 m hops (sub-threshold) forever: anchor never advances, zero km
        var l = new List<KmFix>();
        for (int i = 0; i < 20; i++) l.Add(F(1, 31.0 + (i % 2) * 0.00018, 74.0, 10, T0.AddMinutes(i)));
        Assert.Equal(0, KmCalc.Compute(l).Km, 6);
        // ~1.1 km move
        l.Add(F(1, 31.01, 74.0, 10, T0.AddMinutes(21)));
        var r = KmCalc.Compute(l);
        Assert.InRange(r.Km, 1.0, 1.2);
    }

    [Fact]
    public void Km_NullAndPoorAccuracyRejected_ShiftsNotBridged_GapsFlagged()
    {
        var r = KmCalc.Compute(new[] { F(1, 31.0, 74.0, null, T0), F(1, 31.01, 74.0, 80, T0.AddMinutes(1)) });
        Assert.Equal(0, r.Km); Assert.Equal(2, r.Skipped);

        r = KmCalc.Compute(new[] { F(1, 31.0, 74.0, 5, T0), F(2, 31.01, 74.0, 5, T0.AddMinutes(2)) });
        Assert.Equal(0, r.Km);

        r = KmCalc.Compute(new[] { F(1, 31.0, 74.0, 5, T0), F(1, 31.01, 74.0, 5, T0.AddMinutes(20)) });
        Assert.Equal(0, r.Km); Assert.True(r.Gaps);
    }

    [Fact]
    public void Km_SpeedCap_AndReanchorAfterThreeRejects()
    {
        // 1.1 km in 1 min = 66 km/h ok; 11 km in 1 min = 660 km/h rejected, re-anchors on the 3rd
        var r = KmCalc.Compute(new[]
        {
            F(1, 31.0, 74.0, 5, T0),
            F(1, 31.1, 74.0, 5, T0.AddMinutes(1)),
            F(1, 31.1, 74.0005, 5, T0.AddMinutes(2)),
            F(1, 31.1, 74.001, 5, T0.AddMinutes(3)),
            F(1, 31.1, 74.0015, 5, T0.AddMinutes(4)),
        });
        Assert.InRange(r.Km, 0.04, 0.05);   // 3 rejects then re-anchor; only the last 47 m step counts
        Assert.Equal(2, r.Skipped);
        Assert.True(r.Usable[3]);
    }

    static (TestDb t, long paId) Seed()
    {
        var t = new TestDb();
        using var db = t.NewContext();
        var u1 = new User { MobileNumber = "3000000001", Password = "x", UserType = "DOCTOR", CountryCode = "92" };
        var u2 = new User { MobileNumber = "3000000002", Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.AddRange(u1, u2); db.SaveChanges();
        var doc = new Doctor { FirstName = "T", UserId = u1.Id }; db.Doctors.Add(doc); db.SaveChanges();
        Assert.Equal(1, doc.Id);
        var pa = new PersonalAssistant { DoctorId = doc.Id, UserId = u2.Id, IsActive = true, Name = "A" };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        db.PaPermissions.Add(new PaPermission { PaId = pa.Id, TrackLocation = true });
        db.SaveChanges();
        return (t, pa.Id);
    }

    [Fact]
    public void StaleOpenShift_AutoClosesAtLastFix()
    {
        var (t, pa) = Seed();
        using (var db = t.NewContext())
        {
            var s = new PaShift { PaId = pa, DoctorId = 1, StartedAt = DateTime.UtcNow.AddHours(-6) };
            db.PaShifts.Add(s); db.SaveChanges();
            db.PaLocations.Add(new PaLocation { ShiftId = s.Id, PaId = pa, DoctorId = 1, Latitude = 31, Longitude = 74, Accuracy = 5, RecordedAt = DateTime.UtcNow.AddHours(-5), ReceivedAt = DateTime.UtcNow.AddHours(-5) });
            db.SaveChanges();
        }
        using (var db = t.NewContext())
        {
            new PaLocationController(db).CloseStaleShifts(new List<long> { pa });
            var s = db.PaShifts.Single();
            Assert.NotNull(s.EndedAt);
            Assert.True((DateTime.UtcNow.AddHours(-5) - s.EndedAt.Value).Duration() < TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public void Ping_LateFixAfterShiftEnd_Accepted_Dedup_AndBadInputRejected()
    {
        var (t, pa) = Seed();
        using var db = t.NewContext();
        var end = DateTime.UtcNow.AddMinutes(-30);
        db.PaShifts.Add(new PaShift { PaId = pa, DoctorId = 1, StartedAt = end.AddHours(-2), EndedAt = end });
        db.SaveChanges();
        var c = new PaLocationController(db);
        var late = end.AddMinutes(-1);
        Assert.IsType<OkObjectResult>(c.Ping(new PaLocationController.PingDto { PaId = pa, Latitude = 31, Longitude = 74, Accuracy = 9, Battery = 150, RecordedAt = late }));
        Assert.IsType<OkObjectResult>(c.Ping(new PaLocationController.PingDto { PaId = pa, Latitude = 31, Longitude = 74, Accuracy = 9, RecordedAt = late }));
        Assert.Equal(1, db.PaLocations.Count());
        Assert.Null(db.PaLocations.Single().Battery);
        Assert.IsType<BadRequestObjectResult>(c.Ping(new PaLocationController.PingDto { PaId = pa, Latitude = 0, Longitude = 0 }));
        Assert.IsType<BadRequestObjectResult>(c.Ping(new PaLocationController.PingDto { PaId = pa, Latitude = 31, Longitude = 74, Accuracy = -1 }));
        Assert.IsType<BadRequestObjectResult>(c.Ping(new PaLocationController.PingDto { PaId = pa, Latitude = double.NaN, Longitude = 74 }));
        // not within the 10 min grace -> 409
        var r = c.Ping(new PaLocationController.PingDto { PaId = pa, Latitude = 31, Longitude = 74, Accuracy = 9 });
        Assert.Equal(409, ((ObjectResult)r).StatusCode);
    }

    [Fact]
    public void Km_BadDate_Is400_AndTrailV2_ReturnsSameKm()
    {
        var (t, pa) = Seed();
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.IsType<BadRequestObjectResult>(c.Km(1, "2026/10/01", null));
        Assert.IsType<BadRequestObjectResult>(c.GetTrailV2(1, pa, "nope"));

        var day = new DateTime(2026, 10, 12);
        var start = Pkt.DayStartUtc(day).AddHours(5);
        var s = new PaShift { PaId = pa, DoctorId = 1, StartedAt = start, EndedAt = start.AddHours(1) };
        db.PaShifts.Add(s); db.SaveChanges();
        for (int i = 0; i < 10; i++)
            db.PaLocations.Add(new PaLocation { ShiftId = s.Id, PaId = pa, DoctorId = 1, Latitude = 31 + i * 0.002, Longitude = 74, Accuracy = 5, RecordedAt = start.AddMinutes(i) });
        db.SaveChanges();

        var km = (OkObjectResult)c.Km(1, "2026-10-12", "2026-10-12");
        var kmJson = Newtonsoft.Json.JsonConvert.SerializeObject(km.Value);
        var tr = (OkObjectResult)c.GetTrailV2(1, pa, "2026-10-12");
        var trJson = Newtonsoft.Json.JsonConvert.SerializeObject(tr.Value);
        var kmCell = Newtonsoft.Json.Linq.JObject.Parse(kmJson)["assistants"][0]["cells"][0];
        var trKm = (double)Newtonsoft.Json.Linq.JObject.Parse(trJson)["km"];
        Assert.Equal((double)kmCell["km"], trKm);
        Assert.True(trKm > 1.5);
        Assert.Equal(1, (int)kmCell["shifts"]);
        Assert.Contains("+05:00", trJson);
    }
}
