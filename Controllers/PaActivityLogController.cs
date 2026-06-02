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

        // GET /api/PaActivityLog/pending-reversals/{doctorId}
        // Doctor fetches all ungive-after-payment events awaiting their approval
        [HttpGet("pending-reversals/{doctorId:long}")]
        public ActionResult GetPendingReversals(long doctorId)
        {
            var logs = _db.PaActivityLogs
                .Include(l => l.PersonalAssistant)
                .Where(l => l.DoctorId == doctorId
                         && l.ActionCode == "UngiveAfterPayment"
                         && l.IsReversal == true
                         && l.IsReversalApproved == false)
                .OrderByDescending(l => l.ActionDate)
                .Select(l => new {
                    l.Id,
                    l.PaId,
                    PaName      = l.PersonalAssistant != null ? l.PersonalAssistant.Name : "",
                    l.PatientId,
                    l.Notes,
                    l.Description,
                    l.ActionDate
                })
                .ToList();

            return Ok(new { IsSuccess = true, ResponseData = logs });
        }

        // PATCH /api/PaActivityLog/{id}/approve-reversal
        // Doctor approves: invoice is adjusted and IsPaymentCollected reset on the schedule
        [HttpPatch("{id:long}/approve-reversal")]
        public ActionResult ApproveReversal(long id)
        {
            var log = _db.PaActivityLogs.FirstOrDefault(l => l.Id == id);
            if (log == null)
                return Ok(new { IsSuccess = false, Message = "Log entry not found." });

            if (log.ActionCode != "UngiveAfterPayment" || !log.IsReversal)
                return Ok(new { IsSuccess = false, Message = "This entry is not a pending reversal." });

            if (log.IsReversalApproved)
                return Ok(new { IsSuccess = false, Message = "Already approved." });

            // Parse ScheduleId from Notes field ("Amount pending reversal: X | ScheduleId: Y")
            long scheduleId = 0;
            var notesParts = log.Notes ?? "";
            var sidIndex = notesParts.IndexOf("ScheduleId: ");
            if (sidIndex >= 0)
                long.TryParse(notesParts.Substring(sidIndex + 12).Trim(), out scheduleId);

            if (scheduleId > 0)
            {
                var schedule = _db.Schedules.FirstOrDefault(s => s.Id == scheduleId);
                if (schedule != null)
                {
                    var invoiceDate = schedule.GivenDate.HasValue ? schedule.GivenDate.Value.Date : log.ActionDate.Date;
                    var inv = _db.InvoiceSubmissions.FirstOrDefault(x =>
                        x.ChildId == schedule.ChildId &&
                        x.DoctorId == log.DoctorId &&
                        x.InvoiceDate.Date == invoiceDate);
                    if (inv != null)
                    {
                        inv.TotalAmount = Math.Max(0, inv.TotalAmount - (schedule.Amount ?? 0));
                        _db.Entry(inv).State = EntityState.Modified;
                    }
                    schedule.IsPaymentCollected = false;
                }
            }

            log.IsReversalApproved = true;

            try { _db.SaveChanges(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true });
        }
    }
}
