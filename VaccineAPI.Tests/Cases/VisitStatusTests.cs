using VaccineAPI.Services;
using Xunit;
using static VaccineAPI.Services.VisitStatus;

namespace VaccineAPI.Tests.Cases;

/// <summary>Route ordering and derived visit state (PA_DAILY_ROUTE_SPEC.md v3.2 sections 3 and 4).</summary>
public class VisitStatusTests
{
    private static readonly DateTime Today = new DateTime(2026, 11, 11);
    private static readonly DateTime Assigned = new DateTime(2026, 11, 8, 5, 0, 0);

    private static Stop S(long id, string? start = "10:00", int mins = 60, int? order = null, long pa = 1,
                          DateTime? date = null, bool paid = false, decimal amount = 500,
                          bool refusal = false, DateTime? explicitAt = null)
        => new Stop
        {
            Id = id, PersonalAssistantId = pa, TargetDate = date ?? Today,
            WindowStart = start == null ? (TimeSpan?)null : TimeSpan.Parse(start), WindowMinutes = mins, VisitOrder = order,
            AssignedAt = Assigned, RefusalPending = refusal, VisitCompletedAt = explicitAt,
            Doses = new List<(decimal, bool)> { (amount, paid) }
        };

    private static Result R(IEnumerable<Stop> stops, int minute, long id) => Compute(stops, Today, minute).Single(x => x.Id == id);

    [Fact]
    public void Rank_is_time_then_order_then_id_with_TBD_last()
    {
        var stops = new[] { S(1, null), S(2, "11:00"), S(3, "09:00", order: 5), S(4, "09:00", order: 2), S(5, "09:00", order: 2) };
        var r = Compute(stops, Today, 0).ToDictionary(x => x.Id, x => x.StopNumber);
        Assert.Equal(new[] { 4L, 5L, 3L, 2L, 1L }, r.OrderBy(k => k.Value).Select(k => k.Key));
    }

    [Fact]
    public void Ranks_are_per_PA_and_per_date()
    {
        var stops = new[] { S(1, pa: 1), S(2, pa: 2), S(3, pa: 1, date: Today.AddDays(1)) };
        Assert.All(Compute(stops, Today, 0), x => Assert.Equal(1, x.StopNumber));
    }

    [Fact]
    public void Time_states_queued_next_in_late()
    {
        var stops = new[] { S(1, "10:00"), S(2, "12:00") };
        Assert.Equal(Next, R(stops, 9 * 60, 1).State);
        Assert.Equal(Queued, R(stops, 9 * 60, 2).State);
        Assert.Equal(In, R(stops, 10 * 60 + 30, 1).State);
        Assert.Equal(Late, R(stops, 11 * 60, 1).State);
        Assert.Equal(Next, R(stops, 11 * 60, 2).State);
    }

    [Fact]
    public void Past_day_open_is_late_and_future_day_is_queued()
    {
        Assert.Equal(Late, R(new[] { S(1, date: Today.AddDays(-1)) }, 0, 1).State);
        Assert.Equal(Queued, R(new[] { S(1, date: Today.AddDays(1)) }, 600, 1).State);
    }

    [Fact]
    public void Time_TBD_today_is_queued_and_NeedsTime()
    {
        var r = R(new[] { S(1, null) }, 600, 1);
        Assert.True(r.NeedsTime);
        Assert.NotEqual(Late, r.State);
    }

    private static readonly DateTime Tapped = Assigned.AddDays(3);

    [Fact]
    public void Paid_and_tapped_completes_with_the_tap_time()
    {
        var r = R(new[] { S(1, paid: true, explicitAt: Tapped) }, 0, 1);
        Assert.Equal(Completed, r.State);
        Assert.Equal(Tapped, r.CompletedAt);
    }

    [Fact]
    public void Paid_without_the_tap_is_PaidPending_not_Completed()
        => Assert.Equal(PaidPending, R(new[] { S(1, paid: true) }, 0, 1).State);

    [Fact]
    public void Tap_without_payment_does_not_complete()
        => Assert.NotEqual(Completed, R(new[] { S(1, paid: false, explicitAt: Tapped) }, 0, 1).State);

