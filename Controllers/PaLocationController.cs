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
