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

        // POST /api/PAAssignment
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PAAssignment dto)
        {
            dto.AssignedAt   = DateTime.UtcNow;
            dto.IsCompleted  = false;
            dto.CompletedAt  = null;

            _db.PAAssignments.Add(dto);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, ResponseData = dto });
        }
    }
}
