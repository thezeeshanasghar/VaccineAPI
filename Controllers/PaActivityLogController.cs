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
        // Doctor fetches all pending reversals awaiting approval (ungive-after-payment + invoice reductions)
        [HttpGet("pending-reversals/{doctorId:long}")]
        public ActionResult GetPendingReversals(long doctorId)
        {
            var logs = _db.PaActivityLogs
                .Include(l => l.PersonalAssistant)
                .Where(l => l.DoctorId == doctorId
                         && (l.ActionCode == "UngiveAfterPayment" || l.ActionCode == "InvoiceAmountReduction")
                         && l.IsReversal == true
                         && l.IsReversalApproved == false
                         && l.IsReversalRejected != true)
                .OrderByDescending(l => l.ActionDate)
                .Select(l => new {
                    l.Id,
                    l.PaId,
                    PaName      = l.PersonalAssistant != null ? l.PersonalAssistant.Name : "",
                    l.PatientId,
                    l.Notes,
                    l.Description,
                    l.ActionCode,
                    l.ActionDate
                })
                .ToList();

            return Ok(new { IsSuccess = true, ResponseData = logs });
        }

        // PATCH /api/PaActivityLog/{id}/approve-reversal
        // Doctor approves: invoice adjusted and PA payable reduced
        [HttpPatch("{id:long}/approve-reversal")]
        public ActionResult ApproveReversal(long id)
        {
            var log = _db.PaActivityLogs.FirstOrDefault(l => l.Id == id);
            if (log == null)
                return Ok(new { IsSuccess = false, Message = "Log entry not found." });

            if (!log.IsReversal)
                return Ok(new { IsSuccess = false, Message = "This entry is not a pending reversal." });

            if (log.IsReversalApproved)
                return Ok(new { IsSuccess = false, Message = "Already approved." });

            if (log.IsReversalRejected == true)
                return Ok(new { IsSuccess = false, Message = "Already rejected." });

            if (log.ActionCode == "UngiveAfterPayment")
            {
                // Parse ScheduleId from Notes ("Amount pending reversal: X | ScheduleId: Y")
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

                        // Belt-and-suspenders: ensure Invoice row is voided in case the ungive path missed it
                        var invoiceToVoid = _db.Invoices
                            .FirstOrDefault(i => i.DoseId == schedule.DoseId
                                              && i.ChildId == schedule.ChildId
                                              && i.DoctorId == log.DoctorId
                                              && i.IsVoided == false);
                        if (invoiceToVoid != null)
                        {
                            invoiceToVoid.IsVoided = true;
                            invoiceToVoid.SupersededBy = "UNGIVEN-APPROVED";
                            _db.Entry(invoiceToVoid).State = EntityState.Modified;
                        }

                        // Reduce PA payable for the ungiven vaccine amount
                        if (log.PaId > 0 && (schedule.Amount ?? 0) > 0)
                        {
                            _db.PaPayableAdjustments.Add(new PaPayableAdjustment
                            {
                                PaId = log.PaId,
                                DoctorId = log.DoctorId,
                                ClinicId = log.ClinicId,
                                Amount = -(schedule.Amount ?? 0),
                                Reason = $"Doctor approved ungive reversal (ScheduleId: {scheduleId})",
                                AdjustedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
            else if (log.ActionCode == "InvoiceAmountReduction")
            {
                // Parse reduction amount from Notes ("Reduction: X | OldAmount: Y | NewAmount: Z | ...")
                decimal reduction = 0;
                var notes = log.Notes ?? "";
                var rIdx = notes.IndexOf("Reduction: ");
                if (rIdx >= 0)
                {
                    var afterR = notes.Substring(rIdx + 11);
                    var end = afterR.IndexOf(" |");
                    var numStr = end >= 0 ? afterR.Substring(0, end) : afterR.Trim();
                    decimal.TryParse(numStr, out reduction);
                }

                // Apply the reduction to PA payable
                if (log.PaId > 0 && reduction > 0)
                {
                    _db.PaPayableAdjustments.Add(new PaPayableAdjustment
                    {
                        PaId = log.PaId,
                        DoctorId = log.DoctorId,
                        ClinicId = log.ClinicId,
                        Amount = -reduction,
                        Reason = $"Doctor approved invoice amount reduction of Rs {reduction}",
                        AdjustedAt = DateTime.UtcNow
                    });
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

        // PATCH /api/PaActivityLog/{id}/reject-reversal
        // Doctor rejects: no financial change, reversal dismissed
        [HttpPatch("{id:long}/reject-reversal")]
        public ActionResult RejectReversal(long id)
        {
            var log = _db.PaActivityLogs.FirstOrDefault(l => l.Id == id);
            if (log == null)
                return Ok(new { IsSuccess = false, Message = "Log entry not found." });

            if (!log.IsReversal)
                return Ok(new { IsSuccess = false, Message = "This entry is not a pending reversal." });

            if (log.IsReversalApproved)
                return Ok(new { IsSuccess = false, Message = "Already approved — cannot reject." });

            if (log.IsReversalRejected == true)
                return Ok(new { IsSuccess = false, Message = "Already rejected." });

            log.IsReversalRejected = true;

            try { _db.SaveChanges(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true });
        }
    }
}
