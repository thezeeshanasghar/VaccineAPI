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
            var rawAssignments = await _db.PAAssignments
                .Where(a => a.PersonalAssistantId == paId && !a.IsCompleted && !a.IsCancelled)
                .Join(_db.Childs,
                    a => a.ChildId,
                    c => c.Id,
                    (a, c) => new
                    {
                        AssignmentId    = a.Id,
                        AssignedAt      = a.AssignedAt,
                        Notes           = a.Notes,
                        ChildId         = c.Id,
                        c.Name,
                        c.Gender,
                        c.DOB,
                        c.FatherName,
                        IsAutoCreated   = a.IsAutoCreated
                    })
                .ToListAsync();

            // Enrich each assignment with today's schedules this PA gave
            var result = rawAssignments.Select(a =>
            {
                var assignDate = a.AssignedAt.Date;
                var schedules = _db.Schedules
                    .Where(s => s.ChildId == a.ChildId
                             && s.PaymentCollectorPaId == paId
                             && s.GivenDate.HasValue
                             && s.GivenDate.Value.Date == assignDate)
                    .Join(_db.Doses,
                        s => s.DoseId,
                        d => d.Id,
                        (s, d) => new
                        {
                            s.Id,
                            DoseName           = d.Name,
                            s.IsPaymentCollected,
                            s.Weight,
                            s.Height,
                            s.Circle
                        })
                    .ToList();

                var invoice = _db.InvoiceSubmissions
                    .Where(i => i.ChildId == a.ChildId && i.PaId == paId)
                    .OrderByDescending(i => i.SubmittedAt)
                    .FirstOrDefault();

                return new
                {
                    a.AssignmentId,
                    a.AssignedAt,
                    a.Notes,
                    a.ChildId,
                    a.Name,
                    a.Gender,
                    a.DOB,
                    a.FatherName,
                    a.IsAutoCreated,
                    InvoiceAmount = invoice != null ? invoice.TotalAmount : 0m,
                    Schedules = schedules
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

            // Payment gate: all schedules this PA collected must have payment mode recorded
            var assignDate = assignment.AssignedAt.Date;
            var unpaid = _db.Schedules
                .Where(s => s.ChildId == assignment.ChildId
                         && s.PaymentCollectorPaId == assignment.PersonalAssistantId
                         && s.GivenDate.HasValue
                         && s.GivenDate.Value.Date == assignDate
                         && !s.IsPaymentCollected)
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
            var pas = await _db.PaAccess
                .Where(a => a.ClinicId == clinicId)
                .Join(_db.PersonalAssistant,
                    a => a.PersonalAssistantId,
                    p => p.Id,
                    (a, p) => new { p.Id, p.Name, p.Email, p.IsActive })
                .Where(p => p.IsActive)
                .Distinct()
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
                    var existing = await _db.PAAssignments
                        .Where(a => a.ChildId == dto.ChildId && !a.IsCompleted && !a.IsCancelled
                                    && a.AssignedAt >= today && a.AssignedAt < today.AddDays(1))
                        .Join(_db.PersonalAssistant,
                            a => a.PersonalAssistantId,
                            p => p.Id,
                            (a, p) => new { a.Id, PaName = p.Name })
                        .FirstOrDefaultAsync();

                    var paName = existing?.PaName ?? "another PA";
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

                // Fire-and-forget email
                var pa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
                if (pa != null && !string.IsNullOrEmpty(pa.Email))
                {
                    _ = Task.Run(() => UserEmail.SendEmail(
                        pa.Email,
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

            var children = await _db.Childs
                .Where(c => childIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

            var pas = await _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

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
