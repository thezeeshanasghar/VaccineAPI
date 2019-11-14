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
        public async Task<Response<List<Vaccine>>> GetAll()
        {
            var list = await _db.Vaccines.ToListAsync();
            return new Response<List<Vaccine>>(true, null, list);	
        }

        [HttpGet("{id}")]
        public async Task<Response<Vaccine>> GetSingle(long id)
        {
            
            var vaccine = await _db.Vaccines.FindAsync(id);
           
            if (vaccine == null)
            return new Response<Vaccine>(false, "Not Found", null);
           
            return new Response<Vaccine>(true, null, vaccine);
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
