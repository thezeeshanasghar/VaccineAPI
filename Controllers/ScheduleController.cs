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
    public class ScheduleController : ControllerBase
    {
        private readonly Context _db;

        public ScheduleController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Schedule>>> GetAll()
        {
            return await _db.Schedules.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Schedule>> GetSingle(long id)
        {
            var single = await _db.Schedules.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<Schedule>> Post(Schedule Schedule)
        {
            _db.Schedules.Update(Schedule);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Schedule.Id }, Schedule);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Schedule Schedule)
        {
            if (id != Schedule.Id)
                return BadRequest();

            _db.Entry(Schedule).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Schedules.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Schedules.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
