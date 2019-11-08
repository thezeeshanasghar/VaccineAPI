using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClinicController : ControllerBase
    {
        private readonly Context _db;

        public ClinicController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Clinic>>> GetAll()
        {
            return await _db.Clinics.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Clinic>> GetSingle(long id)
        {
            var single = await _db.Clinics.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<Clinic>> Post(Clinic Clinic)
        {
            _db.Clinics.Update(Clinic);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Clinic.Id }, Clinic);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Clinic Clinic)
        {
            if (id != Clinic.Id)
                return BadRequest();

            _db.Entry(Clinic).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Clinics.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Clinics.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
