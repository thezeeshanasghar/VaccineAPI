using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ManagerAccessController : ControllerBase
    {
        private readonly Context _db;

        public ManagerAccessController(Context db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ManagerAccess>>> GetAll()
        {
            var managerAccessList = await _db.ManagerAccess
                .Include(ma => ma.Manager)
                .Include(ma => ma.Clinic)
                .ToListAsync();
            return Ok(managerAccessList);
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<ManagerAccess>> GetById(long id)
        {
            var managerAccess = await _db.ManagerAccess
                .Include(ma => ma.Manager)
                .Include(ma => ma.Clinic)
                .FirstOrDefaultAsync(ma => ma.Id == id);
            if (managerAccess == null)
            {
                return NotFound(new { message = "Manager Access not found." });
            }
            return Ok(managerAccess);
        }

        [HttpGet("doctor/{doctorId:long}")]
        public async Task<ActionResult<IEnumerable<ManagerAccess>>> GetManagersByDoctorId(long doctorId)
        {
            try
            {
                var managerAccessList = await _db
                    .ManagerAccess.Include(ma => ma.Manager)
                    .Include(ma => ma.Clinic)
                    .Where(ma => ma.Clinic.DoctorId == doctorId)
                    .ToListAsync();

                if (!managerAccessList.Any())
                {
                    return NotFound(
                        new { message = "No Managers found for the provided doctor ID." }
                    );
                }
                return Ok(managerAccessList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Managers for doctor ID {doctorId}: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred while fetching Managers." });
            }
        }

        [HttpPost]
        public async Task<ActionResult<ManagerAccess>> Create(ManagerAccess managerAccess)
        {
            var existingAccess = await _db.ManagerAccess.FirstOrDefaultAsync(ma => ma.ManagerId == managerAccess.ManagerId && ma.ClinicId == managerAccess.ClinicId);
            if (existingAccess != null)
            {
                return BadRequest(new { message = "Manager Access already exists for this manager and clinic." });
            }
            var manager = await _db.Manager.FindAsync(managerAccess.ManagerId);
            if (manager == null)
            {
                return BadRequest("Invalid ManagerId.");
            }
            if (managerAccess == null)
            {
                return BadRequest(new { message = "Invalid data." });
            }

            _db.ManagerAccess.Add(managerAccess);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = managerAccess.Id }, managerAccess);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, ManagerAccess managerAccess)
        {
            if (id != managerAccess.Id)
            {
                return BadRequest(new { message = "ID mismatch." });
            }
            var existingManagerAccess = await _db.ManagerAccess.FindAsync(id);
            if (existingManagerAccess == null)
            {
                return NotFound(new { message = "Manager Access not found." });
            }
            existingManagerAccess.ManagerId = managerAccess.ManagerId;
            existingManagerAccess.ClinicId = managerAccess.ClinicId;
            _db.Entry(existingManagerAccess).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var managerAccess = await _db.ManagerAccess.FindAsync(id);
            if (managerAccess == null)
            {
                return NotFound(new { message = "Manager Access not found." });
            }
            _db.ManagerAccess.Remove(managerAccess);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Manager Access deleted successfully." });
        }
    }
}
