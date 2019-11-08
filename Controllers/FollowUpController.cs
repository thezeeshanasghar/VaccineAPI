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
    public class FollowUpController : ControllerBase
    {
        private readonly Context _db;

        public FollowUpController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<FollowUp>>> GetAll()
        {
            return await _db.FollowUps.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<FollowUp>> GetSingle(long id)
        {
            var single = await _db.FollowUps.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<FollowUp>> Post(FollowUp FollowUp)
        {
            _db.FollowUps.Update(FollowUp);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = FollowUp.Id }, FollowUp);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, FollowUp FollowUp)
        {
            if (id != FollowUp.Id)
                return BadRequest();

            _db.Entry(FollowUp).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.FollowUps.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.FollowUps.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
