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
    public class ChildController : ControllerBase
    {
        private readonly Context _db;

        public ChildController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Child>>> GetAll()
        {
            return await _db.Childs.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Child>> GetSingle(long id)
        {
            var single = await _db.Childs.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<Child>> Post(Child Child)
        {
            _db.Childs.Update(Child);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Child.Id }, Child);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Child Child)
        {
            if (id != Child.Id)
                return BadRequest();

            _db.Entry(Child).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Childs.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Childs.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
