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
    public class BrandAmountController : ControllerBase
    {
        private readonly Context _db;

        public BrandAmountController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BrandAmount>>> GetAll()
        {
            return await _db.BrandAmounts.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BrandAmount>> GetSingle(long id)
        {
            var single = await _db.BrandAmounts.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
        }

        [HttpPost]
        public async Task<ActionResult<BrandAmount>> Post(BrandAmount BrandAmount)
        {
            _db.BrandAmounts.Update(BrandAmount);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = BrandAmount.Id }, BrandAmount);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, BrandAmount BrandAmount)
        {
            if (id != BrandAmount.Id)
                return BadRequest();

            _db.Entry(BrandAmount).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.BrandAmounts.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.BrandAmounts.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
