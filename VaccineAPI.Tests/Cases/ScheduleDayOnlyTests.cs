using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

// TIMESTAMP_SPEC section 11: schedule Date/GivenDate are PKT days with no time, and the alert
// email endpoints find exactly what the alert list shows for the picked day or range.
public class ScheduleDayOnlyTests
{
    private static Schedule Row(Context db, long id) => db.Schedules.AsNoTracking().Single(s => s.Id == id);

    [Fact]
    public void Saved_schedule_dates_never_keep_a_time()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext())
        {
            var s = db.Schedules.Single(x => x.Id == w.Schedule1Id);
            s.Date = new DateTime(2026, 11, 25, 19, 0, 0, DateTimeKind.Utc);       // instant: PKT 26 Nov 00:00
            s.GivenDate = new DateTime(2026, 11, 25, 10, 30, 0);                    // wall value: drop the time
            var s2 = db.Schedules.Single(x => x.Id == w.Schedule2Id);
            s2.Date = new DateTime(2026, 11, 25, 5, 53, 24, DateTimeKind.Unspecified);
            db.SaveChanges();
        }
        using (var db = w.NewContext())
        {
            Assert.Equal(new DateTime(2026, 11, 26), Row(db, w.Schedule1Id).Date);
            Assert.Equal(new DateTime(2026, 11, 25), Row(db, w.Schedule1Id).GivenDate);
            Assert.Equal(new DateTime(2026, 11, 25), Row(db, w.Schedule2Id).Date);
        }
    }

    // A legacy row written before the fix (time part present) is due today by its calendar part.
    private static void MakeLegacyTimedRow(StockWorld w, long scheduleId, DateTime today)
    {
        using var db = w.NewContext();
        var c = db.Childs.Single(x => x.Id == w.ChildId);
        c.Email = "parent@example.test";
        db.SaveChanges();
        // Bypass the converter the way old data did: raw SQL.
        db.Database.ExecuteSqlRaw("UPDATE schedules SET Date = {0} WHERE Id = {1}",
            today.ToString("yyyy-MM-dd") + " 19:00:00", scheduleId);
    }

    [Fact]
    public void Alertone_finds_a_legacy_timed_row_due_today()
    {
        using var w = new StockWorld();
        MakeLegacyTimedRow(w, w.Schedule1Id, StockWorld.Today);
        using var db = w.NewContext();
        var r = w.Schedules(db).SendAlertEmail(w.ChildId);
        // Found the dose; the send itself cannot run in tests (no host), so it reports a send failure.
        Assert.NotEqual("No due vaccine found for the selected date.", r.Message);
        Assert.Equal("Failed to send email.", r.Message);
    }

    [Fact]
    public void Alertone_ignores_doses_already_given_or_skipped_today()
    {
        using var w = new StockWorld();
        using (var db = w.NewContext())
        {
            var c = db.Childs.Single(x => x.Id == w.ChildId);
            c.Email = "parent@example.test";
            db.Schedules.Single(x => x.Id == w.Schedule1Id).IsDone = true;
            db.Schedules.Single(x => x.Id == w.Schedule2Id).IsSkip = true;
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        var r = w.Schedules(db2).SendAlertEmail(w.ChildId);
        Assert.False(r.IsSuccess);
        Assert.Equal("No due vaccine found for the selected date.", r.Message);
    }

    [Fact]
    public void Alertone_follows_the_picked_date()
    {
        using var w = new StockWorld();
        var tomorrow = StockWorld.Today.AddDays(1);
        using (var db = w.NewContext())
        {
            db.Childs.Single(x => x.Id == w.ChildId).Email = "parent@example.test";
            db.Schedules.Single(x => x.Id == w.Schedule1Id).Date = tomorrow;
            db.Schedules.Single(x => x.Id == w.Schedule2Id).Date = StockWorld.Today.AddDays(-1);
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        // No date = today: nothing due today.
        Assert.Equal("No due vaccine found for the selected date.", w.Schedules(db2).SendAlertEmail(w.ChildId).Message);
        // Picked tomorrow: the list shows the child, so the email finds the dose.
        var list = w.Schedules(db2).GetAlert(tomorrow, 0, w.ClinicA);
        Assert.Contains(list.ResponseData, s => s.ChildId == w.ChildId);
        Assert.Equal("Failed to send email.", w.Schedules(db2).SendAlertEmail(w.ChildId, null, null, tomorrow).Message);
        Assert.Contains(w.Schedules(db2).GetAlert2(0, w.ClinicA, tomorrow).ResponseData, c => c.Id == w.ChildId);
        // Picked a day with nothing due: neither list nor email.
        var empty = StockWorld.Today.AddDays(5);
        Assert.DoesNotContain(w.Schedules(db2).GetAlert(empty, 0, w.ClinicA).ResponseData, s => s.ChildId == w.ChildId);
        Assert.Equal("No due vaccine found for the selected date.", w.Schedules(db2).SendAlertEmail(w.ChildId, null, null, empty).Message);
    }

    [Fact]
    public void List_and_bulk_email_agree_on_a_legacy_timed_row()
    {
        using var w = new StockWorld();
        MakeLegacyTimedRow(w, w.Schedule1Id, StockWorld.Today);
        using (var db = w.NewContext())
        {
            db.Schedules.Single(x => x.Id == w.Schedule2Id).IsDone = true;
            db.SaveChanges();
        }

        using var db2 = w.NewContext();
        var list = w.Schedules(db2).GetAlert(StockWorld.Today, 0, w.ClinicA);
        Assert.Contains(list.ResponseData, s => s.ChildId == w.ChildId);

        var bulk = w.Schedules(db2).GetAlert2(0, w.ClinicA);
        Assert.True(bulk.IsSuccess);
        Assert.Contains(bulk.ResponseData, c => c.Id == w.ChildId);
    }
}
