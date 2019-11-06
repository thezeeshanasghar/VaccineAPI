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
    public class VaccineController : ControllerBase
    {
        private readonly Context _db;

        public VaccineController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Vaccine>>> GetAll()
        {
            return await _db.Vaccines.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Vaccine>> GetSingle(long id)
        {
            var single = await _db.Vaccines.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<Vaccine>> Post(Vaccine Vaccine)
        {
            _db.Vaccines.Update(Vaccine);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Vaccine.Id }, Vaccine);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Vaccine Vaccine)
        {
            if (id != Vaccine.Id)
                return BadRequest();

            _db.Entry(Vaccine).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Vaccines.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Vaccines.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
