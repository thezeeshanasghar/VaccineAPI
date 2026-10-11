using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    // Assistant live location. Doctor 1 only; the PA must hold TrackLocation and be on a shift.
    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "SUPERADMIN")]
    public class PaLocationController : ControllerBase
    {
        private const long TrackingDoctorId = 1;
        private const int RetentionDays = 30;
        private static readonly TimeSpan Pkt = TimeSpan.FromHours(5);
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

        [RolesOnly("PA", "SUPERADMIN")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpGet("shift/status/{paId:long}")]
        public ActionResult Status(long paId)
        {
            var pa = _db.PersonalAssistant.Find(paId);
            var allowed = TrackingAllowed(pa);
            var open = allowed ? _db.PaShifts.FirstOrDefault(s => s.PaId == paId && s.EndedAt == null) : null;
            return Ok(new { trackingAllowed = allowed, onShift = open != null, startedAt = open?.StartedAt });
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
            foreach (var s in _db.PaShifts.Where(s => s.PaId == dto.PaId && s.EndedAt == null))
                s.EndedAt = now;

            // Retention: purge old fixes/shifts whenever a shift starts.
            var cutoff = now.AddDays(-RetentionDays);
            _db.PaLocations.RemoveRange(_db.PaLocations.Where(l => l.RecordedAt < cutoff));
            _db.PaShifts.RemoveRange(_db.PaShifts.Where(s => s.StartedAt < cutoff));

            var shift = new PaShift { PaId = dto.PaId, DoctorId = pa!.DoctorId, StartedAt = now };
            _db.PaShifts.Add(shift);
            _db.SaveChanges();
            return Ok(new { shiftId = shift.Id, startedAt = shift.StartedAt });
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
        // app knows to stop its background service.
        [RolesOnly("PA", "SUPERADMIN")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpPost("ping")]
        public ActionResult Ping([FromBody] PingDto dto)
        {
            if (dto == null || Math.Abs(dto.Latitude) > 90 || Math.Abs(dto.Longitude) > 180)
                return BadRequest(new { message = "Invalid location." });

            var pa = _db.PersonalAssistant.Find(dto.PaId);
            if (!TrackingAllowed(pa))
                return StatusCode(403, new { message = "Location sharing is not enabled for this assistant." });

            var shift = _db.PaShifts.FirstOrDefault(s => s.PaId == dto.PaId && s.EndedAt == null);
            if (shift == null)
                return StatusCode(409, new { message = "No active shift." });

            var now = DateTime.UtcNow;
            var recorded = dto.RecordedAt.HasValue ? dto.RecordedAt.Value.ToUniversalTime() : now;
            if (recorded > now) recorded = now;

            _db.PaLocations.Add(new PaLocation
            {
                ShiftId = shift.Id,
                PaId = dto.PaId,
                DoctorId = pa!.DoctorId,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                Accuracy = dto.Accuracy,
                Battery = dto.Battery,
                RecordedAt = recorded,
                ReceivedAt = now
            });
            _db.SaveChanges();
            return Ok(new { message = "ok" });
        }

        // Doctor map: every tracked PA with status + latest fix.
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [HttpGet("doctor/{doctorId:long}/live")]
        public ActionResult Live(long doctorId)
        {
            if (doctorId != TrackingDoctorId) return Ok(new object[0]);

            var pas = (from p in _db.PersonalAssistant
                       join perm in _db.PaPermissions on p.Id equals perm.PaId
                       where p.DoctorId == doctorId && p.IsActive && perm.TrackLocation
                       select new { p.Id, p.Name, p.ProfileImage }).ToList();

            var result = new List<object>();
            foreach (var p in pas)
            {
                var shift = _db.PaShifts.Where(s => s.PaId == p.Id).OrderByDescending(s => s.StartedAt).FirstOrDefault();
                var last = _db.PaLocations.Where(l => l.PaId == p.Id).OrderByDescending(l => l.RecordedAt).FirstOrDefault();
                result.Add(new
                {
                    paId = p.Id,
                    name = p.Name,
                    profileImage = p.ProfileImage,
                    onShift = shift != null && shift.EndedAt == null,
                    shiftStartedAt = shift?.StartedAt,
                    shiftEndedAt = shift?.EndedAt,
                    latitude = last?.Latitude,
                    longitude = last?.Longitude,
                    accuracy = last?.Accuracy,
                    battery = last?.Battery,
                    lastSeenAt = last?.RecordedAt
                });
            }
            return Ok(result);
        }

        // KM per assistant per Pakistan day (from/to = yyyy-MM-dd, max 31 days). Straight-line distance
        // between consecutive fixes of the same shift; poor-accuracy fixes, jitter and impossible jumps
        // are dropped, and long silences are not bridged (they are flagged as gaps instead).
        private const double MaxAccuracyM = 100;
        private const double MinStepKm = 0.025;
        private const double MaxGapMinutes = 15;
        private const double MaxSpeedKmh = 150;

        private static double Haversine(double la1, double lo1, double la2, double lo2)
        {
            double r(double x) => x * Math.PI / 180;
            var dLat = r(la2 - la1); var dLon = r(lo2 - lo1);
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(r(la1)) * Math.Cos(r(la2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 6371 * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        }

        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [HttpGet("doctor/{doctorId:long}/km")]
        public ActionResult Km(long doctorId, [FromQuery] string? from, [FromQuery] string? to)
        {
            if (doctorId != TrackingDoctorId) return Ok(new { days = new string[0], assistants = new object[0] });

            var today = DateTime.UtcNow.Add(Pkt).Date;
            var toDay = today;
            var fromDay = new DateTime(today.Year, today.Month, 1);
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var f)) fromDay = f.Date;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var t)) toDay = t.Date;
            if (toDay < fromDay) return BadRequest(new { message = "Invalid range." });
            if ((toDay - fromDay).TotalDays > 30) return BadRequest(new { message = "Range is limited to 31 days." });

            var fromUtc = fromDay - Pkt;
            var toUtc = toDay.AddDays(1) - Pkt;

            var pas = (from p in _db.PersonalAssistant
                       join perm in _db.PaPermissions on p.Id equals perm.PaId
                       where p.DoctorId == doctorId && p.IsActive && perm.TrackLocation
                       select new { p.Id, p.Name }).ToList();
            var paIds = pas.Select(p => p.Id).ToList();

            var fixes = _db.PaLocations
                .Where(l => l.DoctorId == doctorId && paIds.Contains(l.PaId) && l.RecordedAt >= fromUtc && l.RecordedAt < toUtc)
                .OrderBy(l => l.RecordedAt)
                .Select(l => new { l.PaId, l.ShiftId, l.Latitude, l.Longitude, l.Accuracy, l.RecordedAt })
                .ToList();
            var shifts = _db.PaShifts
                .Where(s => s.DoctorId == doctorId && paIds.Contains(s.PaId) && s.StartedAt >= fromUtc && s.StartedAt < toUtc)
                .ToList();

            string Day(DateTime utc) => utc.Add(Pkt).ToString("yyyy-MM-dd");
            var days = new List<string>();
            for (var d = fromDay; d <= toDay; d = d.AddDays(1)) days.Add(d.ToString("yyyy-MM-dd"));

            var assistants = new List<object>();
            foreach (var pa in pas)
            {
                var km = days.ToDictionary(d => d, d => 0.0);
                var used = days.ToDictionary(d => d, d => 0);
                var skipped = days.ToDictionary(d => d, d => 0);
                var gaps = days.ToDictionary(d => d, d => false);

                foreach (var grp in fixes.Where(x => x.PaId == pa.Id).GroupBy(x => x.ShiftId))
                {
                    double pLat = 0, pLon = 0; DateTime pAt = default; bool have = false;
                    foreach (var x in grp.OrderBy(x => x.RecordedAt))
                    {
                        var dk = Day(x.RecordedAt);
                        if (x.Accuracy.HasValue && x.Accuracy.Value > MaxAccuracyM) { skipped[dk]++; continue; }
                        used[dk]++;
                        if (have)
                        {
                            var mins = (x.RecordedAt - pAt).TotalMinutes;
                            var step = Haversine(pLat, pLon, x.Latitude, x.Longitude);
                            if (mins > MaxGapMinutes) { gaps[dk] = true; }
                            else if (step >= MinStepKm && mins > 0 && step / (mins / 60.0) <= MaxSpeedKmh)
                            {
                                km[dk] += step;
                            }
                            else if (step >= MinStepKm) { skipped[dk]++; continue; } // implausible jump: keep old anchor
                        }
                        pLat = x.Latitude; pLon = x.Longitude; pAt = x.RecordedAt; have = true;
                    }
                }

                var paShifts = shifts.Where(s => s.PaId == pa.Id).ToList();
                var cells = days.Select(d =>
                {
                    var ds = paShifts.Where(s => Day(s.StartedAt) == d).ToList();
                    var hours = ds.Sum(s => ((s.EndedAt ?? DateTime.UtcNow) - s.StartedAt).TotalHours);
                    return new
                    {
                        date = d,
                        km = Math.Round(km[d], 1),
                        shifts = ds.Count,
                        hours = Math.Round(hours, 1),
                        fixes = used[d],
                        skipped = skipped[d],
                        gaps = gaps[d]
                    };
                }).ToList();

                var worked = cells.Count(c => c.shifts > 0);
                var total = Math.Round(cells.Sum(c => c.km), 1);
                assistants.Add(new
                {
                    paId = pa.Id,
                    name = pa.Name,
                    totalKm = total,
                    shiftDays = worked,
                    avgKm = worked > 0 ? Math.Round(total / worked, 1) : 0,
                    totalShifts = cells.Sum(c => c.shifts),
                    totalHours = Math.Round(cells.Sum(c => c.hours), 1),
                    cells
                });
            }
            return Ok(new { days, assistants });
        }

        // Trail for one PA on one Pakistan-time calendar day (date = yyyy-MM-dd, default today).
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [Owns(OwnerKind.Pa, "paId")]
        [HttpGet("doctor/{doctorId:long}/trail")]
        public ActionResult Trail(long doctorId, [FromQuery] long paId, [FromQuery] string? date)
        {
            if (doctorId != TrackingDoctorId) return Ok(new object[0]);

            var localDay = DateTime.UtcNow.Add(Pkt).Date;
            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var d)) localDay = d.Date;
            var fromUtc = localDay - Pkt;
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
