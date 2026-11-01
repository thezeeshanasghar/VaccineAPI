using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Time-based pushes. Wakes every 5 minutes; every push is deduplicated through PushLog so a restart or a
    // second tick never sends the same reminder twice. Scheduled pushes go out 09:00-20:59 Pakistan time only.
    public class PushReminderService : BackgroundService
    {
        private static readonly TimeSpan Pkt = TimeSpan.FromHours(5);
        private const long BrandedDoctorId = 1; // Doctor 1's clients use the Vaccine.pk brand; everyone else is Vaccination Centre.
        private readonly IServiceScopeFactory _scopes;

        public PushReminderService(IServiceScopeFactory scopes) { _scopes = scopes; }

        public static string Brand(long doctorId) => doctorId == BrandedDoctorId ? "Vaccine.pk" : "Vaccination Centre";

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
            while (!ct.IsCancellationRequested)
            {
                if (PushService.Enabled)
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Context>();
                        await RunOnce(db);
                    }
                    catch (Exception e) { Console.Error.WriteLine("[PUSH] reminder tick failed: " + e.Message); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch (TaskCanceledException) { }
            }
        }

        private async Task RunOnce(Context db)
        {
            var nowUtc = DateTime.UtcNow;
            var pkt = nowUtc + Pkt;
            var today = pkt.Date;

            // Event-style jobs (not tied to quiet hours): they follow something that just happened.
            // Each job is isolated: one failing job must not stop the others on this or any later tick.
            await Job(db, "VaccineGiven", () => VaccineGiven(db, nowUtc));
            await Job(db, "AgentFirstDose", () => AgentFirstDose(db, nowUtc));
            await Job(db, "PruneAttempts", () => PruneAttempts(db, nowUtc));
            // Before the quiet-hours gate on purpose: an 08:00 visit is reminded at 07:00.
            await Job(db, "PaVisitReminder", () => PaVisitReminder(db, nowUtc));

            if (pkt.Hour < 9 || pkt.Hour > 20) return;

            await Job(db, "ParentDueReminders", () => ParentDueReminders(db, today));
            await Job(db, "ParentBirthdays", () => ParentBirthdays(db, today));
            await Job(db, "ParentFollowUps", () => ParentFollowUps(db, today));
            await Job(db, "ParentBookingTomorrow", () => ParentBookingTomorrow(db, today));
            if (pkt.Hour >= 9 && pkt.Hour < 12) await Job(db, "PaMorningDigest", () => PaMorningDigest(db, today));
            if (pkt.Hour >= 15 && pkt.Hour < 18) await Job(db, "PaStillOpen", () => PaStillOpen(db, today));
            if (pkt.Day == 1 && pkt.Hour >= 10) await Job(db, "AgentMonthlySummary", () => AgentMonthlySummary(db, today));
        }

        private static async Task Job(Context db, string name, Func<Task> job)
        {
            try { await job(); }
            catch (Exception e)
            {
                db.ChangeTracker.Clear(); // drop half-finished tracked rows so later jobs start clean
                Console.Error.WriteLine($"[PUSH] job {name} failed: {e.Message}");
            }
        }

        private static DateTime _lastPrune = DateTime.MinValue;

        // pushattempt keeps 30 days; checked at most once a day.
        private static async Task PruneAttempts(Context db, DateTime nowUtc)
        {
            if (nowUtc - _lastPrune < TimeSpan.FromHours(24)) return;
            _lastPrune = nowUtc;
            var cutoff = nowUtc.AddDays(-30);
            await db.PushAttempts.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync();
        }

        private static string D(DateTime d) => d.ToString("dd MMM");

        private static Dictionary<string, string> Data(string type, long? childId = null)
        {
            var d = new Dictionary<string, string> { ["type"] = type };
            if (childId.HasValue) d["childId"] = childId.Value.ToString();
            return d;
        }

        // 3 days before, 1 day before and 1 / 7 days after the due date. One push per child per day listing the vaccines.
        private async Task ParentDueReminders(Context db, DateTime today)
        {
            var offsets = new[] { (3, "due3", "Vaccines due in 3 days"), (1, "due1", "Vaccines due tomorrow"),
                                  (-1, "over1", "Vaccines overdue"), (-7, "over7", "Vaccines still overdue") };
            foreach (var (days, key, title) in offsets)
            {
                var day = today.AddDays(days);
                var next = day.AddDays(1);
                var rows = await db.Schedules
                    .Where(s => s.Date >= day && s.Date < next && !s.IsDone && s.IsSkip != true && s.IsDisease != true
                                && s.Child.IsInactive != true)
                    .Select(s => new { s.ChildId, ChildName = s.Child.Name, s.Child.UserId, DoctorId = s.Child.Clinic.DoctorId, Dose = s.Dose.Name })
                    .ToListAsync();
                foreach (var g in rows.GroupBy(r => r.ChildId))
                {
                    var first = g.First();
                    var doses = string.Join(", ", g.Select(x => x.Dose).Distinct().Take(3)) + (g.Select(x => x.Dose).Distinct().Count() > 3 ? "…" : "");
                    var body = days > 0
                        ? $"{first.ChildName}: {doses} due on {D(day)}. Book your visit with {Brand(first.DoctorId)}."
                        : $"{first.ChildName}: {doses} was due on {D(day)}. Please book a visit with {Brand(first.DoctorId)}.";
                    await PushService.SendOnceAsync(db, $"{key}:{g.Key}:{day:yyyyMMdd}", "PARENT", first.UserId, title, body, Data("VaccineDue", g.Key));
                }
            }
        }

        // A dose marked given in the last 3 hours (and more than 5 minutes ago, so an immediate ungive does not notify).
        private async Task VaccineGiven(Context db, DateTime nowUtc)
        {
            var from = nowUtc.AddHours(-3);
            var to = nowUtc.AddMinutes(-5);
            var cutoff = (nowUtc + Pkt).Date.AddDays(-1);
            var rows = await db.Schedules
                .Where(s => s.IsDone && s.DoneAt != null && s.DoneAt >= from && s.DoneAt <= to
                            && s.GivenDate != null && s.GivenDate >= cutoff && s.Child.IsInactive != true)
                .Select(s => new { s.Id, s.ChildId, ChildName = s.Child.Name, s.Child.UserId, DoctorId = s.Child.Clinic.DoctorId, Dose = s.Dose.Name })
                .ToListAsync();
            foreach (var g in rows.GroupBy(r => r.ChildId))
            {
                var first = g.First();
                var tomorrow = (nowUtc + Pkt).Date;
                var nextDue = await db.Schedules
                    .Where(s => s.ChildId == g.Key && !s.IsDone && s.IsSkip != true && s.IsDisease != true && s.Date >= tomorrow)
                    .OrderBy(s => s.Date).Select(s => (DateTime?)s.Date).FirstOrDefaultAsync();
                var doses = string.Join(", ", g.Select(x => x.Dose).Distinct().Take(3));
                var body = $"{first.ChildName} received {doses}." + (nextDue.HasValue ? $" Next vaccine due on {D(nextDue.Value)}." : " All scheduled vaccines are complete.");
                await PushService.SendOnceAsync(db, g.Select(x => $"given:{x.Id}"), "PARENT", first.UserId, "Vaccine given", body, Data("VaccineGiven", g.Key));
            }
        }

        private async Task ParentBirthdays(Context db, DateTime today)
        {
            var kids = await db.Childs
                .Where(c => c.DOB.Month == today.Month && c.DOB.Day == today.Day && c.IsInactive != true)
                .Select(c => new { c.Id, c.Name, c.UserId, DoctorId = c.Clinic.DoctorId })
                .ToListAsync();
            foreach (var c in kids)
                await PushService.SendOnceAsync(db, $"bday:{c.Id}:{today.Year}", "PARENT", c.UserId, $"Happy birthday {c.Name}!",
                    $"Wishing {c.Name} a happy and healthy birthday from {Brand(c.DoctorId)}.", Data("Birthday", c.Id));
        }

        private async Task ParentFollowUps(Context db, DateTime today)
        {
            var tomorrow = today.AddDays(1);
            var rows = await db.FollowUps
                .Where(f => f.NextVisitDate >= tomorrow && f.NextVisitDate < tomorrow.AddDays(1) && f.Child.IsInactive != true)
                .Select(f => new { f.Id, f.ChildId, ChildName = f.Child.Name, f.Child.UserId, DoctorId = f.Child.Clinic.DoctorId })
                .ToListAsync();
            foreach (var f in rows)
                await PushService.SendOnceAsync(db, $"fu1:{f.Id}", "PARENT", f.UserId, "Follow-up visit tomorrow",
                    $"{f.ChildName} has a follow-up visit tomorrow with {Brand(f.DoctorId)}.", Data("FollowUp", f.ChildId));
        }

        private async Task ParentBookingTomorrow(Context db, DateTime today)
        {
            var tomorrow = today.AddDays(1);
            var rows = await db.Bookings
                .Where(b => b.Status == "Confirmed" && b.PreferredDate >= tomorrow && b.PreferredDate < tomorrow.AddDays(1))
                .Select(b => new { b.Id, b.ChildId, b.ChildName, b.ParentUserId, b.DoctorId, b.Type })
                .ToListAsync();
            foreach (var b in rows)
                await PushService.SendOnceAsync(db, $"bk1:{b.Id}", "PARENT", b.ParentUserId, "Your visit is tomorrow",
                    $"{b.ChildName}'s {(b.Type == "HomeBooked" ? "home visit" : "clinic visit")} with {Brand(b.DoctorId)} is booked for tomorrow.", Data("BookingReminder", b.ChildId));
        }

        // True from 60 minutes before the visit window opens until it opens. Both are PKT wall-clock times of day.
        public static bool InReminderWindow(TimeSpan nowOfDay, TimeSpan windowStart)
            => nowOfDay >= windowStart - TimeSpan.FromMinutes(60) && nowOfDay < windowStart;

        public class VisitReminderRow
        {
            public long AssignmentId { get; set; }
            public long PaId { get; set; }
            public long ChildId { get; set; }
            public string ChildName { get; set; } = "";
            public string? Area { get; set; }
            public TimeSpan WindowStart { get; set; }
        }

        // Open scheduled visits for today whose window opens within the next hour. No WindowStart = no reminder.
        public static async Task<List<VisitReminderRow>> VisitReminderCandidates(Context db, DateTime nowUtc)
        {
            var pkt = nowUtc + Pkt;
            var today = pkt.Date;
            var next = today.AddDays(1);
            var rows = await (from a in db.PAAssignments
                              join c in db.Childs on a.ChildId equals c.Id
                              where a.IsScheduledVisit && !a.IsCompleted && !a.IsCancelled && !a.IsCashConfirmedByDoctor
                                    && a.VisitCompletedAt == null && a.AssignmentStatus == "Active"
                                    && a.WindowStart != null && a.TargetDate >= today && a.TargetDate < next
                              select new VisitReminderRow
                              {
                                  AssignmentId = a.Id, PaId = a.PersonalAssistantId, ChildId = a.ChildId,
                                  ChildName = c.Name, Area = a.Area, WindowStart = a.WindowStart!.Value
                              }).ToListAsync();
            return rows.Where(r => InReminderWindow(pkt.TimeOfDay, r.WindowStart)).ToList();
        }

        // One push per assignment about an hour before its visit window. The key carries the date and the time, so
        // moving the visit re-arms it. It does not carry the PA id: a reassign keeps one reminder per visit.
        private async Task PaVisitReminder(Context db, DateTime nowUtc)
        {
            var today = (nowUtc + Pkt).Date;
            foreach (var r in await VisitReminderCandidates(db, nowUtc))
            {
                var hm = new DateTime(1, 1, 1).Add(r.WindowStart).ToString("hh:mm tt");
                var where = string.IsNullOrWhiteSpace(r.Area) ? "" : $" in {r.Area}";
                await PushService.SendOnceAsync(db, $"pa1h:{r.AssignmentId}:{today:yyyyMMdd}:{r.WindowStart:hhmm}", "PA", r.PaId,
                    "Visit in about an hour", $"{r.ChildName}{where} at {hm}. Open Today's Route for the details.",
                    Data("PaVisitReminder", r.ChildId));
            }
        }

        private async Task PaMorningDigest(Context db, DateTime today)
        {
            var next = today.AddDays(1);
            var rows = await db.PAAssignments
                .Where(a => !a.IsCompleted && !a.IsCancelled && a.AssignmentStatus == "Active" && a.TargetDate >= today && a.TargetDate < next)
                .GroupBy(a => a.PersonalAssistantId).Select(g => new { PaId = g.Key, N = g.Select(a => a.ChildId).Distinct().Count() }).ToListAsync();
            foreach (var r in rows)
                await PushService.SendOnceAsync(db, $"padigest:{r.PaId}:{today:yyyyMMdd}", "PA", r.PaId, "Today's visits",
                    $"You have {r.N} vaccination visit{(r.N == 1 ? "" : "s")} assigned for today.", Data("PaDigest"));
        }

        private async Task PaStillOpen(Context db, DateTime today)
        {
            var next = today.AddDays(1);
            var rows = await db.PAAssignments
                .Where(a => !a.IsCompleted && !a.IsCancelled && a.AssignmentStatus == "Active" && a.TargetDate >= today && a.TargetDate < next)
                .GroupBy(a => a.PersonalAssistantId).Select(g => new { PaId = g.Key, N = g.Select(a => a.ChildId).Distinct().Count() }).ToListAsync();
            foreach (var r in rows)
                await PushService.SendOnceAsync(db, $"paopen:{r.PaId}:{today:yyyyMMdd}", "PA", r.PaId, "Visits still open",
                    $"{r.N} assignment{(r.N == 1 ? " is" : "s are")} still open for today.", Data("PaOpen"));
        }

        // Fires once for a referred child when their first qualifying dose (given + invoiced) appears.
        private async Task AgentFirstDose(Context db, DateTime nowUtc)
        {
            var since = (nowUtc + Pkt).Date.AddDays(-3);
            var recent = await db.Schedules
                .Where(s => s.IsDone && s.InvoiceSubmissionId != null && s.GivenDate != null && s.GivenDate >= since && s.Child.AgentId != null)
                .Select(s => new { s.ChildId, ChildName = s.Child.Name, AgentId = s.Child.AgentId!.Value, VaccineId = s.Dose.VaccineId, s.GivenDate })
                .ToListAsync();
            foreach (var g in recent.GroupBy(r => r.ChildId))
            {
                bool earlier = await db.Schedules.AnyAsync(s => s.ChildId == g.Key && s.IsDone && s.InvoiceSubmissionId != null && s.GivenDate < since);
                if (earlier) continue;
                var first = g.OrderBy(x => x.GivenDate).First();
                var agent = await db.Agents.FindAsync((int)first.AgentId);
                if (agent == null) continue;
                var fee = await db.AgentVaccineFeeOverrides.Where(o => o.AgentId == agent.Id && o.VaccineId == first.VaccineId).Select(o => (decimal?)o.Fee).FirstOrDefaultAsync() ?? agent.ReferralFeePerClient;
                await PushService.SendOnceAsync(db, $"agentfirst:{g.Key}", "AGENT", agent.Id, "Referral fee earned",
                    $"{first.ChildName} received their first vaccine. You earned Rs. {fee:N0}.", Data("AgentReferralEarned", g.Key));
            }
        }

        private async Task AgentMonthlySummary(Context db, DateTime today)
        {
            var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            var monthEnd = monthStart.AddMonths(1);
            var agents = await db.Agents.ToListAsync();
            foreach (var agent in agents)
            {
                var key = $"agentmonth:{agent.Id}:{monthStart:yyyyMM}";
                if (await db.PushLogs.AnyAsync(p => p.DedupKey == key)) continue;
                var overrides = await db.AgentVaccineFeeOverrides.Where(o => o.AgentId == agent.Id).ToDictionaryAsync(o => o.VaccineId, o => o.Fee);
                var children = await db.Childs.Where(c => c.AgentId == agent.Id)
                    .Select(c => c.Schedules.Where(s => s.IsDone && s.InvoiceSubmissionId != null && s.GivenDate != null)
                        .OrderBy(s => s.GivenDate).Select(s => new { s.GivenDate, s.Dose.VaccineId }).FirstOrDefault())
                    .ToListAsync();
                var hits = children.Where(f => f != null && f.GivenDate >= monthStart && f.GivenDate < monthEnd).ToList();
                if (hits.Count == 0) continue;
                var total = hits.Sum(f => overrides.TryGetValue(f!.VaccineId, out var o) ? o : agent.ReferralFeePerClient);
                await PushService.SendOnceAsync(db, key, "AGENT", agent.Id, $"{monthStart:MMMM} referral summary",
                    $"{hits.Count} referred client{(hits.Count == 1 ? "" : "s")} vaccinated. Total earned: Rs. {total:N0}.", Data("AgentMonthly"));
            }
        }
    }
}
