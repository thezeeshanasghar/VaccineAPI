using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PAAccessController : ControllerBase
    {
        private readonly Context _db;

        public PAAccessController(Context db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaAccess>>> GetAll()
        {
            var paAccessList = await _db.PaAccess
                .Include(pa => pa.PersonalAssistant)
                .Include(pa => pa.Clinic)
                .ToListAsync();
            return Ok(paAccessList);
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<PaAccess>> GetById(long id)
        {
            var paAccess = await _db.PaAccess
                .Include(pa => pa.PersonalAssistant)
                .Include(pa => pa.Clinic)
                .FirstOrDefaultAsync(pa => pa.Id == id);
            if (paAccess == null)
            {
                return NotFound(new { message = "PA Access not found." });
            }
            return Ok(paAccess);
        }

        [HttpGet("doctor/{doctorId:long}")]
        public async Task<ActionResult<IEnumerable<PaAccess>>> GetPAsByDoctorId(long doctorId)
        {
            try
            {
                var paAccessList = await _db
                    .PaAccess.Include(pa => pa.PersonalAssistant) 
                    .Include(pa => pa.Clinic)
                    .Where(pa => pa.Clinic.DoctorId == doctorId)
                    .ToListAsync();

                if (!paAccessList.Any())
                {
                    return NotFound(
                        new { message = "No Personal Assistants found for the provided doctor ID." }
                    );
                }
                return Ok(paAccessList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching PAs for doctor ID {doctorId}: {ex.Message}");
                return StatusCode(500,new { message = "An error occurred while fetching Personal Assistants." });
            }
        }

        [HttpPost]
        public async Task<ActionResult<PaAccess>> Create(PaAccess paAccess)
        {
             var existingAccess = await _db.PaAccess.FirstOrDefaultAsync(pa => pa.PersonalAssistantId == paAccess.PersonalAssistantId && pa.ClinicId == paAccess.ClinicId);
             if (existingAccess != null)
             {
                return BadRequest(new { message = "PAAccess already exists for this doctor and clinic." });
             }
            var personalAssistant = await _db.PersonalAssistant.FindAsync(paAccess.PersonalAssistantId);
            if (personalAssistant == null)
            {
                return BadRequest("Invalid PersonalAssistantId.");
            }
            if (paAccess == null)
            {
                return BadRequest(new { message = "Invalid data." });
            }
            _db.PaAccess.Add(paAccess);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = paAccess.Id }, paAccess);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, PaAccess paAccess)
        {
            if (id != paAccess.Id)
            {
                return BadRequest(new { message = "ID mismatch." });
            }
            var existingPaAccess = await _db.PaAccess.FindAsync(id);
            if (existingPaAccess == null)
            {
                return NotFound(new { message = "PA Access not found." });
            }
            existingPaAccess.PersonalAssistantId = paAccess.PersonalAssistantId;
            existingPaAccess.ClinicId = paAccess.ClinicId;
            _db.Entry(existingPaAccess).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var paAccess = await _db.PaAccess.FindAsync(id);
            if (paAccess == null)
            {
                return NotFound(new { message = "PA Access not found." });
            }
            _db.PaAccess.Remove(paAccess);
            await _db.SaveChangesAsync();
            return Ok(new { message = "PA Access deleted successfully." });
        }
    }
}