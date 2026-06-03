using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PAAssignmentController : ControllerBase
    {
        private readonly Context _db;

        public PAAssignmentController(Context db)
        {
            _db = db;
        }

        // GET /api/PAAssignment/pa/{paId}
        [HttpGet("pa/{paId}")]
        public async Task<IActionResult> GetByPA(long paId)
        {
            // Fetch raw assignments first — avoid EF Join+Where MySQL column misattribution bug
            var rawAssignments = await _db.PAAssignments
                .Where(a => a.PersonalAssistantId == paId && !a.IsCompleted && !a.IsCancelled)
                .ToListAsync();

            var childIds = rawAssignments.Select(a => a.ChildId).Distinct().ToList();
            var children = childIds.Any()
                ? await _db.Childs.Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id)
                : new Dictionary<long, VaccineAPI.Models.Child>();

            // Enrich each assignment with child info, schedules, and invoice
            var result = rawAssignments.Select(a =>
            {
                var child      = children.ContainsKey(a.ChildId) ? children[a.ChildId] : null;
                var assignDate = a.AssignedAt.Date;

                var assignDateEnd = assignDate.AddDays(1);
                var schedules = _db.Schedules
                    .Where(s => s.ChildId == a.ChildId
                             && s.PaymentCollectorPaId == paId
                             && s.GivenDate.HasValue
                             && s.GivenDate.Value >= assignDate
                             && s.GivenDate.Value < assignDateEnd)
                    .Join(_db.Doses,
                        s => s.DoseId,
                        d => d.Id,
                        (s, d) => new
                        {
                            s.Id,
                            DoseName           = d.Name,
                            s.IsPaymentCollected,
                            s.Amount,
                            s.Weight,
                            s.Height,
                            s.Circle
                        })
                    .ToList();

                var invoice = _db.InvoiceSubmissions
                    .Where(i => i.ChildId == a.ChildId && i.InvoiceDate.Date == assignDate)
                    .OrderByDescending(i => i.SubmittedAt)
                    .FirstOrDefault();

                return new
                {
                    AssignmentId  = a.Id,
                    a.AssignedAt,
                    a.Notes,
                    ChildId       = a.ChildId,
                    Name          = child != null ? child.Name      : "",
                    Gender        = child != null ? child.Gender    : "",
                    DOB           = child != null ? child.DOB       : (DateTime?)null,
                    FatherName    = child != null ? child.FatherName: "",
                    a.IsAutoCreated,
                    InvoiceAmount = invoice != null ? invoice.TotalAmount : 0m,
                    Schedules     = schedules
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // POST /api/PAAssignment/{id}/complete
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(long id, [FromQuery] long? paId = null)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is already cancelled" });

            // Ownership check: PA can only complete their own assignment
            if (paId.HasValue && assignment.PersonalAssistantId != paId.Value)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to complete this assignment" });

            // Payment gate: all schedules this PA collected with a non-zero amount must have payment recorded
            var assignDate    = assignment.AssignedAt.Date;
            var assignDateEnd = assignDate.AddDays(1);
            var unpaid = _db.Schedules
                .Where(s => s.ChildId == assignment.ChildId
                         && s.PaymentCollectorPaId == assignment.PersonalAssistantId
                         && s.GivenDate.HasValue
                         && s.GivenDate.Value >= assignDate
                         && s.GivenDate.Value < assignDateEnd
                         && !s.IsPaymentCollected
                         && s.Amount > 0)
                .Join(_db.Doses, s => s.DoseId, d => d.Id, (s, d) => d.Name)
                .ToList();

            if (unpaid.Any())
                return Ok(new { IsSuccess = false, Message = "Please record payment mode before completing. Unpaid vaccines: " + string.Join(", ", unpaid) });

            assignment.IsCompleted  = true;
            assignment.CompletedAt  = DateTime.UtcNow;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true });
        }

        // PATCH /api/PAAssignment/{id}/cancel
        [HttpPatch("{id}/cancel")]
        public async Task<IActionResult> Cancel(long id, [FromBody] CancelAssignmentDto dto)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Assignment is already completed" });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is already cancelled" });

            // PA can only cancel their own; doctor can cancel any under their DoctorId
            if (dto.CallerType == "PA" && assignment.PersonalAssistantId != dto.CallerId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to cancel this assignment" });

            if (dto.CallerType == "DOCTOR" && assignment.DoctorId != dto.CallerId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to cancel this assignment" });

            assignment.IsCancelled  = true;
            assignment.CancelledAt  = DateTime.UtcNow;
            assignment.CancelReason = dto.Reason;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Notify doctor by email (fire-and-forget)
            if (dto.CallerType == "PA")
            {
                var doctor = await _db.Doctors.FindAsync(assignment.DoctorId);
                if (doctor != null && !string.IsNullOrEmpty(doctor.Email))
                {
                    var paUser = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                    var paNameStr    = paUser?.Name ?? "Your PA";
                    var child        = await _db.Childs.FindAsync(assignment.ChildId);
                    var childNameStr = child?.Name ?? "a patient";
                    var reasonStr    = dto.Reason ?? "No reason given";
                    _ = Task.Run(() => UserEmail.SendEmail(
                        doctor.Email,
                        $"{paNameStr} has cancelled the assignment for patient {childNameStr}. Reason: {reasonStr}. Please reassign or reschedule.",
                        "PA Assignment Cancelled"
                    ));
                }
            }

            return Ok(new { IsSuccess = true });
        }

        // PATCH /api/PAAssignment/{id}/reassign
        [HttpPatch("{id}/reassign")]
        public async Task<IActionResult> Reassign(long id, [FromBody] ReassignDto dto)
        {
            var old = await _db.PAAssignments.FindAsync(id);
            if (old == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (old.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Cannot reassign a completed assignment" });

            if (old.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Cannot reassign a cancelled assignment" });

            // Cancel the old one
            old.IsCancelled  = true;
            old.CancelledAt  = DateTime.UtcNow;
            old.CancelReason = "Reassigned to another PA";

            // Create new assignment for the new PA
            var newAssignment = new PAAssignment
            {
                DoctorId                    = old.DoctorId,
                ClinicId                    = old.ClinicId,
                PersonalAssistantId         = dto.NewPaId,
                ChildId                     = old.ChildId,
                Notes                       = old.Notes,
                AssignedAt                  = DateTime.UtcNow,
                IsCompleted                 = false,
                ReassignedFromAssignmentId  = old.Id
            };

            _db.PAAssignments.Add(newAssignment);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Move invoice from old PA to new PA
            var reassignDay = DateTime.UtcNow.Date;
            var invoiceToMove = _db.InvoiceSubmissions.FirstOrDefault(i =>
                i.ChildId == old.ChildId &&
                i.InvoiceDate.Date == reassignDay &&
                (i.PaId == old.PersonalAssistantId || i.PaId == null));
            if (invoiceToMove != null)
            {
                invoiceToMove.PaId = dto.NewPaId;
                if (invoiceToMove.ClinicId == null && old.ClinicId.HasValue)
                    invoiceToMove.ClinicId = old.ClinicId;
                _db.Entry(invoiceToMove).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }

            // Notify new PA by email (fire-and-forget)
            var pa = await _db.PersonalAssistant.FindAsync(dto.NewPaId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    "A patient has been assigned to you. Please log in to your VacDoc app to view your assignments.",
                    "New Patient Assignment"
                ));
            }

            return Ok(new { IsSuccess = true, ResponseData = new { NewAssignmentId = newAssignment.Id } });
        }

        // GET /api/PAAssignment/clinic/{clinicId}
        [HttpGet("clinic/{clinicId}")]
        public async Task<IActionResult> GetPAsForClinic(long clinicId)
        {
            // Fetch PaAccess rows first, then look up PAs in memory — avoids EF Join+Where MySQL alias bug
            var paIds = await _db.PaAccess
                .Where(a => a.ClinicId == clinicId)
                .Select(a => a.PersonalAssistantId)
                .Distinct()
                .ToListAsync();

            var pas = await _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id) && p.IsActive)
                .Select(p => new { p.Id, p.Name, p.Email, p.IsActive })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = pas });
        }

        // POST /api/PAAssignment
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAssignmentDto dto)
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                var exists = await _db.PAAssignments.AnyAsync(a =>
                    a.ChildId == dto.ChildId &&
                    !a.IsCompleted &&
                    !a.IsCancelled &&
                    a.AssignedAt >= today && a.AssignedAt < today.AddDays(1));

                if (exists)
                {
                    var existingRow = await _db.PAAssignments
                        .Where(a => a.ChildId == dto.ChildId && !a.IsCompleted && !a.IsCancelled
                                    && a.AssignedAt >= today && a.AssignedAt < today.AddDays(1))
                        .FirstOrDefaultAsync();

                    var paName = "another PA";
                    if (existingRow != null)
                    {
                        var existingPa = await _db.PersonalAssistant.FindAsync(existingRow.PersonalAssistantId);
                        paName = existingPa?.Name ?? "another PA";
                    }
                    return Ok(new { IsSuccess = false, Message = $"This patient is already assigned to {paName} today. Cancel that assignment first or use Reassign." });
                }

                var assignment = new PAAssignment
                {
                    DoctorId            = dto.DoctorId,
                    ClinicId            = dto.ClinicId,
                    PersonalAssistantId = dto.PersonalAssistantId,
                    ChildId             = dto.ChildId,
                    Notes               = dto.Notes ?? "",
                    AssignedAt          = DateTime.UtcNow,
                    IsCompleted         = false,
                    IsCancelled         = false,
                    IsAutoCreated       = false
                };

                _db.PAAssignments.Add(assignment);
                await _db.SaveChangesAsync();

                // Stamp today's doctor-downloaded invoice with this PA so it appears in their payable
                var assignDay = DateTime.UtcNow.Date;
                var todayInvoice = _db.InvoiceSubmissions.FirstOrDefault(i =>
                    i.ChildId == dto.ChildId &&
                    i.InvoiceDate.Date == assignDay &&
                    i.PaId == null);
                if (todayInvoice != null)
                {
                    todayInvoice.PaId = dto.PersonalAssistantId;
                    if (todayInvoice.ClinicId == null && dto.ClinicId.HasValue)
                        todayInvoice.ClinicId = dto.ClinicId;
                    _db.Entry(todayInvoice).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }

                // Fire-and-forget email
                var newPa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
                if (newPa != null && !string.IsNullOrEmpty(newPa.Email))
                {
                    _ = Task.Run(() => UserEmail.SendEmail(
                        newPa.Email,
                        "A patient has been assigned to you. Please log in to your VacDoc app to view your assignments.",
                        "New Patient Assignment"
                    ));
                }

                return Ok(new { IsSuccess = true, ResponseData = new { assignment.Id } });
            }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // PATCH /api/PAAssignment/{id}/mark-done
        // PA marks assignment as done (payment collected) — transitions to PendingHandover state.
        // Doctor sees this row on Payment Reconciliation with "Pending Handover" flag.
        [HttpPatch("{id}/mark-done")]
        public async Task<IActionResult> MarkDone(long id, [FromQuery] long? paId = null)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found." });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is cancelled." });

            if (assignment.AssignmentStatus == "PendingHandover" || assignment.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Assignment is already marked done." });

            if (paId.HasValue && assignment.PersonalAssistantId != paId.Value)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to update this assignment." });

            // Payment gate: at least one schedule for this child/day must have payment recorded
            var assignDate    = assignment.AssignedAt.Date;
            var assignDateEnd = assignDate.AddDays(1);
            var hasPayment = _db.Schedules.Any(s =>
                s.ChildId == assignment.ChildId &&
                s.PaymentCollectorPaId == assignment.PersonalAssistantId &&
                s.GivenDate.HasValue &&
                s.GivenDate.Value >= assignDate &&
                s.GivenDate.Value < assignDateEnd &&
                s.IsPaymentCollected == true);

            if (!hasPayment)
                return Ok(new { IsSuccess = false, Message = "Please record payment mode before marking done." });

            assignment.AssignmentStatus = "PendingHandover";
            assignment.HandoverDoneAt   = DateTime.UtcNow;

            // Flag the linked InvoiceSubmission so it shows as PendingHandover on reconciliation
            var inv = _db.InvoiceSubmissions.FirstOrDefault(i =>
                i.ChildId == assignment.ChildId &&
                i.InvoiceDate.Date == assignDate);
            if (inv != null)
            {
                inv.PendingHandover = true;
                inv.HandoverDoneAt  = DateTime.UtcNow;
                _db.Entry(inv).State = EntityState.Modified;
            }

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Assignment marked as done. Pending handover to doctor." });
        }

        // GET /api/PAAssignment/active/{doctorId}
        // Returns all active (non-completed, non-cancelled) assignments for a doctor today
        [HttpGet("active/{doctorId}")]
        public async Task<IActionResult> GetActiveForDoctor(long doctorId)
        {
            var today = DateTime.UtcNow.Date;

            // Fetch raw assignment rows first, then enrich in memory to avoid EF join translation issues
            var raw = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId
                         && !a.IsCompleted
                         && !a.IsCancelled
                         && a.AssignedAt >= today && a.AssignedAt < today.AddDays(1))
                .ToListAsync();

            var childIds = raw.Select(a => a.ChildId).Distinct().ToList();
            var paIds    = raw.Select(a => a.PersonalAssistantId).Distinct().ToList();

            var children = childIds.Any()
                ? await _db.Childs.Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id)
                : new Dictionary<long, VaccineAPI.Models.Child>();

            var pas = paIds.Any()
                ? await _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id)
                : new Dictionary<long, VaccineAPI.Models.PersonalAssistant>();

            var assignments = raw.Select(a => new {
                AssignmentId = a.Id,
                a.AssignedAt,
                a.Notes,
                ChildId      = a.ChildId,
                ChildName    = children.ContainsKey(a.ChildId)             ? children[a.ChildId].Name       : "",
                Gender       = children.ContainsKey(a.ChildId)             ? children[a.ChildId].Gender     : "",
                DOB          = children.ContainsKey(a.ChildId)             ? children[a.ChildId].DOB        : (DateTime?)null,
                FatherName   = children.ContainsKey(a.ChildId)             ? children[a.ChildId].FatherName : "",
                PaId         = a.PersonalAssistantId,
                PaName       = pas.ContainsKey(a.PersonalAssistantId)      ? pas[a.PersonalAssistantId].Name : ""
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = assignments });
        }
    }

    public class CancelAssignmentDto
    {
        public string CallerType { get; set; } // "DOCTOR" or "PA"
        public long CallerId { get; set; }
        public string? Reason { get; set; }
    }

    public class ReassignDto
    {
        public long NewPaId { get; set; }
    }

    public class CreateAssignmentDto
    {
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long PersonalAssistantId { get; set; }
        public long ChildId { get; set; }
        public string? Notes { get; set; }
    }
}
