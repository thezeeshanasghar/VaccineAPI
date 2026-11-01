using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>PA push triggers: the 1-hour-before reminder window and the notifications for visit/assignment changes.</summary>
public class PaPushTriggerTests
{
    private static readonly TimeSpan Pkt = TimeSpan.FromHours(5);

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    [Theory]
    [InlineData("14:00", "15:00", true)]   // exactly 60 min before
    [InlineData("14:30", "15:00", true)]
    [InlineData("14:59", "15:00", true)]
    [InlineData("13:59", "15:00", false)]  // more than an hour away
    [InlineData("15:00", "15:00", false)]  // window already open
    [InlineData("15:30", "15:00", false)]
    [InlineData("06:00", "07:00", true)]   // early visit: reminder lands before 09:00
    public void Reminder_window_boundaries(string now, string start, bool expected)
        => Assert.Equal(expected, PushReminderService.InReminderWindow(TimeSpan.Parse(now), TimeSpan.Parse(start)));

    private static (long child, long asg) SeedVisit(StockWorld w, TimeSpan? windowStart, DateTime targetDate, Action<PAAssignment>? tweak = null)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Hania", ClinicId = w.ClinicA, UserId = parent.Id, DOB = new DateTime(2026, 7, 13) };
        db.Childs.Add(child); db.SaveChanges();
        var a = new PAAssignment
        {
            DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = child.Id, PersonalAssistantId = 17, AssignedAt = DateTime.UtcNow,
            IsScheduledVisit = true, TargetDate = targetDate, WindowStart = windowStart, Area = "DHA"
        };
        tweak?.Invoke(a);
        db.PAAssignments.Add(a); db.SaveChanges();
        return (child.Id, a.Id);
    }

    [Fact]
    public async Task Candidates_only_include_open_visits_with_a_time_inside_the_next_hour()
    {
        using var w = new StockWorld();
        // 14:30 PKT today = 09:30 UTC
        var nowUtc = new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc);
        var today = (nowUtc + Pkt).Date;
        var inWindow = SeedVisit(w, new TimeSpan(15, 0, 0), today).asg;
        SeedVisit(w, new TimeSpan(17, 0, 0), today);                                            // too far away
        SeedVisit(w, null, today);                                                              // no time: no reminder
        SeedVisit(w, new TimeSpan(15, 0, 0), today.AddDays(1));                                 // other day
        SeedVisit(w, new TimeSpan(15, 0, 0), today, a => a.IsCancelled = true);                 // cancelled
        SeedVisit(w, new TimeSpan(15, 0, 0), today, a => a.IsCompleted = true);                 // completed
        SeedVisit(w, new TimeSpan(15, 0, 0), today, a => a.IsScheduledVisit = false);           // not a scheduled visit

        using var db = w.NewContext();
        var rows = await PushReminderService.VisitReminderCandidates(db, nowUtc);
        Assert.Single(rows);
        Assert.Equal(inWindow, rows[0].AssignmentId);
        Assert.Equal("Hania", rows[0].ChildName);
    }

    [Fact]
    public async Task Changing_the_target_date_notifies_the_PA_and_the_parent_of_a_home_visit()
    {
        using var w = new StockWorld();
        var (childId, asgId) = SeedVisit(w, new TimeSpan(15, 0, 0), new DateTime(2026, 11, 5));
        using var db = w.NewContext();
        var r = await Ctl(db).SetTargetDate(asgId, new SetTargetDateDto { DoctorId = w.DoctorId, TargetDate = new DateTime(2026, 11, 6) });
        Assert.NotNull(r);
        using var check = w.NewContext();
        var parentId = check.Childs.Single(c => c.Id == childId).UserId;
        Assert.Contains(check.Notifications, n => n.Type == "PaVisitChanged" && n.RecipientType == "PA" && n.RecipientId == 17);
        Assert.Contains(check.Notifications, n => n.Type == "VisitChanged" && n.RecipientType == "PARENT" && n.RecipientId == parentId);
    }

    [Fact]
    public async Task Setting_the_same_date_again_sends_nothing()
    {
        using var w = new StockWorld();
        var (_, asgId) = SeedVisit(w, new TimeSpan(15, 0, 0), new DateTime(2026, 11, 5));
        using var db = w.NewContext();
        await Ctl(db).SetTargetDate(asgId, new SetTargetDateDto { DoctorId = w.DoctorId, TargetDate = new DateTime(2026, 11, 5) });
        using var check = w.NewContext();
        Assert.Empty(check.Notifications);
    }

    [Fact]
    public async Task Deleting_an_assignment_tells_the_PA()
    {
        using var w = new StockWorld();
        var (_, asgId) = SeedVisit(w, null, new DateTime(2026, 11, 5));
        using var db = w.NewContext();
        await Ctl(db).DeleteAssignment(asgId, w.DoctorId);
        using var check = w.NewContext();
        Assert.Contains(check.Notifications, n => n.Type == "PaAssignmentRemoved" && n.RecipientType == "PA" && n.RecipientId == 17);
    }
}
