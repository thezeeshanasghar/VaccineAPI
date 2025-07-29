using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// using VaccineBrand.DTOs;
using VaccineAPI.Models;
// using VaccineBrandApi.Models;
using VaccineAPI.ModelDTO;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace VaccineBrandApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VaccineBrandController : ControllerBase
    {
        private readonly Context _db;

        public VaccineBrandController(Context db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<VaccineBrandDto>>> GetAll()
        {
            var VaccineBrands = await _db.VaccineBrands.OrderBy(x => x.Id).ToListAsync();
            if (VaccineBrands == null || !VaccineBrands.Any())
            {
                return NotFound(new { message = "No Brand Vaccines found." });
            }
            return Ok(VaccineBrands.Select(bv => new VaccineBrandDto
            {
                Id = (int)bv.Id,
                BrandId = (int)bv.BrandId,
                VaccineId = (int)bv.VaccineId
            }));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<VaccineBrandDto>> GetById(int id)
        {
            var VaccineBrand = await _db.VaccineBrands.FindAsync(id);
            if (VaccineBrand == null)
            {
                return NotFound();
            }

            return Ok(new VaccineBrandDto
            {
                Id = (int)VaccineBrand.Id,
                BrandId = (int)VaccineBrand.BrandId,
                VaccineId = (int)VaccineBrand.VaccineId
            });
        }

        [HttpPost]
        public async Task<ActionResult<VaccineBrandDto>> Create(VaccineBrandDto VaccineBrandDto)
        {
            var VaccineBrand = new VaccineBrand
            {
                BrandId = VaccineBrandDto.BrandId,
                VaccineId = VaccineBrandDto.VaccineId
            };

            _db.VaccineBrands.Add(VaccineBrand);
            await _db.SaveChangesAsync();

            VaccineBrandDto.Id = (int)VaccineBrand.Id;

            return CreatedAtAction(nameof(GetById), new { id = VaccineBrand.Id }, VaccineBrandDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, VaccineBrandDto VaccineBrandDto)
        {
            if (id != VaccineBrandDto.Id)
            {
                return BadRequest();
            }

            var VaccineBrand = await _db.VaccineBrands.FindAsync(id);
            if (VaccineBrand == null)
            {
                return NotFound();
            }

            VaccineBrand.BrandId = VaccineBrandDto.BrandId;
            VaccineBrand.VaccineId = VaccineBrandDto.VaccineId;

            _db.Entry(VaccineBrand).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var VaccineBrand = await _db.VaccineBrands.FindAsync(id);
            if (VaccineBrand == null)
            {
                return NotFound();
            }

            _db.VaccineBrands.Remove(VaccineBrand);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Brand Vaccine deleted successfully." });
        }
    }
}