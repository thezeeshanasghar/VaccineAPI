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
    public class DoctorScheduleController : ControllerBase
    {
        private readonly Context _db;

        public DoctorScheduleController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<Response<List<DoctorSchedule>>> GetAll()
        {
             var list = await _db.DoctorSchedules.ToListAsync();
            return new Response<List<DoctorSchedule>>(true, null, list);
        }

        [HttpGet("{id}")]
        public async Task<Response<DoctorSchedule>> GetSingle(long id)
        {
            var single = await _db.DoctorSchedules.FindAsync(id);
            if (single == null)
                return new Response<DoctorSchedule>(false, "Not Found", null);
           
                 return new Response<DoctorSchedule>(true, null, single);
        }

        [HttpPost]
        public async Task<ActionResult<DoctorSchedule>> Post(DoctorSchedule DoctorSchedule)
        {
            _db.DoctorSchedules.Update(DoctorSchedule);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = DoctorSchedule.Id }, DoctorSchedule);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, DoctorSchedule DoctorSchedule)
        {
            if (id != DoctorSchedule.Id)
                return BadRequest();

            _db.Entry(DoctorSchedule).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.DoctorSchedules.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.DoctorSchedules.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
