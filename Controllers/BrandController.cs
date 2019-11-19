using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandController(Context context , IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
            
        }

        [HttpGet]
 public async Task<Response<List<BrandDTO>>> GetAll()
        {
            var list = await _db.Brands.Include(x=>x.Vaccine).OrderBy(x=>x.Id).ToListAsync();
            List<BrandDTO> listDTO = _mapper.Map<List<BrandDTO>>(list);
           
            return new Response<List<BrandDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<BrandDTO>> GetSingle(long id)
        {
            var dbbrand = await _db.Brands.Include(x=>x.Vaccine).FirstOrDefaultAsync();

           BrandDTO brandDTO = _mapper.Map<BrandDTO>(dbbrand);
           
            if (dbbrand == null)
            return new Response<BrandDTO>(false, "Not Found", null);
           
            return new Response<BrandDTO>(true, null, brandDTO);
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
