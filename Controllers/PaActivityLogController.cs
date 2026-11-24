using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
    [Owns(OwnerKind.Doctor, "doctorId", "DoctorId")]
    [Owns(OwnerKind.Pa, "paId", "PaId")]
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
            var query = VaccineAPI.Services.PaDoctor.ForDoctor(_db, _db.PaActivityLogs.Include(l => l.PersonalAssistant), doctorId);

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
            var logs = VaccineAPI.Services.PaDoctor.ForDoctor(_db, _db.PaActivityLogs.Include(l => l.PersonalAssistant), doctorId)
                .Where(l => (l.ActionCode == "UngiveAfterPayment" || l.ActionCode == "InvoiceAmountReduction")
                         && l.IsReversal == true
                         && l.IsReversalApproved == false
                         && l.IsReversalRejected != true)
                .OrderByDescending(l => l.ActionDate)
                .ToList();

            // Patient / dose / invoice and the dose's CURRENT state, so the doctor can tell what each line is
            // and whether it is stale (the dose was given again after the PA's ungive).
            var patientIds = logs.Where(l => l.PatientId.HasValue).Select(l => l.PatientId.Value).Distinct().ToList();
            var names = _db.Childs.Where(c => patientIds.Contains(c.Id)).ToDictionary(c => c.Id, c => c.Name ?? "");
            var parsed = logs.ToDictionary(l => l.Id, l => VaccineAPI.Services.UngiveReversalGuard.ParseNotes(l.Notes));
            var schedIds = parsed.Values.Where(p => p.ScheduleId > 0).Select(p => p.ScheduleId).Distinct().ToList();
            var doseInfo = (from s in _db.Schedules
                            where schedIds.Contains(s.Id)
                            join d in _db.Doses on s.DoseId equals d.Id
                            select new { s.Id, s.IsDone, DoseName = d.Name }).ToDictionary(x => x.Id, x => x);
            var invIds = parsed.Values.Where(p => p.InvoiceId > 0).Select(p => p.InvoiceId).Distinct().ToList();
            var invTotals = _db.InvoiceSubmissions.Where(i => invIds.Contains(i.Id)).ToDictionary(i => i.Id, i => i.TotalAmount);

            var result = logs.Select(l =>
            {
                var p = parsed[l.Id];
                var isUngive = l.ActionCode == "UngiveAfterPayment";
                var dose = isUngive && doseInfo.ContainsKey(p.ScheduleId) ? doseInfo[p.ScheduleId] : null;
                return new {
                    l.Id,
                    l.PaId,
                    PaName      = l.PersonalAssistant != null ? l.PersonalAssistant.Name : "",
                    l.PatientId,
                    PatientName = l.PatientId.HasValue && names.ContainsKey(l.PatientId.Value) ? names[l.PatientId.Value] : "",
                    l.Notes,
                    l.Description,
                    l.ActionCode,
                    l.ActionDate,
                    ScheduleId  = isUngive ? (long?)p.ScheduleId : null,
                    DoseName    = dose != null ? dose.DoseName : "",
                    InvoiceId   = isUngive && p.InvoiceId > 0 ? (long?)p.InvoiceId : null,
                    InvoiceAmount = isUngive && invTotals.ContainsKey(p.InvoiceId) ? (decimal?)invTotals[p.InvoiceId] : null,
                    DoseGiven   = dose != null && dose.IsDone,
                    IsStale     = dose != null && dose.IsDone
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // PATCH /api/PaActivityLog/{id}/approve-reversal
        // Doctor approves: invoice adjusted and PA payable reduced
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [HttpPatch("{id:long}/approve-reversal")]
        public ActionResult ApproveReversal(long id)
        {
            var log = _db.PaActivityLogs.FirstOrDefault(l => l.Id == id);
            if (log == null)
                return Ok(new { IsSuccess = false, Message = "Log entry not found." });

            // Rows written before the PaDoctor fix carry DoctorId 0; use their PA's / manager's doctor.
            var effectiveDoctorId = VaccineAPI.Services.PaDoctor.Effective(_db, log);

            if (!log.IsReversal)
                return Ok(new { IsSuccess = false, Message = "This entry is not a pending reversal." });

            if (log.IsReversalApproved)
                return Ok(new { IsSuccess = false, Message = "Already approved." });

            if (log.IsReversalRejected == true)
                return Ok(new { IsSuccess = false, Message = "Already rejected." });

            if (log.ActionCode == "UngiveAfterPayment")
            {
                // Parse ScheduleId and InvoiceSubmissionId from Notes
                // ("Amount pending reversal: X | ScheduleId: Y | InvoiceSubmissionId: Z").
                // The invoice is looked up by this FK — captured by ScheduleController at the
                // moment of ungive, before Schedule.InvoiceSubmissionId/GivenDate were wiped —
                // NOT by guessing InvoiceDate == Schedule.GivenDate. That guess used to be the
                // only lookup here and was already silently broken: GivenDate is always null by
                // the time a doctor gets around to approving (the ungive itself clears it), so
                // the old code fell back to "today," almost never the invoice's real date.
                var parsedNotes = VaccineAPI.Services.UngiveReversalGuard.ParseNotes(log.Notes);
                long scheduleId = parsedNotes.ScheduleId;
                long invoiceSubmissionId = parsedNotes.InvoiceId;
                // The ungive nulls Schedule.Amount, so the amount to reverse is the one captured in the note.
                var noteAmount = VaccineAPI.Services.UngiveReversalGuard.ParseAmount(log.Notes);

                // S0: the doctor already confirmed this invoice, so the cash is counted and there is nothing to
                // forgive or reduce. Only Reject is valid; the confirmed invoice is never cut from here.
                if (invoiceSubmissionId > 0 && _db.InvoiceSubmissions.Any(i => i.Id == invoiceSubmissionId && i.IsConfirmedByDoctor))
                    return Ok(new { IsSuccess = false, Message = "The doctor already confirmed this invoice, so there is nothing to reduce. Reject this request." });

                if (scheduleId > 0)
                {
                    var schedule = _db.Schedules.FirstOrDefault(s => s.Id == scheduleId);
                    // The dose was given again after the PA's ungive: approving would un-pay a given dose and
                    // cancel its invoice QR. Only Reject is valid (live case: Hakeem Ullah, 19 Nov 2026).
                    if (schedule != null && schedule.IsDone)
                        return Ok(new { IsSuccess = false, Message = "This dose has been given again since the PA ungave it, so this request is stale and cannot be approved. Reject it." });
                    if (schedule != null)
                    {
                        var reverseAmount = (schedule.Amount ?? 0) > 0 ? schedule.Amount!.Value : noteAmount;
                        schedule.IsPaymentCollected = false;

                        if (invoiceSubmissionId > 0)
                        {
                            var inv = _db.InvoiceSubmissions.FirstOrDefault(x => x.Id == invoiceSubmissionId);
                            if (inv != null)
                            {
                                inv.TotalAmount = Math.Max(0, inv.TotalAmount - reverseAmount);
                                if (inv.TotalAmount == 0)
                                {
                                    // This was the only (or last remaining) dose on this invoice —
                                    // delete it outright, matching the doctor-instant path's
                                    // behavior for the same situation.
                                    var amendments = _db.InvoiceAmendments.Where(am => am.InvoiceSubmissionId == inv.Id);
                                    _db.InvoiceAmendments.RemoveRange(amendments);

                                    foreach (var linkedAssignment in _db.PAAssignments.Where(a => a.InvoiceSubmissionId == inv.Id).ToList())
                                        linkedAssignment.InvoiceSubmissionId = null;

                                    _db.InvoiceSubmissions.Remove(inv);
                                }
                                else
                                {
                                    // Other doses still stand on this invoice — reduce, don't delete.
                                    _db.Entry(inv).State = EntityState.Modified;
                                }
                            }
                        }

                        // Belt-and-suspenders: ensure Invoice row is voided in case the ungive path missed it
                        var invoiceToVoid = _db.Invoices
                            .FirstOrDefault(i => i.DoseId == schedule.DoseId
                                              && i.ChildId == schedule.ChildId
                                              && i.DoctorId == effectiveDoctorId
                                              && i.IsVoided == false);
                        if (invoiceToVoid != null)
                        {
                            invoiceToVoid.IsVoided = true;
                            invoiceToVoid.SupersededBy = "UNGIVEN-APPROVED";
                            _db.Entry(invoiceToVoid).State = EntityState.Modified;
                        }

                        // Reduce PA payable for the ungiven vaccine amount. Manager-originated
                        // logs (PaId null) correctly skip this — Manager has no payable balance.
                        if (log.PaId.HasValue && log.PaId > 0 && reverseAmount > 0)
                        {
                            _db.PaPayableAdjustments.Add(new PaPayableAdjustment
                            {
                                PaId = log.PaId.Value,
                                DoctorId = effectiveDoctorId,
                                ClinicId = log.ClinicId,
                                Amount = -reverseAmount,
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

                // Apply the reduction to PA payable. Manager-originated logs (PaId null)
                // correctly skip this — Manager has no payable balance.
                if (log.PaId.HasValue && log.PaId > 0 && reduction > 0)
                {
                    _db.PaPayableAdjustments.Add(new PaPayableAdjustment
                    {
                        PaId = log.PaId.Value,
                        DoctorId = effectiveDoctorId,
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
        [RolesOnly("DOCTOR", "SUPERADMIN")]
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
