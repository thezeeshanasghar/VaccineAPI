using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaActivityLogController : ControllerBase
    {
        private readonly Context _db;

        public PaActivityLogController(Context db)
        {
            _db = db;
        }

        // PA calls this every time it takes an action
        [HttpPost]
        public ActionResult Create([FromBody] PaActivityLog log)
        {
            if (log == null)
                return BadRequest(new { message = "Invalid data." });

            log.Id = 0;
            log.ActionDate = DateTime.UtcNow;
            _db.PaActivityLogs.Add(log);
            _db.SaveChanges();
            return Ok(new { message = "Action logged.", logId = log.Id });
        }

        // Doctor fetches all logs for their PAs — optional filters via query params
        [HttpGet("doctor/{doctorId:long}")]
        public ActionResult GetByDoctor(
            long doctorId,
            [FromQuery] long? paId,
            [FromQuery] string? actionCode,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            var query = _db.PaActivityLogs
                .Include(l => l.PersonalAssistant)
                .Where(l => l.DoctorId == doctorId);

            if (paId.HasValue)
                query = query.Where(l => l.PaId == paId.Value);

            if (!string.IsNullOrEmpty(actionCode))
                query = query.Where(l => l.ActionCode == actionCode);

            if (from.HasValue)
                query = query.Where(l => l.ActionDate >= from.Value);

            if (to.HasValue)
                query = query.Where(l => l.ActionDate <= to.Value);

            var total = query.Count();
            var logs = query
                .OrderByDescending(l => l.ActionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new
                {
                    l.Id,
                    l.PaId,
                    PaName = l.PersonalAssistant != null ? l.PersonalAssistant.Name : "",
                    l.ClinicId,
                    l.PatientId,
                    l.ActionCode,
                    l.Description,
                    l.Notes,
                    l.IsReversal,
                    l.ReversalOfLogId,
                    l.ActionDate
                })
                .ToList();

            return Ok(new { total, page, pageSize, logs });
        }

        // Fetch logs for a single PA
        [HttpGet("pa/{paId:long}")]
        public ActionResult GetByPa(long paId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var total = _db.PaActivityLogs.Count(l => l.PaId == paId);
            var logs = _db.PaActivityLogs
                .Where(l => l.PaId == paId)
                .OrderByDescending(l => l.ActionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new { total, page, pageSize, logs });
        }
    }
}
