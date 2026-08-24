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
            
            // Check if this is the first clinic for this PA - if so, set it as online
            var clinicList = await _db.PaAccess
                .Where(pa => pa.PersonalAssistantId == paAccess.PersonalAssistantId)
                .ToListAsync();
            paAccess.IsOnline = clinicList.Count == 0;
            
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
            existingPaAccess.IsOnline = paAccess.IsOnline;
            _db.Entry(existingPaAccess).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("{id:long}/isonline")]
        public async Task<IActionResult> UpdateIsOnline(long id, [FromBody] bool isOnline)
        {
            var existingPaAccess = await _db.PaAccess.FindAsync(id);
            if (existingPaAccess == null)
            {
                return NotFound(new { message = "PA Access not found." });
            }
            
            // Get all PA accesses for this PA
            var paAccessList = await _db.PaAccess
                .Where(pa => pa.PersonalAssistantId == existingPaAccess.PersonalAssistantId)
                .ToListAsync();

            // If setting this one to online, set others to offline (similar to clinic logic)
            if (isOnline && paAccessList.Count > 1)
            {
                foreach (var paAccess in paAccessList.Where(x => x.Id != id))
                {
                    paAccess.IsOnline = false;
                    _db.Entry(paAccess).State = EntityState.Modified;
                }
                existingPaAccess.IsOnline = true;
            }
            // If this is the only clinic for the PA, set it as online
            else if (paAccessList.Count == 1)
            {
                existingPaAccess.IsOnline = true;
            }
            else
            {
                existingPaAccess.IsOnline = isOnline;
            }

            _db.Entry(existingPaAccess).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new { message = "IsOnline status updated successfully.", isOnline = existingPaAccess.IsOnline });
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