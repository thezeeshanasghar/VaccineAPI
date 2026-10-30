using System;
using System.Collections.Generic;
using System.Linq;

namespace VaccineAPI.Services
{
    // PA Daily Route (spec v3.1 sections 3 and 4). Pure and database-free: ranks a PA's stops for a
    // date and derives each stop's state. Vaccine status / AssignmentStage never feed this.
    //   completed       v3.2: the PA (or doctor for the PA) tapped "Visit completed" (VisitCompletedAt) AND all
    //                   billable pinned doses are still paid. Payment alone never completes a visit.
    //   paidPending     all billable paid but the visit was not tapped completed yet
    //   refusalRequested a refusal is pending; skipped as Next
    //   late            window ended (or past day) and not completed. UI wording: "no payment recorded yet"
    //   in / next / queued  time based, against the server PKT clock
    public static class VisitStatus
    {
        public const string Queued = "queued", Next = "next", In = "in", Late = "late",
                            Completed = "completed", PaidPending = "paidPending", RefusalRequested = "refusalRequested";

        public class Stop
        {
            public long Id { get; set; }
            public long PersonalAssistantId { get; set; }
            public DateTime? TargetDate { get; set; }
            public TimeSpan? WindowStart { get; set; }
            public int WindowMinutes { get; set; } = 60;
            public int? VisitOrder { get; set; }
            public DateTime AssignedAt { get; set; }
            public DateTime? VisitCompletedAt { get; set; }
            public bool RefusalPending { get; set; }
            // Amount and IsPaymentCollected of each pinned dose.
            public IList<(decimal Amount, bool Paid)> Doses { get; set; } = new List<(decimal, bool)>();
            // Manager view: payment detail is hidden, so completion is just whether the visit was tapped completed.
            public bool PaymentHidden { get; set; }
        }

        public class Result
        {
            public long Id { get; set; }
            public int StopNumber { get; set; }
            public string State { get; set; } = Queued;
            public DateTime? CompletedAt { get; set; }
            public bool NeedsTime { get; set; }
        }

        public static bool WindowValid(TimeSpan start, int minutes)
            => minutes >= 15 && minutes <= 240 && minutes % 5 == 0
               && start >= TimeSpan.Zero && start < TimeSpan.FromHours(24)
               && start.Add(TimeSpan.FromMinutes(minutes)) <= TimeSpan.FromHours(24);


        public static bool TryParseHm(string? hm, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(hm) || hm.Length != 5 || hm[2] != ':') return false;
            if (!int.TryParse(hm.Substring(0, 2), out var h) || !int.TryParse(hm.Substring(3, 2), out var m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            t = new TimeSpan(h, m, 0);
            return true;
        }

        public static string FormatHm(TimeSpan? t) => t.HasValue ? t.Value.ToString(@"hh\:mm") : null;

        // R3. Returns an error message, or null when the input is acceptable. A blank windowStart
        // clears the window ("time TBD"). pktToday is passed in so the rule stays testable.
        public static string? Validate(bool scheduled, DateTime? targetDate, string? windowStart, int? windowMinutes,
                                       string? area, bool isNew, DateTime pktToday,
                                       out TimeSpan? ws, out int wm, out string? cleanArea)
        {
            ws = null; wm = 60; cleanArea = string.IsNullOrWhiteSpace(area) ? null : area.Trim();
            var hasWindow = !string.IsNullOrWhiteSpace(windowStart);
            if (!scheduled)
                return (hasWindow || cleanArea != null) ? "A window or area needs a scheduled visit." : null;
            if (!targetDate.HasValue)
                return (hasWindow || cleanArea != null) ? "A window or area needs a date." : null;
            if (isNew && targetDate.Value.Date < pktToday.Date) return "A scheduled visit cannot be on a past date.";
            if (cleanArea != null && cleanArea.Length > 80) return "Area is limited to 80 characters.";
            if (!hasWindow) return null;
            if (!TryParseHm(windowStart, out var t)) return "Window start must be HH:mm.";
            wm = windowMinutes ?? 60;
            if (!WindowValid(t, wm)) return "Window must be 15 to 240 minutes in 5-minute steps and must not cross midnight.";
            ws = t;
            return null;
        }

        public static IComparer<Stop> Order { get; } = Comparer<Stop>.Create((a, b) =>
        {
            var c = (a.WindowStart.HasValue ? 0 : 1).CompareTo(b.WindowStart.HasValue ? 0 : 1);
            if (c != 0) return c;
            if (a.WindowStart.HasValue && b.WindowStart.HasValue)
            {
                c = a.WindowStart.Value.CompareTo(b.WindowStart.Value);
                if (c != 0) return c;
            }
            c = (a.VisitOrder.HasValue ? 0 : 1).CompareTo(b.VisitOrder.HasValue ? 0 : 1);
            if (c != 0) return c;
            if (a.VisitOrder.HasValue && b.VisitOrder.HasValue)
            {
                c = a.VisitOrder.Value.CompareTo(b.VisitOrder.Value);
                if (c != 0) return c;
            }
            return a.Id.CompareTo(b.Id);
        });

        // Returns (completed?, completedAt) for one stop.
        public static (bool Done, DateTime? At, bool PaidPending) Completion(Stop s)
        {
            if (s.PaymentHidden) return (s.VisitCompletedAt.HasValue, s.VisitCompletedAt, false);
            var billable = s.Doses.Where(d => d.Amount > 0).ToList();
            var allPaid = billable.Count > 0 && billable.All(d => d.Paid);
            if (!allPaid) return (false, null, false);
            return s.VisitCompletedAt.HasValue ? (true, s.VisitCompletedAt, false) : (false, null, true);
        }

        // pktToday = server PKT date, pktMinute = minutes since PKT midnight.
        // Stops are ranked per (PA, TargetDate); stops without a date rank together after dated ones.
        public static IList<Result> Compute(IEnumerable<Stop> stops, DateTime pktToday, int pktMinute)
        {
            var results = new List<Result>();
            foreach (var grp in stops.GroupBy(s => new { s.PersonalAssistantId, Date = s.TargetDate?.Date }))
            {
                var ordered = grp.OrderBy(s => s, Order).ToList();
                var nextTaken = false;
                for (var i = 0; i < ordered.Count; i++)
                {
                    var s = ordered[i];
                    var (done, at, paidPending) = Completion(s);
                    var r = new Result { Id = s.Id, StopNumber = i + 1, CompletedAt = at, NeedsTime = s.TargetDate.HasValue && !s.WindowStart.HasValue };

                    if (done) r.State = Completed;
                    else if (s.RefusalPending) r.State = RefusalRequested;
                    else if (paidPending) r.State = PaidPending;
                    else if (s.TargetDate.HasValue && s.TargetDate.Value.Date < pktToday.Date) r.State = Late;
                    else if (s.TargetDate.HasValue && s.TargetDate.Value.Date > pktToday.Date) r.State = Queued;
                    else if (!s.WindowStart.HasValue) r.State = Queued;
                    else
                    {
                        var start = (int)s.WindowStart.Value.TotalMinutes;
                        var end = start + s.WindowMinutes;
                        if (pktMinute >= end) r.State = Late;
                        else if (pktMinute >= start) r.State = In;
                        else r.State = Queued;
                    }

                    // First open queued stop of a PA's today list is "next".
                    if (r.State == Queued && !nextTaken && s.TargetDate.HasValue && s.TargetDate.Value.Date == pktToday.Date)
                    {
                        r.State = Next;
                        nextTaken = true;
                    }
                    results.Add(r);
                }
            }
            return results;
        }
    }
}
