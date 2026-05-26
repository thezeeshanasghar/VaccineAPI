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
            var assignments = await _db.PAAssignments
                .Where(a => a.PersonalAssistantId == paId && !a.IsCompleted)
                .Join(_db.Childs,
                    a => a.ChildId,
                    c => c.Id,
                    (a, c) => new
                    {
                        AssignmentId = a.Id,
                        AssignedAt   = a.AssignedAt,
                        Notes        = a.Notes,
                        ChildId      = c.Id,
                        c.Name,
                        c.Gender,
                        c.DOB,
                        c.FatherName
                    })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = assignments });
        }

        // POST /api/PAAssignment/{id}/complete
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(long id)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            assignment.IsCompleted  = true;
            assignment.CompletedAt  = DateTime.UtcNow;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true });
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
            var exists = await _db.PAAssignments.AnyAsync(a =>
                a.ChildId == dto.ChildId &&
                a.PersonalAssistantId == dto.PersonalAssistantId &&
                !a.IsCompleted &&
                a.AssignedAt >= today && a.AssignedAt < today.AddDays(1));

            if (exists)
                return Ok(new { IsSuccess = false, Message = "Already assigned to this PA today" });

            dto.AssignedAt   = DateTime.UtcNow;
            dto.IsCompleted  = false;
            dto.CompletedAt  = null;

            _db.PAAssignments.Add(dto);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            var pa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                UserEmail.SendEmail(
                    pa.Email,
                    "A patient has been assigned to you. Please log in to your VacDoc app to view your assignments.",
                    "New Patient Assignment"
                );
            }

            return Ok(new { IsSuccess = true, ResponseData = dto });
        }
    }
}
