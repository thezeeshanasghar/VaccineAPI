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
    public class ClinicTimingController : ControllerBase
    {
        private readonly Context _db;

        public ClinicTimingController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<Response<List<ClinicTiming>>> GetAll()
        {
            var list = await _db.ClinicTimings.ToListAsync();
            return new Response<List<ClinicTiming>>(true, null, list);
        }

        [HttpGet("{id}")]
        public async Task<Response<ClinicTiming>> GetSingle(long id)
        {
            var single = await _db.ClinicTimings.FindAsync(id);
            if (single == null)
            return new Response<ClinicTiming>(false, "Not Found", null);
                
            return new Response<ClinicTiming>(true, null, single);
        }

        [HttpPost]
        public async Task<ActionResult<ClinicTiming>> Post(ClinicTiming ClinicTiming)
        {
            _db.ClinicTimings.Update(ClinicTiming);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = ClinicTiming.Id }, ClinicTiming);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, ClinicTiming ClinicTiming)
        {
            if (id != ClinicTiming.Id)
                return BadRequest();

            _db.Entry(ClinicTiming).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.ClinicTimings.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.ClinicTimings.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