    [Fact]
    public void Partial_payment_is_not_completed()
    {
        var s = S(1, paid: true, explicitAt: Tapped);
        s.Doses = new List<(decimal, bool)> { (500, true), (300, false) };
        Assert.NotEqual(Completed, R(new[] { s }, 0, 1).State);
    }

    [Fact]
    public void Free_doses_are_ignored_when_billable_ones_are_paid()
    {
        var s = S(1, paid: true, explicitAt: Tapped);
        s.Doses = new List<(decimal, bool)> { (500, true), (0, false) };
        Assert.Equal(Completed, R(new[] { s }, 0, 1).State);
    }

    [Fact]
    public void Clearing_payment_reverts_completion()
    {
        var s = S(1, paid: true, explicitAt: Tapped);
        Assert.Equal(Completed, R(new[] { s }, 0, 1).State);
        s.Doses = new List<(decimal, bool)> { (500, false) };
        Assert.NotEqual(Completed, R(new[] { s }, 0, 1).State);
    }

    [Fact]
    public void Manager_view_sees_only_the_tap()
    {
        var s = S(1, paid: false, explicitAt: Tapped);
        s.PaymentHidden = true;
        s.Doses = new List<(decimal, bool)>();
        Assert.Equal(Completed, R(new[] { s }, 0, 1).State);
        s.VisitCompletedAt = null;
        Assert.NotEqual(Completed, R(new[] { s }, 0, 1).State);
    }

    [Fact]
    public void Pending_refusal_shows_badge_and_is_skipped_as_Next()
    {
        var stops = new[] { S(1, "09:00", refusal: true), S(2, "10:00") };
        Assert.Equal(RefusalRequested, R(stops, 8 * 60, 1).State);
        Assert.Equal(Next, R(stops, 8 * 60, 2).State);
    }

    [Theory]
    [InlineData("09:00", 60, true)]
    [InlineData("09:00", 15, true)]
    [InlineData("09:00", 240, true)]
    [InlineData("09:00", 10, false)]
    [InlineData("09:00", 245, false)]
    [InlineData("09:00", 62, false)]
    [InlineData("23:30", 60, false)]
    [InlineData("23:00", 60, true)]
    public void Window_validation(string start, int mins, bool ok)
        => Assert.Equal(ok, WindowValid(TimeSpan.Parse(start), mins));

    private static string? V(bool scheduled = true, DateTime? date = null, string? start = "10:00", int? mins = 60, string? area = "I-8", bool isNew = true)
        => Validate(scheduled, date ?? Today, start, mins, area, isNew, Today, out _, out _, out _);

    [Fact]
    public void Validate_accepts_a_normal_visit_and_a_blank_window()
    {
        Assert.Null(V());
        Assert.Null(V(start: null));
        Assert.Null(V(scheduled: false, start: null, area: null));
    }

    [Fact]
    public void Validate_rejects_window_or_area_without_date_or_without_scheduled_flag()
    {
        Assert.NotNull(Validate(true, null, "10:00", 60, null, true, Today, out _, out _, out _));
        Assert.NotNull(Validate(true, null, null, null, "I-8", true, Today, out _, out _, out _));
        Assert.NotNull(V(scheduled: false));
    }

    [Fact]
    public void Validate_rejects_bad_time_midnight_cross_and_past_date_for_new_only()
    {
        Assert.NotNull(V(start: "25:00"));
        Assert.NotNull(V(start: "9:00"));
        Assert.NotNull(V(start: "23:30", mins: 60));
        Assert.NotNull(V(date: Today.AddDays(-1)));
        Assert.Null(V(date: Today.AddDays(-1), isNew: false));
        Assert.NotNull(V(area: new string('x', 81)));
    }

    [Fact]
    public void Validate_trims_area()
    {
        Validate(true, Today, "10:00", 60, "  I-8/2 ", true, Today, out var ws, out var wm, out var area);
        Assert.Equal("I-8/2", area);
        Assert.Equal(TimeSpan.FromHours(10), ws);
        Assert.Equal(60, wm);
    }
}
