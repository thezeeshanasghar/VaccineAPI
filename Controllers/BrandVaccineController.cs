using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// using BrandVaccine.DTOs;
using VaccineAPI.Models;
// using BrandVaccineApi.Models;
using VaccineAPI.ModelDTO;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace BrandVaccineApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandVaccineController : ControllerBase
    {
        private readonly Context _db;

        public BrandVaccineController(Context db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BrandVaccineDto>>> GetAll()
        {
            var brandVaccines = await _db.VbTables.OrderBy(x => x.Id).ToListAsync();
            if (brandVaccines == null || !brandVaccines.Any())
            {
                return NotFound(new { message = "No Brand Vaccines found." });
            }
            return Ok(brandVaccines.Select(bv => new BrandVaccineDto
            {
                Id = (int)bv.Id,
                BrandId = (int)bv.BrandId,
                VaccineId = (int)bv.VaccineId
            }));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BrandVaccineDto>> GetById(int id)
        {
            var brandVaccine = await _db.VbTables.FindAsync(id);
            if (brandVaccine == null)
            {
                return NotFound();
            }

            return Ok(new BrandVaccineDto
            {
                Id = (int)brandVaccine.Id,
                BrandId = (int)brandVaccine.BrandId,
                VaccineId = (int)brandVaccine.VaccineId
            });
        }

        [HttpPost]
        public async Task<ActionResult<BrandVaccineDto>> Create(BrandVaccineDto brandVaccineDto)
        {
            var brandVaccine = new VbTable
            {
                BrandId = brandVaccineDto.BrandId,
                VaccineId = brandVaccineDto.VaccineId
            };

            _db.VbTables.Add(brandVaccine);
            await _db.SaveChangesAsync();

            brandVaccineDto.Id = (int)brandVaccine.Id;

            return CreatedAtAction(nameof(GetById), new { id = brandVaccine.Id }, brandVaccineDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, BrandVaccineDto brandVaccineDto)
        {
            if (id != brandVaccineDto.Id)
            {
                return BadRequest();
            }

            var brandVaccine = await _db.VbTables.FindAsync(id);
            if (brandVaccine == null)
            {
                return NotFound();
            }

            brandVaccine.BrandId = brandVaccineDto.BrandId;
            brandVaccine.VaccineId = brandVaccineDto.VaccineId;

            _db.Entry(brandVaccine).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var brandVaccine = await _db.VbTables.FindAsync(id);
            if (brandVaccine == null)
            {
                return NotFound();
            }

            _db.VbTables.Remove(brandVaccine);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Brand Vaccine deleted successfully." });
        }
    }
}