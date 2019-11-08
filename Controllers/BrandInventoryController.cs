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
    public class BrandInventoryController : ControllerBase
    {
        private readonly Context _db;

        public BrandInventoryController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BrandInventory>>> GetAll()
        {
            return await _db.BrandInventorys.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BrandInventory>> GetSingle(long id)
        {
            var single = await _db.BrandInventorys.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<BrandInventory>> Post(BrandInventory BrandInventory)
        {
            _db.BrandInventorys.Update(BrandInventory);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = BrandInventory.Id }, BrandInventory);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, BrandInventory BrandInventory)
        {
            if (id != BrandInventory.Id)
                return BadRequest();

            _db.Entry(BrandInventory).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.BrandInventorys.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.BrandInventorys.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
