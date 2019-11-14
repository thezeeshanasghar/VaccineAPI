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
    public class BrandController : ControllerBase
    {
        private readonly Context _db;

        public BrandController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<Response<List<Brand>>> GetAll()
        {
             var list = await _db.Brands.ToListAsync();
            return new Response<List<Brand>>(true, null, list);
        }

        [HttpGet("{id}")]
        public async Task<Response<Brand>> GetSingle(long id)
        {
            var single = await _db.Brands.FindAsync(id);
            if (single == null)
                return new Response<Brand>(false, "Not Found", null);
           
                 return new Response<Brand>(true, null, single);
        }

        [HttpPost]
        public async Task<ActionResult<Brand>> Post(Brand Brand)
        {
            _db.Brands.Update(Brand);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Brand.Id }, Brand);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Brand Brand)
        {
            if (id != Brand.Id)
                return BadRequest();

            _db.Entry(Brand).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Brands.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Brands.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
