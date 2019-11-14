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
    public class DoctorController : ControllerBase
    {
        private readonly Context _db;

        public DoctorController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<Response<List<Doctor>>> GetAll()
        {
             var list = await _db.Doctors.ToListAsync();
            return new Response<List<Doctor>>(true, null, list);
        }

        [HttpGet("{id}")]
        public async Task<Response<Doctor>> GetSingle(long id)
        {
            var single = await _db.Doctors.FindAsync(id);
            if (single == null)
                return new Response<Doctor>(false, "Not Found", null);
           
                 return new Response<Doctor>(true, null, single);
        }

        [HttpPost]
        public async Task<ActionResult<Doctor>> Post(Doctor Doctor)
        {
            _db.Doctors.Update(Doctor);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Doctor.Id }, Doctor);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Doctor Doctor)
        {
            if (id != Doctor.Id)
                return BadRequest();

            _db.Entry(Doctor).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Doctors.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Doctors.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
