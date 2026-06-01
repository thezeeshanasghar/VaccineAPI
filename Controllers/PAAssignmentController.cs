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
        public async Task<IActionResult> Create([FromBody] PAAssignment dto)
        {
            var today = DateTime.UtcNow.Date;

            // Block if ANY PA already has an active assignment for this child today
            var exists = await _db.PAAssignments.AnyAsync(a =>
                a.ChildId == dto.ChildId &&
                !a.IsCompleted &&
                !a.IsCancelled &&
                a.AssignedAt >= today && a.AssignedAt < today.AddDays(1));

            if (exists)
            {
                // Find which PA has it to give a helpful message
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

            dto.AssignedAt   = DateTime.UtcNow;
            dto.IsCompleted  = false;
            dto.IsCancelled  = false;
            dto.CompletedAt  = null;

            _db.PAAssignments.Add(dto);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Fire-and-forget email so email failure does not fail the create
            var pa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    "A patient has been assigned to you. Please log in to your VacDoc app to view your assignments.",
                    "New Patient Assignment"
                ));
            }

            return Ok(new { IsSuccess = true, ResponseData = dto });
        }

        // GET /api/PAAssignment/active/{doctorId}
        // Returns all active (non-completed, non-cancelled) assignments for a doctor's clinics today
        [HttpGet("active/{doctorId}")]
        public async Task<IActionResult> GetActiveForDoctor(long doctorId)
        {
            var today = DateTime.UtcNow.Date;
            var clinicIds = await _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToListAsync();

            var assignments = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId
                         && !a.IsCompleted
                         && !a.IsCancelled
                         && a.AssignedAt >= today && a.AssignedAt < today.AddDays(1))
                .Join(_db.Childs,
                    a => a.ChildId,
                    c => c.Id,
                    (a, c) => new { a, c })
                .Join(_db.PersonalAssistant,
                    x => x.a.PersonalAssistantId,
                    p => p.Id,
                    (x, p) => new
                    {
                        AssignmentId          = x.a.Id,
                        x.a.AssignedAt,
                        x.a.Notes,
                        ChildId               = x.c.Id,
                        ChildName             = x.c.Name,
                        x.c.Gender,
                        x.c.DOB,
                        x.c.FatherName,
                        PaId                  = p.Id,
                        PaName                = p.Name
                    })
                .ToListAsync();

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
}
