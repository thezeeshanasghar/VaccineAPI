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
    public class DoseController : ControllerBase
    {
        private readonly Context _db;

        public DoseController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Dose>>> GetAll()
        {
            return await _db.Doses.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Dose>> GetSingle(long id)
        {
            var single = await _db.Doses.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<Dose>> Post(Dose Dose)
        {
            _db.Doses.Update(Dose);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Dose.Id }, Dose);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Dose Dose)
        {
            if (id != Dose.Id)
                return BadRequest();

            _db.Entry(Dose).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Doses.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Doses.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
