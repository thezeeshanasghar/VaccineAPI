using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    // Assistant live location. Doctor 1 only; the PA must hold TrackLocation and be on a shift.
    // All outgoing times are DateTimeOffset in Pakistan time (+05:00); DB values stay UTC.
    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "SUPERADMIN")]
    public class PaLocationController : ControllerBase
    {
        private const long TrackingDoctorId = 1;
        private const int RetentionDays = 35;           // must stay >= the 31-day km range
        private const int MaxRangeDays = 31;
        private const int StaleNoFixHours = 3;
        private const int MaxShiftHours = 14;
        private const int OnShiftFreshSeconds = 300;
        private const int LateFixGraceMinutes = 10;
        private const int EarlyFixSlackMinutes = 2;
        private const int MaxFixAgeHours = 12;
        private const int MaxTrailPoints = 3000;
        private static readonly TimeSpan PktOffset = Pkt.Offset;
        private static readonly object PurgeLock = new object();
        private static DateTime _lastPurgeUtc = DateTime.MinValue;
        private readonly Context _db;

        public PaLocationController(Context db) { _db = db; }

        public class PaIdDto { public long PaId { get; set; } }

        public class PingDto
        {
            public long PaId { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public double? Accuracy { get; set; }
            public int? Battery { get; set; }
            public DateTime? RecordedAt { get; set; }
        }

        private bool TrackingAllowed(PersonalAssistant? pa) =>
            pa != null && pa.IsActive && pa.DoctorId == TrackingDoctorId
            && _db.PaPermissions.Any(p => p.PaId == pa.Id && p.TrackLocation);

        // ---- housekeeping -------------------------------------------------------------------------

        // Close shifts the app never ended (phone died, app killed): no fix for >= 3h, or longer than 14h.
        // EndedAt = last fix time (StartedAt if there was none), never "now".
        // Also guarantees a single open shift per PA (older duplicates are closed at their last fix).
        public void CloseStaleShifts(ICollection<long> paIds)
        {
            if (paIds == null || paIds.Count == 0) return;
            var now = DateTime.UtcNow;
            var open = _db.PaShifts.Where(s => paIds.Contains(s.PaId) && s.EndedAt == null).ToList();
            if (open.Count == 0) return;
            var ids = open.Select(s => s.Id).ToList();
            var lastFixRows = _db.PaLocations.Where(l => ids.Contains(l.ShiftId))
                .GroupBy(l => l.ShiftId).Select(g => new { ShiftId = g.Key, At = g.Max(l => l.RecordedAt), Rcv = g.Max(l => l.ReceivedAt) })
                .ToList();
            var lastFix = lastFixRows.ToDictionary(x => x.ShiftId, x => x.At);
            var lastReceived = lastFixRows.ToDictionary(x => x.ShiftId, x => x.Rcv);

            var newestPerPa = open.GroupBy(s => s.PaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id).First().Id);
            var changed = false;
            foreach (var s in open)
            {
                DateTime? last = lastFix.TryGetValue(s.Id, out var l) ? l : (DateTime?)null;
                var ref0 = last ?? s.StartedAt;
                // "no fix for 3h" is judged on when the server last HEARD from the phone as well as on the fix time:
                // a phone that was offline and is now uploading its backlog (old RecordedAt, fresh ReceivedAt) is alive.
                var heard = lastReceived.TryGetValue(s.Id, out var rc) && rc > ref0 ? rc : ref0;
                var stale = (now - heard).TotalHours >= StaleNoFixHours || (now - s.StartedAt).TotalHours > MaxShiftHours;
                var duplicate = newestPerPa[s.PaId] != s.Id;
                if (stale || duplicate)
                {
                    var end = ref0 < s.StartedAt ? s.StartedAt : ref0;
                    s.EndedAt = end;
                    changed = true;
                }
            }
            if (changed) _db.SaveChanges();
        }

        // Retention purge, at most once an hour per process, cut at a PKT day boundary, bulk deletes.
        public void PurgeOldThrottled()
        {
            lock (PurgeLock)
            {
                if ((DateTime.UtcNow - _lastPurgeUtc).TotalMinutes < 60) return;
                _lastPurgeUtc = DateTime.UtcNow;
            }
            var cutoff = Pkt.TodayStartUtc().AddDays(-RetentionDays);
            try
            {
                _db.PaLocations.Where(l => l.RecordedAt < cutoff).ExecuteDelete();
                _db.PaShifts.Where(s => s.EndedAt != null && s.StartedAt < cutoff).ExecuteDelete();
            }
            catch (Exception) { lock (PurgeLock) { _lastPurgeUtc = DateTime.MinValue; } }
        }

        // ---- shift lifecycle -----------------------------------------------------------------------

        [RolesOnly("PA", "SUPERADMIN")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpGet("shift/status/{paId:long}")]
        public ActionResult Status(long paId)
        {
            var pa = _db.PersonalAssistant.Find(paId);
            var allowed = TrackingAllowed(pa);
            if (allowed) CloseStaleShifts(new List<long> { paId });
            var open = allowed ? _db.PaShifts.Where(s => s.PaId == paId && s.EndedAt == null).OrderByDescending(s => s.StartedAt).FirstOrDefault() : null;
            return Ok(new { trackingAllowed = allowed, onShift = open != null, startedAt = Pkt.ToPkt(open?.StartedAt), serverNowPkt = Pkt.Now() });
        }

        [RolesOnly("PA", "SUPERADMIN")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpPost("shift/start")]
        public ActionResult Start([FromBody] PaIdDto dto)
        {
            var pa = _db.PersonalAssistant.Find(dto.PaId);
            if (!TrackingAllowed(pa))
                return StatusCode(403, new { message = "Location sharing is not enabled for this assistant." });

            var now = DateTime.UtcNow;
            // Any older open shift is closed at its last fix (not "now"): the gap between is not work time.
            var open = _db.PaShifts.Where(s => s.PaId == dto.PaId && s.EndedAt == null).ToList();
            if (open.Count > 0)
            {
                var ids = open.Select(s => s.Id).ToList();
                var lastFix = _db.PaLocations.Where(l => ids.Contains(l.ShiftId))
                    .GroupBy(l => l.ShiftId).Select(g => new { ShiftId = g.Key, At = g.Max(l => l.RecordedAt) })
                    .ToList().ToDictionary(x => x.ShiftId, x => x.At);
                foreach (var s in open)
                {
                    var end = lastFix.TryGetValue(s.Id, out var l) ? l : s.StartedAt;
                    s.EndedAt = end < s.StartedAt ? s.StartedAt : end;
                }
                // committed on its own so the INSERT below can never run before the close (open-shift unique index)
                _db.SaveChanges();
            }

            var shift = new PaShift { PaId = dto.PaId, DoctorId = pa!.DoctorId, StartedAt = now };
            _db.PaShifts.Add(shift);
            try { _db.SaveChanges(); }
            catch (DbUpdateException)
            {   // only reachable with the optional one-open-shift unique index: a parallel Start won
                return StatusCode(409, new { message = "A shift is already open." });
            }
            PurgeOldThrottled();
            return Ok(new { shiftId = shift.Id, startedAt = Pkt.ToPkt(shift.StartedAt), serverNowPkt = Pkt.Now() });
        }

        [RolesOnly("PA", "SUPERADMIN")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpPost("shift/end")]
        public ActionResult End([FromBody] PaIdDto dto)
        {
            var now = DateTime.UtcNow;
            foreach (var s in _db.PaShifts.Where(s => s.PaId == dto.PaId && s.EndedAt == null))
                s.EndedAt = now;
            _db.SaveChanges();
            return Ok(new { message = "Shift ended." });
        }

        // The app posts a fix about once a minute. Rejected (409) once the shift is over so the
        // app knows to stop its background service. A late (queued) fix that belongs to the most recent
        // shift, within 10 min of its end, is still accepted.
        [RolesOnly("PA", "SUPERADMIN")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpPost("ping")]
        public ActionResult Ping([FromBody] PingDto dto)
        {
            if (dto == null || !double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)
                || Math.Abs(dto.Latitude) > 90 || Math.Abs(dto.Longitude) > 180
                || (dto.Latitude == 0 && dto.Longitude == 0))
                return BadRequest(new { message = "Invalid location." });
            if (dto.Accuracy.HasValue && (!double.IsFinite(dto.Accuracy.Value) || dto.Accuracy.Value < 0 || dto.Accuracy.Value > 10000))
                return BadRequest(new { message = "Invalid accuracy." });

            var pa = _db.PersonalAssistant.Find(dto.PaId);
            if (!TrackingAllowed(pa))
                return StatusCode(403, new { message = "Location sharing is not enabled for this assistant." });

            var now = DateTime.UtcNow;
            var recorded = dto.RecordedAt.HasValue ? Pkt.ToUtc(dto.RecordedAt.Value) : now;
            if (recorded > now) recorded = now;
            recorded = new DateTime(recorded.Ticks - recorded.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            if ((now - recorded).TotalHours > MaxFixAgeHours)
                return BadRequest(new { message = "Fix is too old." });

            // No stale-closing here: a phone that was offline for hours uploads its backlog into the still-open
            // shift (KmCalc flags the silence). Abandoned shifts are closed by Live/Km/TrailV2/Status/Start.
            var shift = _db.PaShifts.Where(s => s.PaId == dto.PaId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt).FirstOrDefault();
            if (shift == null)
            {
                var latest = _db.PaShifts.Where(s => s.PaId == dto.PaId)
                    .OrderByDescending(s => s.StartedAt).FirstOrDefault();
                if (latest != null && latest.EndedAt != null
                    && recorded >= latest.StartedAt.AddMinutes(-EarlyFixSlackMinutes)
                    && recorded <= latest.EndedAt.Value.AddMinutes(LateFixGraceMinutes))
                    shift = latest;
            }
            if (shift == null)
                return StatusCode(409, new { message = "No active shift." });
            if (recorded < shift.StartedAt.AddMinutes(-EarlyFixSlackMinutes))
                return BadRequest(new { message = "Fix predates the shift." });

            if (_db.PaLocations.Any(l => l.PaId == dto.PaId && l.RecordedAt == recorded))
                return Ok(new { message = "ok" });   // duplicate (app retry): already stored

            _db.PaLocations.Add(new PaLocation
            {
                ShiftId = shift.Id,
                PaId = dto.PaId,
                DoctorId = pa!.DoctorId,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                Accuracy = dto.Accuracy,
                Battery = dto.Battery.HasValue && dto.Battery.Value >= 0 && dto.Battery.Value <= 100 ? dto.Battery : null,
                RecordedAt = recorded,
                ReceivedAt = now
            });
            try { _db.SaveChanges(); }
            catch (DbUpdateException) { /* unique (PaId, RecordedAt): a parallel duplicate already won */ }
            return Ok(new { message = "ok" });
        }

        // Doctor map: every tracked PA with server-derived status + latest fix.
        // Default response is the bare array (each element carries serverNowPkt); ?v=2 returns { serverNowPkt, assistants:[...] }.
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [HttpGet("doctor/{doctorId:long}/live")]
        public ActionResult Live(long doctorId, [FromQuery] int v = 0)
        {
            var nowUtc = DateTime.UtcNow;
            var nowPkt = Pkt.Now();
            if (doctorId != TrackingDoctorId)
                return Ok(v == 2 ? (object)new { serverNowPkt = nowPkt, assistants = new object[0] } : new object[0]);

            var pas = (from p in _db.PersonalAssistant
                       join perm in _db.PaPermissions on p.Id equals perm.PaId
                       where p.DoctorId == doctorId && p.IsActive && perm.TrackLocation
                       select new { p.Id, p.Name, p.ProfileImage }).ToList();
            var paIds = pas.Select(p => p.Id).ToList();
            CloseStaleShifts(paIds);

            // latest shift per PA (open one if any, else most recent)
            var shiftRows = _db.PaShifts.Where(s => paIds.Contains(s.PaId))
                .OrderByDescending(s => s.StartedAt).Select(s => new { s.Id, s.PaId, s.StartedAt, s.EndedAt }).ToList();
            var latestShift = shiftRows.GroupBy(s => s.PaId)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault(s => s.EndedAt == null) ?? g.First());

            // grouped last-fix queries (no per-PA round trips)
            var curShiftIds = latestShift.Values.Where(s => s.EndedAt == null).Select(s => s.Id).ToList();
            var maxByShift = _db.PaLocations.Where(l => curShiftIds.Contains(l.ShiftId))
                .GroupBy(l => l.ShiftId).Select(g => new { g.Key, At = g.Max(l => l.RecordedAt) }).ToList();
            var maxByPa = _db.PaLocations.Where(l => paIds.Contains(l.PaId))
                .GroupBy(l => l.PaId).Select(g => new { g.Key, At = g.Max(l => l.RecordedAt) }).ToList();
            var wanted = maxByShift.Select(x => x.At).Concat(maxByPa.Select(x => x.At)).Distinct().ToList();
            var rows = wanted.Count == 0 ? new List<PaLocation>()
                : _db.PaLocations.Where(l => paIds.Contains(l.PaId) && wanted.Contains(l.RecordedAt)).ToList();

            var result = new List<object>();
            foreach (var p in pas)
            {
                latestShift.TryGetValue(p.Id, out var shift);
                var isOpen = shift != null && shift.EndedAt == null;
                PaLocation? fix = null;
                var lastKnown = true;
                if (isOpen)
                {
                    var m = maxByShift.FirstOrDefault(x => x.Key == shift!.Id);
                    if (m != null) fix = rows.FirstOrDefault(r => r.ShiftId == shift!.Id && r.RecordedAt == m.At);
                    if (fix != null) lastKnown = false;
                }
                if (fix == null)
                {
                    var m = maxByPa.FirstOrDefault(x => x.Key == p.Id);
                    if (m != null) fix = rows.FirstOrDefault(r => r.PaId == p.Id && r.RecordedAt == m.At);
                    lastKnown = true;
                }

                double? age = fix != null ? Math.Max(0, Math.Round((nowUtc - fix.RecordedAt).TotalSeconds)) : (double?)null;
                string status = !isOpen ? "off"
                    : (!lastKnown && age.HasValue && age.Value <= OnShiftFreshSeconds ? "onshift" : "nosignal");

                result.Add(new
                {
                    paId = p.Id,
                    name = p.Name,
                    profileImage = p.ProfileImage,
                    onShift = isOpen,
                    status,
                    shiftStartedAt = Pkt.ToPkt(shift?.StartedAt),
                    shiftEndedAt = Pkt.ToPkt(shift?.EndedAt),
                    latitude = fix?.Latitude,
                    longitude = fix?.Longitude,
                    accuracy = fix?.Accuracy,
                    battery = fix?.Battery,
                    lastSeenAt = Pkt.ToPkt(fix?.RecordedAt),
                    ageSeconds = age.HasValue ? (long?)age.Value : null,
                    lastKnown = fix != null && lastKnown,
                    serverNowPkt = nowPkt
                });
            }
            if (v != 2) return Ok(result);
            return Ok(new { serverNowPkt = nowPkt, assistants = result });
        }

        // KM per assistant per Pakistan day (from/to = yyyy-MM-dd, max 31 days). The distance maths is
        // KmCalc (shared with the trail). Each PKT day is computed independently on that day's fixes, so a
        // day's km here always equals the trail for that day. Shifts are split at PKT midnight so km,
        // shifts and hours line up per day.
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [HttpGet("doctor/{doctorId:long}/km")]
        public ActionResult Km(long doctorId, [FromQuery] string? from, [FromQuery] string? to)
        {
            var serverToday = Pkt.TodayKey();
            if (doctorId != TrackingDoctorId) return Ok(new { serverTodayPkt = serverToday, days = new string[0], assistants = new object[0] });

            var today = DateTime.UtcNow.Add(PktOffset).Date;
            var toDay = today;
            var fromDay = new DateTime(today.Year, today.Month, 1);
            if (!string.IsNullOrEmpty(from) && !Pkt.TryParseDay(from, out fromDay))
                return BadRequest(new { message = "from must be yyyy-MM-dd." });
            if (!string.IsNullOrEmpty(to) && !Pkt.TryParseDay(to, out toDay))
                return BadRequest(new { message = "to must be yyyy-MM-dd." });
            if (toDay < fromDay) return BadRequest(new { message = "Invalid range." });
            if ((toDay - fromDay).TotalDays > MaxRangeDays - 1) return BadRequest(new { message = "Range is limited to 31 days." });

            PurgeOldThrottled();

            var fromUtc = Pkt.DayStartUtc(fromDay);
            var toUtc = Pkt.DayStartUtc(toDay).AddDays(1);

            var pas = (from p in _db.PersonalAssistant
                       join perm in _db.PaPermissions on p.Id equals perm.PaId
                       where p.DoctorId == doctorId && p.IsActive && perm.TrackLocation
                       select new { p.Id, p.Name }).ToList();
            var paIds = pas.Select(p => p.Id).ToList();
            CloseStaleShifts(paIds);

            var fixes = _db.PaLocations
                .Where(l => l.DoctorId == doctorId && paIds.Contains(l.PaId) && l.RecordedAt >= fromUtc && l.RecordedAt < toUtc)
                .OrderBy(l => l.RecordedAt)
                .Select(l => new { l.PaId, l.ShiftId, l.Latitude, l.Longitude, l.Accuracy, l.RecordedAt })
                .ToList();
            var shifts = _db.PaShifts
                .Where(s => s.DoctorId == doctorId && paIds.Contains(s.PaId) && s.StartedAt < toUtc && (s.EndedAt == null || s.EndedAt >= fromUtc))
                .ToList();

            var days = new List<string>();
            for (var d = fromDay; d <= toDay; d = d.AddDays(1)) days.Add(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            var nowUtc = DateTime.UtcNow;

            var assistants = new List<object>();
            foreach (var pa in pas)
            {
                var myFixes = fixes.Where(x => x.PaId == pa.Id).GroupBy(x => Pkt.DayKey(x.RecordedAt))
                    .ToDictionary(g => g.Key, g => g.ToList());

                // shifts split at PKT midnight: per day -> count, seconds
                var shiftCount = days.ToDictionary(d => d, d => 0);
                var shiftSecs = days.ToDictionary(d => d, d => 0.0);
                foreach (var s in shifts.Where(s => s.PaId == pa.Id))
                {
                    var sStart = Pkt.ToUtc(s.StartedAt);
                    var sEnd = Pkt.ToUtc(s.EndedAt ?? nowUtc);
                    if (sEnd < sStart) sEnd = sStart;
                    var dayCursor = sStart.Add(PktOffset).Date;
                    var lastDay = sEnd.Add(PktOffset).Date;
                    for (; dayCursor <= lastDay; dayCursor = dayCursor.AddDays(1))
                    {
                        var key = dayCursor.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        if (!shiftCount.ContainsKey(key)) continue;
                        var ds = Pkt.DayStartUtc(dayCursor); var de = ds.AddDays(1);
                        var a = sStart > ds ? sStart : ds;
                        var b = sEnd < de ? sEnd : de;
                        if (b < a) continue;
                        if (b == a && !(dayCursor == sStart.Add(PktOffset).Date)) continue;  // empty tail piece
                        shiftCount[key]++;
                        shiftSecs[key] += (b - a).TotalSeconds;
                    }
                }

                var cells = new List<(string date, double km, int shifts, double hours, int fixes, int skipped, bool gaps, double coverage, bool noGps)>();
                foreach (var d in days)
                {
                    double km = 0; int used = 0, skipped = 0; bool gaps = false; double covered = 0;
                    if (myFixes.TryGetValue(d, out var list))
                    {
                        var r = KmCalc.Compute(list.Select(x => new KmFix { ShiftId = x.ShiftId, Lat = x.Latitude, Lon = x.Longitude, Accuracy = x.Accuracy, At = x.RecordedAt }).ToList());
                        km = r.Km; used = r.Used; skipped = r.Skipped; gaps = r.Gaps; covered = r.CoveredSeconds;
                    }
                    var cov = shiftSecs[d] > 0 ? Math.Min(1.0, covered / shiftSecs[d]) : 0.0;
                    cells.Add((d, km, shiftCount[d], shiftSecs[d] / 3600.0, used, skipped, gaps, cov, shiftCount[d] > 0 && used == 0));
                }

                var daysWithKm = cells.Count(c => c.km > 0);
                var totalKm = cells.Sum(c => c.km);
                var outCells = cells.Select(c => new
                {
                    date = c.date,
                    km = Math.Round(c.km, 1),
                    shifts = c.shifts,
                    hours = Math.Round(c.hours, 1),
                    fixes = c.fixes,
                    skipped = c.skipped,
                    gaps = c.gaps,
                    coverage = Math.Round(c.coverage, 2),
                    noGps = c.noGps
                }).ToList();
                assistants.Add(new
                {
                    paId = pa.Id,
                    name = pa.Name,
                    totalKm = Math.Round(totalKm, 1),
                    shiftDays = cells.Count(c => c.shifts > 0),
                    avgKm = daysWithKm > 0 ? Math.Round(totalKm / daysWithKm, 1) : 0,
                    totalShifts = cells.Sum(c => c.shifts),
                    totalHours = Math.Round(cells.Sum(c => c.hours), 1),
                    cells = outCells
                });
            }
            return Ok(new { serverTodayPkt = serverToday, days, assistants });
        }

        // Trail v2: one PA, one PKT day. km comes from the same KmCalc as the km report; each point says
        // whether the calc used it. Old Trail below stays unchanged for old clients.
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpGet("doctor/{doctorId:long}/trailv2")]
        public ActionResult GetTrailV2(long doctorId, [FromQuery] long paId, [FromQuery] string? date)
        {
            if (doctorId != TrackingDoctorId) return StatusCode(403, new { message = "This assistant is not tracked." });
            if (string.IsNullOrEmpty(date) || !Pkt.TryParseDay(date, out var day))
                return BadRequest(new { message = "date must be yyyy-MM-dd." });
            var pa = _db.PersonalAssistant.Find(paId);
            if (pa == null || pa.DoctorId != doctorId || !TrackingAllowed(pa))
                return StatusCode(403, new { message = "This assistant is not tracked." });

            CloseStaleShifts(new List<long> { paId });
            var (fromUtc, toUtc) = Pkt.DayWindowUtc(day);
            var rows = _db.PaLocations
                .Where(l => l.DoctorId == doctorId && l.PaId == paId && l.RecordedAt >= fromUtc && l.RecordedAt < toUtc)
                .OrderBy(l => l.RecordedAt).Take(20000)
                .Select(l => new { l.ShiftId, l.Latitude, l.Longitude, l.Accuracy, l.RecordedAt })
                .ToList();
            var r = KmCalc.Compute(rows.Select(x => new KmFix { ShiftId = x.ShiftId, Lat = x.Latitude, Lon = x.Longitude, Accuracy = x.Accuracy, At = x.RecordedAt }).ToList());

            var idx = Enumerable.Range(0, rows.Count).ToList();
            var truncated = idx.Count > MaxTrailPoints;
            if (truncated)
            {   // keep an even spread, always including the first and last fix
                var step = (double)(idx.Count - 1) / (MaxTrailPoints - 1);
                idx = Enumerable.Range(0, MaxTrailPoints).Select(i => (int)Math.Round(i * step)).Distinct().ToList();
            }
            var points = idx.Select(i => new
            {
                lat = rows[i].Latitude,
                lng = rows[i].Longitude,
                recordedAt = Pkt.ToPkt(rows[i].RecordedAt),
                accuracy = rows[i].Accuracy,
                shiftId = rows[i].ShiftId,
                usable = r.Usable[i]
            }).ToList();
            return Ok(new
            {
                serverNowPkt = Pkt.Now(),
                km = Math.Round(r.Km, 1),
                gaps = r.Gaps,
                skipped = r.Skipped,
                truncated,
                points
            });
        }

        // Trail for one PA on one Pakistan-time calendar day (date = yyyy-MM-dd, default today).
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpGet("doctor/{doctorId:long}/trail")]
        public ActionResult Trail(long doctorId, [FromQuery] long paId, [FromQuery] string? date)
        {
            if (doctorId != TrackingDoctorId) return Ok(new object[0]);

            var localDay = DateTime.UtcNow.Add(PktOffset).Date;
            if (!string.IsNullOrEmpty(date))
            {
                if (!Pkt.TryParseDay(date, out var d)) return BadRequest(new { message = "date must be yyyy-MM-dd." });
                localDay = d.Date;
            }
            var fromUtc = localDay - PktOffset;
            var toUtc = fromUtc.AddDays(1);

            var points = _db.PaLocations
                .Where(l => l.DoctorId == doctorId && l.PaId == paId && l.RecordedAt >= fromUtc && l.RecordedAt < toUtc)
                .OrderBy(l => l.RecordedAt)
                .Select(l => new { l.Latitude, l.Longitude, l.Accuracy, l.Battery, l.RecordedAt })
                .ToList();
            return Ok(points);
        }
    }
}
