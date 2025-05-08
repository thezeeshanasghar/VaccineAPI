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

        // GET: api/PAAccess
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaAccess>>> GetAll()
        {
            var paAccessList = await _db.PaAccess
                .Include(pa => pa.PersonalAssistant)
                .Include(pa => pa.Clinic)
                .ToListAsync();
            return Ok(paAccessList);
        }

        // GET: api/PAAccess/{id}
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

        // POST: api/PAAccess
        [HttpPost]
        public async Task<ActionResult<PaAccess>> Create(PaAccess paAccess)
        {
            if (paAccess == null)
            {
                return BadRequest(new { message = "Invalid data." });
            }

            _db.PaAccess.Add(paAccess);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = paAccess.Id }, paAccess);
        }

        // PUT: api/PAAccess/{id}
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

            existingPaAccess.PaId = paAccess.PaId;
            existingPaAccess.ClinicId = paAccess.ClinicId;

            _db.Entry(existingPaAccess).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/PAAccess/{id}
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