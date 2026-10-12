using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

// Adversarial QA of the PKT / km / stale-shift / ping / purge work. Run the suite under several TZ values
// (TZ=UTC, TZ=America/New_York, TZ=Asia/Karachi): every result here must be identical.
public class PaLocationAdversarialTests
{
    static KmFix F(long shift, double lat, double lon, double? acc, DateTime at) =>
        new KmFix { ShiftId = shift, Lat = lat, Lon = lon, Accuracy = acc, At = at };
    static readonly DateTime T0 = new DateTime(2026, 10, 12, 4, 0, 0, DateTimeKind.Utc);
    const double DegPerM = 1.0 / 111195.0;   // latitude degrees per metre

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

    static long AddOtherDoctorPa(TestDb t, bool track = true)
    {
        using var db = t.NewContext();
        var u3 = new User { MobileNumber = "3000000003", Password = "x", UserType = "DOCTOR", CountryCode = "92" };
        var u4 = new User { MobileNumber = "3000000004", Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.AddRange(u3, u4); db.SaveChanges();
        var d2 = new Doctor { FirstName = "Other", UserId = u3.Id }; db.Doctors.Add(d2); db.SaveChanges();
        var pa = new PersonalAssistant { DoctorId = d2.Id, UserId = u4.Id, IsActive = true, Name = "B" };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        db.PaPermissions.Add(new PaPermission { PaId = pa.Id, TrackLocation = track }); db.SaveChanges();
        return pa.Id;
    }

    static JObject J(ActionResult r) => Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(Newtonsoft.Json.JsonConvert.SerializeObject(((OkObjectResult)r).Value), new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })!;
    static string RawJ(object v) => Newtonsoft.Json.JsonConvert.SerializeObject(v);
    static JObject Parse(string s) => Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(s, new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })!;

    static long AddShift(TestDb t, long pa, DateTime start, DateTime? end, long doctor = 1)
    {
        using var db = t.NewContext();
        var s = new PaShift { PaId = pa, DoctorId = doctor, StartedAt = start, EndedAt = end };
        db.PaShifts.Add(s); db.SaveChanges(); return s.Id;
    }
    static void AddFix(TestDb t, long shift, long pa, DateTime at, double lat = 31, double lon = 74, double? acc = 5, long doctor = 1)
    {
        using var db = t.NewContext();
        db.PaLocations.Add(new PaLocation { ShiftId = shift, PaId = pa, DoctorId = doctor, Latitude = lat, Longitude = lon, Accuracy = acc, RecordedAt = at, ReceivedAt = at < DateTime.UtcNow ? at : DateTime.UtcNow });
        db.SaveChanges();
    }

    // ---------------- 1. PKT boundaries ----------------

    [Fact]
    public void Pkt_MidnightBoundary_AndMonthStart()
    {
        Assert.Equal("2026-10-12", Pkt.DayKey(new DateTime(2026, 10, 12, 18, 59, 59, DateTimeKind.Utc)));
        Assert.Equal("2026-10-13", Pkt.DayKey(new DateTime(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-09-30", Pkt.DayKey(new DateTime(2026, 9, 30, 18, 59, 59)));
        Assert.Equal("2026-10-01", Pkt.DayKey(new DateTime(2026, 9, 30, 19, 0, 0)));
        // Local-kind input is converted by the runtime, not reinterpreted
        var local = new DateTime(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.Equal("2026-10-13", Pkt.DayKey(local));
        Assert.Equal(new DateTime(2026, 9, 30, 19, 0, 0), Pkt.DayStartUtc("2026-10-01"));
        Assert.Equal(new DateTime(2026, 12, 31, 19, 0, 0), Pkt.DayStartUtc("2027-01-01"));
        var (f, t) = Pkt.DayWindowUtc(new DateTime(2026, 10, 12));
        Assert.Equal(TimeSpan.FromHours(24), t - f);   // no DST shift on any server zone
        Assert.Equal(TimeSpan.Zero, new TimeSpan(Pkt.Now().Offset.Ticks - Pkt.Offset.Ticks));
    }

    [Fact]
    public void Pkt_TryParseDay_RejectsOutOfRangeYears_NotJustFormat()
    {
        // must not be parseable into something that later throws ArgumentOutOfRange / overflows
        foreach (var s in new[] { "0001-01-01", "9999-12-31", "1999-12-31", "2100-01-01" })
        {
            if (Pkt.TryParseDay(s, out var d))
            {
                var ex = Record.Exception(() => { var w = Pkt.DayWindowUtc(d); var x = w.toUtc.AddDays(1); var y = d.AddDays(1); });
                Assert.Null(ex);
            }
        }
    }

    [Fact]
    public void Km_FixesOnEitherSideOfPktMidnight_LandInTheRightDay()
    {
        var (t, pa) = Seed();
        var s = AddShift(t, pa, new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc));
        AddFix(t, s, pa, new DateTime(2026, 10, 12, 18, 59, 59, DateTimeKind.Utc));
        AddFix(t, s, pa, new DateTime(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc));
        using var db = t.NewContext();
        var j = J(new PaLocationController(db).Km(1, "2026-10-12", "2026-10-13"));
        var cells = (JArray)j["assistants"][0]["cells"];
        Assert.Equal(1, (int)cells[0]["fixes"]); Assert.Equal(1, (int)cells[1]["fixes"]);
        Assert.Equal(1, (int)cells[0]["shifts"]); Assert.Equal(1, (int)cells[1]["shifts"]);
        Assert.Equal(1.0, (double)cells[0]["hours"], 3); Assert.Equal(1.0, (double)cells[1]["hours"], 3);
        var tr = J(new PaLocationController(db).GetTrailV2(1, pa, "2026-10-13"));
        Assert.Single((JArray)tr["points"]);
        Assert.Contains("+05:00", (string)tr["points"][0]["recordedAt"]);
        Assert.StartsWith("2026-10-13T00:00:00", (string)tr["points"][0]["recordedAt"]);
    }

    // ---------------- 2. Km vs TrailV2 ----------------

    [Fact]
    public void Km_VsTrailV2_ShiftCrossingMidnight_PerDayEquality_AndTotals()
    {
        var (t, pa) = Seed();
        var start = new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);   // 23:00 PKT
        var s = AddShift(t, pa, start, start.AddHours(2));
        for (int i = 0; i < 120; i++)
            AddFix(t, s, pa, start.AddMinutes(i), 31 + i * 55 * DegPerM, 74, 5);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        var j = J(c.Km(1, "2026-10-12", "2026-10-13"));
        var a = j["assistants"][0]; var cells = (JArray)a["cells"];
        double sumRaw = 0;
        for (int d = 0; d < 2; d++)
        {
            var tr = J(c.GetTrailV2(1, pa, d == 0 ? "2026-10-12" : "2026-10-13"));
            Assert.Equal((double)cells[d]["km"], (double)tr["km"]);
            Assert.Equal((int)cells[d]["skipped"], (int)tr["skipped"]);
            Assert.Equal((bool)cells[d]["gaps"], (bool)tr["gaps"]);
            sumRaw += (double)tr["km"];
        }
        Assert.InRange((double)cells[0]["km"] + (double)cells[1]["km"], 6.4, 6.7);   // 119 steps * 55 m ~ 6.5 km minus the unbridged midnight step
        Assert.InRange(Math.Abs((double)a["totalKm"] - sumRaw), 0, 0.1001);
        Assert.Equal(2, (int)a["totalShifts"]); Assert.Equal(2, (int)a["shiftDays"]);
        Assert.Equal(2.0, (double)a["totalHours"], 2);
    }

    // ---------------- 3. KmCalc ----------------

    [Fact]
    public void KmCalc_StationaryJitter8Hours_IsZero()
    {
        var rnd = new Random(42);
        var l = new List<KmFix>();
        for (int i = 0; i < 480; i++)   // +-8 m per axis around a point, accuracy 10
            l.Add(F(1, 31 + (rnd.NextDouble() * 2 - 1) * 8 * DegPerM, 74 + (rnd.NextDouble() * 2 - 1) * 8 * DegPerM, 10, T0.AddMinutes(i)));
        var r = KmCalc.Compute(l);
        Assert.True(r.Km < 0.05, "stationary jitter produced " + r.Km + " km");
        Assert.False(r.Gaps);
    }

    [Fact]
    public void KmCalc_StationaryNoisyGps_AccuracyAware_IsNearZero()
    {
        var rnd = new Random(7);
        var l = new List<KmFix>();
        for (int i = 0; i < 480; i++)   // +-15 m jitter, device reports accuracy 20 -> threshold 40 m
            l.Add(F(1, 31 + (rnd.NextDouble() * 2 - 1) * 15 * DegPerM, 74 + (rnd.NextDouble() * 2 - 1) * 15 * DegPerM, 20, T0.AddMinutes(i)));
        Assert.True(KmCalc.Compute(l).Km < 0.1);
    }

    [Fact]
    public void KmCalc_SlowWalk_20mSteps_IsCounted()
    {
        var l = new List<KmFix>();
        for (int i = 0; i <= 60; i++) l.Add(F(1, 31 + i * 20 * DegPerM, 74, 5, T0.AddMinutes(i)));
        var r = KmCalc.Compute(l);
        Assert.InRange(r.Km, 1.1, 1.2);   // 1.2 km walked; at most one sub-threshold step of tail lost
    }

    [Fact]
    public void KmCalc_BadFirstFix_IsSkipped_NotAnAnchor()
    {
        var r = KmCalc.Compute(new[]
        {
            F(1, 40, 80, 200, T0),                         // poor accuracy, far away
            F(1, 31, 74, 5, T0.AddMinutes(1)),
            F(1, 31 + 500 * DegPerM, 74, 5, T0.AddMinutes(2)),
        });
        Assert.Equal(1, r.Skipped); Assert.InRange(r.Km, 0.49, 0.51);
        Assert.False(r.Usable[0]);
    }

    [Fact]
    public void KmCalc_SpikeOutAndBack_Rejected()
    {
        var r = KmCalc.Compute(new[]
        {
            F(1, 31, 74, 5, T0),
            F(1, 31 + 20000 * DegPerM, 74, 5, T0.AddMinutes(1)),   // 20 km in a minute
            F(1, 31, 74, 5, T0.AddMinutes(2)),
        });
        Assert.Equal(0, r.Km, 6); Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void KmCalc_SpikeAfterLongStationary_IsStillRejected()
    {
        // 8 h of tiny jitter keeps the anchor at the original point; speed must be judged against the time
        // since the previous fix (1 min), not since the anchor (8 h), or a 50 km glitch looks like 6 km/h.
        var l = new List<KmFix>();
        for (int i = 0; i < 480; i++) l.Add(F(1, 31 + (i % 2) * 5 * DegPerM, 74, 10, T0.AddMinutes(i)));
        l.Add(F(1, 31 + 50000 * DegPerM, 74, 5, T0.AddMinutes(480)));
        l.Add(F(1, 31, 74, 5, T0.AddMinutes(481)));
        var r = KmCalc.Compute(l);
        Assert.True(r.Km < 0.05, "spike after stationary period counted " + r.Km + " km");
    }

    [Fact]
    public void KmCalc_NullAccuracy_Rejected_AndGapFlags()
    {
        var r = KmCalc.Compute(new[] { F(1, 31, 74, null, T0), F(1, 31, 74, null, T0.AddMinutes(1)) });
        Assert.Equal(0, r.Used); Assert.Equal(2, r.Skipped);

        // stationary PA, 3 h of fixes every minute: no gap flag
        var st = new List<KmFix>();
        for (int i = 0; i < 180; i++) st.Add(F(1, 31 + (i % 2) * 3 * DegPerM, 74, 10, T0.AddMinutes(i)));
        Assert.False(KmCalc.Compute(st).Gaps);

        // driver, 20 min of silence: flagged, not bridged
        var dr = KmCalc.Compute(new[]
        {
            F(1, 31, 74, 5, T0), F(1, 31 + 500 * DegPerM, 74, 5, T0.AddMinutes(1)),
            F(1, 31 + 15000 * DegPerM, 74, 5, T0.AddMinutes(21)),
            F(1, 31 + 15500 * DegPerM, 74, 5, T0.AddMinutes(22)),
        });
        Assert.True(dr.Gaps);
        Assert.InRange(dr.Km, 0.99, 1.01);   // 0.5 + 0.5, the 14.5 km silent leg is not counted
    }

    [Fact]
    public void KmCalc_ExactlyFifteenMinutes_IsBridged_SixteenIsNot()
    {
        var a = KmCalc.Compute(new[] { F(1, 31, 74, 5, T0), F(1, 31 + 1000 * DegPerM, 74, 5, T0.AddMinutes(15)) });
        Assert.False(a.Gaps); Assert.InRange(a.Km, 0.99, 1.01);
        var b = KmCalc.Compute(new[] { F(1, 31, 74, 5, T0), F(1, 31 + 1000 * DegPerM, 74, 5, T0.AddMinutes(16)) });
        Assert.True(b.Gaps); Assert.Equal(0, b.Km);
    }

    [Fact]
    public void KmCalc_InputOrderIrrelevant_UsableMapsToInputIndex()
    {
        var fx = new List<KmFix>
        {
            F(1, 31 + 500 * DegPerM, 74, 5, T0.AddMinutes(1)),
            F(1, 31, 74, 5, T0),
            F(1, 31 + 1000 * DegPerM, 74, 5, T0.AddMinutes(2)),
        };
        var r = KmCalc.Compute(fx);
        Assert.InRange(r.Km, 0.99, 1.01); Assert.All(r.Usable, Assert.True);
    }

    [Fact]
    public void Km_CoverageAndNoGps_Flags()
    {
        var (t, pa) = Seed();
        var day = new DateTime(2026, 10, 12);
        var st = Pkt.DayStartUtc(day).AddHours(5);
        var s1 = AddShift(t, pa, st, st.AddHours(1));                           // shift with zero fixes -> noGps
        var st2 = Pkt.DayStartUtc(day.AddDays(1)).AddHours(5);
        var s2 = AddShift(t, pa, st2, st2.AddHours(1));                         // half-covered
        for (int i = 0; i < 31; i++) AddFix(t, s2, pa, st2.AddMinutes(i));
        using var db = t.NewContext();
        var cells = (JArray)J(new PaLocationController(db).Km(1, "2026-10-12", "2026-10-14"))["assistants"][0]["cells"];
        Assert.True((bool)cells[0]["noGps"]); Assert.Equal(0, (int)cells[0]["fixes"]); Assert.Equal(0.0, (double)cells[0]["coverage"]);
        Assert.False((bool)cells[1]["noGps"]); Assert.Equal(0.5, (double)cells[1]["coverage"], 2);
        Assert.False((bool)cells[2]["noGps"]); Assert.Equal(0, (int)cells[2]["shifts"]);   // no shift, no fixes: not noGps
    }

    // ---------------- 4. Stale shifts ----------------

    [Fact]
    public void Stale_LiveShiftWithRecentFix_NotClosed_EvenIfStartedLongAgo()
    {
        var (t, pa) = Seed();
        var s = AddShift(t, pa, DateTime.UtcNow.AddHours(-9), null);
        AddFix(t, s, pa, DateTime.UtcNow.AddMinutes(-1).AddTicks(-DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));
        using var db = t.NewContext();
        new PaLocationController(db).CloseStaleShifts(new List<long> { pa });
        Assert.Null(db.PaShifts.Single().EndedAt);
    }

    [Fact]
    public void Stale_Threshold_Just_Under_And_Over_ThreeHours()
    {
        var (t, pa) = Seed();
        var s = AddShift(t, pa, DateTime.UtcNow.AddHours(-5), null);
        AddFix(t, s, pa, DateTime.UtcNow.AddMinutes(-170));
        using (var db = t.NewContext()) { new PaLocationController(db).CloseStaleShifts(new List<long> { pa }); Assert.Null(db.PaShifts.Single().EndedAt); }
        using (var db = t.NewContext()) { db.PaLocations.Single().RecordedAt = DateTime.UtcNow.AddMinutes(-185); db.PaLocations.Single().ReceivedAt = DateTime.UtcNow.AddMinutes(-185); db.SaveChanges(); }
        using (var db = t.NewContext())
        {
            new PaLocationController(db).CloseStaleShifts(new List<long> { pa });
            var e = db.PaShifts.Single().EndedAt; Assert.NotNull(e);
            Assert.True((DateTime.UtcNow.AddMinutes(-185) - e.Value).Duration() < TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public void Stale_NoFixShift_ClosesAtStart_Not_Now()
    {
        var (t, pa) = Seed();
        var st = DateTime.UtcNow.AddHours(-4);
        AddShift(t, pa, st, null);
        using var db = t.NewContext();
        new PaLocationController(db).CloseStaleShifts(new List<long> { pa });
        Assert.Equal(st, db.PaShifts.Single().EndedAt);
    }

    [Fact]
    public void Stale_OverFourteenHours_ClosesAtLastFix_DuplicatesCollapsed()
    {
        var (t, pa) = Seed();
        var old = AddShift(t, pa, DateTime.UtcNow.AddHours(-15), null);
        var lastFix = DateTime.UtcNow.AddMinutes(-2);
        AddFix(t, old, pa, lastFix);
        // two overlapping fresh open shifts: older one must be closed
        var a = AddShift(t, pa, DateTime.UtcNow.AddMinutes(-30), null);
        var b = AddShift(t, pa, DateTime.UtcNow.AddMinutes(-10), null);
        using var db = t.NewContext();
        new PaLocationController(db).CloseStaleShifts(new List<long> { pa });
        Assert.Equal(lastFix, db.PaShifts.Single(x => x.Id == old).EndedAt);
        Assert.NotNull(db.PaShifts.Single(x => x.Id == a).EndedAt);
        Assert.Null(db.PaShifts.Single(x => x.Id == b).EndedAt);
        // idempotent
        new PaLocationController(db).CloseStaleShifts(new List<long> { pa });
        Assert.Equal(lastFix, db.PaShifts.Single(x => x.Id == old).EndedAt);
    }

    [Fact]
    public void Stale_ReadEndpoints_OnlyTouchOwnTrackedPas()
    {
        var (t, pa) = Seed();
        var other = AddOtherDoctorPa(t);
        var otherShift = AddShift(t, other, DateTime.UtcNow.AddHours(-20), null, doctor: 2);
        var mine = AddShift(t, pa, DateTime.UtcNow.AddHours(-20), null);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        c.Live(1); c.Km(1, null, null);
        Assert.Null(db.PaShifts.Single(x => x.Id == otherShift).EndedAt);
        Assert.NotNull(db.PaShifts.Single(x => x.Id == mine).EndedAt);
    }

    [Fact]
    public void Ping_After4HourOffline_AttachesToOpenShift_BacklogKept_ShiftStaysOpen()
    {
        var (t, pa) = Seed();
        var s = AddShift(t, pa, DateTime.UtcNow.AddHours(-6), null);
        AddFix(t, s, pa, DateTime.UtcNow.AddHours(-5).AddMinutes(-1));
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        for (int i = 0; i < 20; i++)   // backlog uploaded oldest-first, spanning the offline period
        {
            var r = c.Ping(P(pa, lat: 31 + i * 1e-4, at: DateTime.UtcNow.AddHours(-4).AddMinutes(i * 10)));
            Assert.IsType<OkObjectResult>(r);
            c.Live(1); c.Km(1, null, null);        // read endpoints in between must not close a shift that is being fed
        }
        Assert.Null(db.PaShifts.Single().EndedAt);
        Assert.Equal(21, db.PaLocations.Count());
        Assert.IsType<OkObjectResult>(c.Ping(P(pa, lat: 31.5)));   // live fix afterwards
    }

    [Fact]
    public void Stale_AbandonedShift_ClosedByLiveAndKm_ButNotByPing()
    {
        var (t, pa) = Seed();
        var s = AddShift(t, pa, DateTime.UtcNow.AddHours(-6), null);
        var last = DateTime.UtcNow.AddHours(-5);
        AddFix(t, s, pa, last);
        using (var db = t.NewContext()) { new PaLocationController(db).Live(1); Assert.Equal(last, db.PaShifts.Single().EndedAt); }
        using (var db = t.NewContext()) { db.PaShifts.Single().EndedAt = null; db.SaveChanges(); new PaLocationController(db).Km(1, null, null); Assert.Equal(last, db.PaShifts.Single().EndedAt); }
    }

    [Fact]
    public void Live_Default_IsBareArray_V2_IsObject()
    {
        var (t, _) = Seed();
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.IsType<JArray>(JToken.Parse(RawJ(((OkObjectResult)c.Live(1)).Value)));
        Assert.IsType<JObject>(JToken.Parse(RawJ(((OkObjectResult)c.Live(1, 2)).Value)));
    }

    // ---------------- 5. Authorization-adjacent ----------------

    [Fact]
    public void TrailV2_UntrackedOrForeignOrZeroPa_Is403_NeverForbidResult()
    {
        var (t, pa) = Seed();
        var other = AddOtherDoctorPa(t);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        foreach (var id in new long[] { 0, other, 9999 })
            Assert.Equal(403, ((ObjectResult)c.GetTrailV2(1, id, "2026-10-12")).StatusCode);
        db.PaPermissions.Single(p => p.PaId == pa).TrackLocation = false; db.SaveChanges();
        Assert.Equal(403, ((ObjectResult)c.GetTrailV2(1, pa, "2026-10-12")).StatusCode);
        // a doctor other than 1 must get a plain 403 (ForbidResult throws with no auth scheme configured)
        var r = c.GetTrailV2(2, other, "2026-10-12");
        Assert.IsNotType<ForbidResult>(r);
        Assert.Equal(403, ((ObjectResult)r).StatusCode);
    }

    [Fact]
    public void Live_Km_OtherDoctor_EmptyShape()
    {
        var (t, _) = Seed();
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.Empty((JArray)J(c.Live(2, 2))["assistants"]);
        Assert.Empty((JArray)J(c.Km(2, null, null))["assistants"]);
        Assert.IsType<JArray>(JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(((OkObjectResult)c.Live(2)).Value)));
    }

    [Fact]
    public void Km_RangeValidation()
    {
        var (t, _) = Seed();
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.IsType<OkObjectResult>(c.Km(1, "2026-09-01", "2026-10-01"));          // 31 days
        Assert.IsType<BadRequestObjectResult>(c.Km(1, "2026-09-01", "2026-10-02"));  // 32 days
        Assert.IsType<BadRequestObjectResult>(c.Km(1, "2026-10-02", "2026-10-01"));
        Assert.IsType<BadRequestObjectResult>(c.Km(1, "2026-02-30", null));
        Assert.IsType<BadRequestObjectResult>(c.Km(1, "0001-01-01", "0001-01-05"));
        Assert.IsType<BadRequestObjectResult>(c.Km(1, "9999-12-01", "9999-12-31"));
        Assert.IsType<BadRequestObjectResult>(c.GetTrailV2(1, 1, "0001-01-01"));
        Assert.IsType<BadRequestObjectResult>(c.Trail(1, 1, "12/10/2026"));
        Assert.IsType<OkObjectResult>(c.Trail(1, 1, "2026-10-12"));
    }

    // ---------------- 6. Ping ----------------

    PaLocationController.PingDto P(long pa, double lat = 31, double lon = 74, double? acc = 5, int? bat = null, DateTime? at = null) =>
        new PaLocationController.PingDto { PaId = pa, Latitude = lat, Longitude = lon, Accuracy = acc, Battery = bat, RecordedAt = at };

    [Fact]
    public void Ping_ValidationBranches()
    {
        var (t, pa) = Seed();
        AddShift(t, pa, DateTime.UtcNow.AddMinutes(-30), null);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.IsType<BadRequestObjectResult>(c.Ping(null!));
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, lat: 91)));
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, lon: -181)));
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, lon: double.PositiveInfinity)));
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, acc: 10001)));
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, acc: double.NaN)));
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, at: DateTime.UtcNow.AddHours(-13))));         // too old
        Assert.IsType<BadRequestObjectResult>(c.Ping(P(pa, at: DateTime.UtcNow.AddHours(-1))));          // before shift start
        Assert.Equal(403, ((ObjectResult)c.Ping(P(99999))).StatusCode);
        Assert.Equal(0, db.PaLocations.Count());
        Assert.IsType<OkObjectResult>(c.Ping(P(pa, acc: null, bat: -5)));
        Assert.Null(db.PaLocations.Single().Battery); Assert.Null(db.PaLocations.Single().Accuracy);
    }

    [Fact]
    public void Ping_NoShift_409_UntrackedPa_403()
    {
        var (t, pa) = Seed();
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.Equal(409, ((ObjectResult)c.Ping(P(pa))).StatusCode);
        db.PaPermissions.Single().TrackLocation = false; db.SaveChanges();
        Assert.Equal(403, ((ObjectResult)c.Ping(P(pa))).StatusCode);
    }

    [Fact]
    public void Ping_FutureRecordedAt_ClampedToNow_Truncated_AndDedupSameSecond()
    {
        var (t, pa) = Seed();
        AddShift(t, pa, DateTime.UtcNow.AddMinutes(-30), null);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.IsType<OkObjectResult>(c.Ping(P(pa, at: DateTime.UtcNow.AddHours(3))));
        var rec = db.PaLocations.Single().RecordedAt;
        Assert.True(rec <= DateTime.UtcNow && rec > DateTime.UtcNow.AddSeconds(-5));
        Assert.Equal(0, rec.Ticks % TimeSpan.TicksPerSecond);
        // two distinct fixes within the same second: second is treated as duplicate (documented trade-off)
        var at = DateTime.UtcNow.AddMinutes(-5);
        var a = new DateTime(at.Ticks - at.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        c.Ping(P(pa, lat: 31.0, at: a.AddMilliseconds(100)));
        c.Ping(P(pa, lat: 31.5, at: a.AddMilliseconds(900)));
        Assert.Equal(2, db.PaLocations.Count());
    }

    [Fact]
    public void Ping_RecordedAtKinds_AllResolveToSameUtcInstant()
    {
        var (t, pa) = Seed();
        AddShift(t, pa, DateTime.UtcNow.AddHours(-2), null);
        var instant = new DateTime(DateTime.UtcNow.AddMinutes(-20).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        c.Ping(P(pa, at: DateTime.SpecifyKind(instant, DateTimeKind.Unspecified)));          // no offset in JSON = UTC
        c.Ping(P(pa, at: instant));                                                          // 'Z'
        c.Ping(P(pa, at: instant.ToLocalTime()));                                            // "+hh:mm" parsed to Local by Newtonsoft
        Assert.Equal(instant, db.PaLocations.Single().RecordedAt);
    }

    [Fact]
    public void Ping_LateFix_BoundaryAtTenMinutes_And_BeforeNewShift()
    {
        var (t, pa) = Seed();
        var end = DateTime.UtcNow.AddMinutes(-20);
        end = new DateTime(end.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        AddShift(t, pa, end.AddHours(-1), end);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        Assert.IsType<OkObjectResult>(c.Ping(P(pa, at: end.AddMinutes(8))));     // within +10 min of end and not in the future
        Assert.Equal(409, ((ObjectResult)c.Ping(P(pa, lat: 31.2, at: end.AddMinutes(11)))).StatusCode);
    }

    [Fact]
    public void Ping_QueuedFixFromEndedShift_WhileNewShiftOpen_IsRejectedNot_Misattributed()
    {
        var (t, pa) = Seed();
        var old = AddShift(t, pa, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-2));
        var cur = AddShift(t, pa, DateTime.UtcNow.AddMinutes(-30), null);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        var r = c.Ping(P(pa, at: DateTime.UtcNow.AddHours(-2).AddMinutes(-1)));
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(0, db.PaLocations.Count());
    }

    [Fact]
    public void Start_ClosesOldAtLastFix_AndOpensExactlyOne()
    {
        var (t, pa) = Seed();
        var old = AddShift(t, pa, DateTime.UtcNow.AddHours(-1), null);
        var last = DateTime.UtcNow.AddMinutes(-20);
        AddFix(t, old, pa, last);
        using var db = t.NewContext();
        var r = new PaLocationController(db).Start(new PaLocationController.PaIdDto { PaId = pa });
        Assert.IsType<OkObjectResult>(r);
        Assert.Equal(1, db.PaShifts.Count(s => s.EndedAt == null));
        Assert.Equal(last, db.PaShifts.Single(s => s.Id == old).EndedAt);
    }

    [Fact]
    public void Start_ClosesOldBeforeInserting_SoTheOpenShiftUniqueIndexCannotFalseConflict()
    {
        // With UX_pashifts_PaId_Open an INSERT executed before the closing UPDATE would violate the index.
        // Enforce the ordering by checking that the close is committed by a separate SaveChanges.
        var (t, pa) = Seed();
        AddShift(t, pa, DateTime.UtcNow.AddHours(-1), null);
        using var db = t.NewContext();
        var saves = new List<int>();
        var c = new PaLocationController(db);
        db.SavingChanges += (_, __) =>
            saves.Add(db.ChangeTracker.Entries<PaShift>().Count(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added)
                    + 10 * db.ChangeTracker.Entries<PaShift>().Count(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Modified));
        c.Start(new PaLocationController.PaIdDto { PaId = pa });
        Assert.DoesNotContain(saves, x => x % 10 > 0 && x / 10 > 0);   // never an INSERT and an UPDATE in one batch
    }

    // ---------------- 7. Purge ----------------

    static void ResetPurge() =>
        typeof(PaLocationController).GetField("_lastPurgeUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, DateTime.MinValue);

    [Fact]
    public void Purge_ExecuteDelete_ActuallyDeletes_AtPktBoundary_KeepsOpenAndRecent()
    {
        var (t, pa) = Seed();
        var cutoff = Pkt.TodayStartUtc().AddDays(-35);
        var oldShift = AddShift(t, pa, cutoff.AddDays(-2), cutoff.AddDays(-2).AddHours(1));
        var openOld = AddShift(t, pa, cutoff.AddDays(-3), null);
        var recent = AddShift(t, pa, cutoff.AddDays(5), cutoff.AddDays(5).AddHours(1));
        AddFix(t, oldShift, pa, cutoff.AddSeconds(-1));
        AddFix(t, recent, pa, cutoff);                      // exactly at cutoff: kept
        AddFix(t, recent, pa, cutoff.AddDays(5));
        ResetPurge();
        using var db = t.NewContext();
        new PaLocationController(db).PurgeOldThrottled();
        Assert.Equal(2, db.PaLocations.Count());
        Assert.DoesNotContain(db.PaShifts.ToList(), s => s.Id == oldShift);
        Assert.Contains(db.PaShifts.ToList(), s => s.Id == openOld);
        Assert.Contains(db.PaShifts.ToList(), s => s.Id == recent);
        Assert.True(cutoff < Pkt.TodayStartUtc().AddDays(-30), "retention must exceed the 31-day km window");
    }

    // ---------------- 9. JSON shape ----------------

    [Fact]
    public void Json_Live_Km_TrailV2_Shapes_WithAppSettings()
    {
        var (t, pa) = Seed();
        var s = AddShift(t, pa, DateTime.UtcNow.AddMinutes(-30), null);
        var fixAt = DateTime.UtcNow.AddSeconds(-30); fixAt = new DateTime(fixAt.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        AddFix(t, s, pa, fixAt);
        AddFix(t, s, pa, fixAt.AddSeconds(-60), 31.001);
        using var db = t.NewContext();
        var c = new PaLocationController(db);
        string Ser(object v) => Newtonsoft.Json.JsonConvert.SerializeObject(v, new Newtonsoft.Json.JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver() });

        var live = Parse(Ser(((OkObjectResult)c.Live(1, 2)).Value));
        Assert.Contains("+05:00", (string)live["serverNowPkt"]);
        var a = (JObject)live["assistants"][0];
        foreach (var k in new[] { "paId", "name", "profileImage", "onShift", "status", "shiftStartedAt", "shiftEndedAt", "latitude", "longitude", "accuracy", "battery", "lastSeenAt", "ageSeconds", "lastKnown", "serverNowPkt" })
            Assert.True(a.ContainsKey(k), "live missing " + k);
        Assert.Equal("onshift", (string)a["status"]); Assert.False((bool)a["lastKnown"]);
        Assert.EndsWith("+05:00", (string)a["lastSeenAt"]); Assert.EndsWith("+05:00", (string)a["shiftStartedAt"]);
        Assert.Equal(JTokenType.Null, a["shiftEndedAt"].Type);
        Assert.InRange((long)a["ageSeconds"], 0, 60);
        var bare = Newtonsoft.Json.JsonConvert.DeserializeObject<JToken>(Ser(((OkObjectResult)c.Live(1)).Value), new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })!; Assert.IsType<JArray>(bare); Assert.Contains("+05:00", (string)((JArray)bare)[0]["serverNowPkt"]);

        var km = Parse(Ser(((OkObjectResult)c.Km(1, null, null)).Value));
        Assert.True(km.ContainsKey("serverTodayPkt")); Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", (string)km["serverTodayPkt"]);
        var cell = (JObject)km["assistants"][0]["cells"].Last;
        foreach (var k in new[] { "date", "km", "shifts", "hours", "fixes", "skipped", "gaps", "coverage", "noGps" }) Assert.True(cell.ContainsKey(k), "km cell missing " + k);
        foreach (var k in new[] { "paId", "name", "totalKm", "shiftDays", "avgKm", "totalShifts", "totalHours", "cells" }) Assert.True(((JObject)km["assistants"][0]).ContainsKey(k), "km pa missing " + k);

        var tr = Parse(Ser(((OkObjectResult)c.GetTrailV2(1, pa, Pkt.TodayKey())).Value));
        foreach (var k in new[] { "serverNowPkt", "km", "gaps", "skipped", "truncated", "points" }) Assert.True(tr.ContainsKey(k), "trailv2 missing " + k);
        var pt = (JObject)tr["points"][0];
        foreach (var k in new[] { "lat", "lng", "recordedAt", "accuracy", "shiftId", "usable" }) Assert.True(pt.ContainsKey(k), "point missing " + k);
        Assert.EndsWith("+05:00", (string)pt["recordedAt"]);
        // ascending order
        Assert.True(DateTimeOffset.Parse((string)tr["points"][0]["recordedAt"]) < DateTimeOffset.Parse((string)tr["points"][1]["recordedAt"]));
    }

    [Fact]
    public void Live_Status_OffNosignalOnshift_AndLastKnown()
    {
        var (t, pa) = Seed();
        using (var db = t.NewContext())
            Assert.Equal("off", (string)J(new PaLocationController(db).Live(1, 2))["assistants"][0]["status"]);
        var s = AddShift(t, pa, DateTime.UtcNow.AddHours(-1), null);
        using (var db = t.NewContext())
        {
            var a = J(new PaLocationController(db).Live(1, 2))["assistants"][0];
            Assert.Equal("nosignal", (string)a["status"]); Assert.False((bool)a["lastKnown"]);   // no fix at all
        }
        AddFix(t, s, pa, DateTime.UtcNow.AddMinutes(-10));
        using (var db = t.NewContext())
        {
            var a = J(new PaLocationController(db).Live(1, 2))["assistants"][0];
            Assert.Equal("nosignal", (string)a["status"]); Assert.False((bool)a["lastKnown"]);   // stale fix on an open shift
        }
        using (var db = t.NewContext()) { db.PaShifts.Single().EndedAt = DateTime.UtcNow.AddMinutes(-5); db.SaveChanges(); }
        using (var db = t.NewContext())
        {
            var a = J(new PaLocationController(db).Live(1, 2))["assistants"][0];
            Assert.Equal("off", (string)a["status"]); Assert.True((bool)a["lastKnown"]); Assert.NotNull(a["shiftEndedAt"].Type == JTokenType.Null ? null : "x");
        }
    }
}
