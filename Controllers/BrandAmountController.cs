using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandAmountController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandAmountController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
       public async Task<Response<List<BrandAmountDTO>>> GetAll()
        {
            var list = await _db.BrandAmounts.OrderBy(x=>x.Id).ToListAsync();
            List<BrandAmountDTO> listDTO = _mapper.Map<List<BrandAmountDTO>>(list);
           
            return new Response<List<BrandAmountDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<BrandAmount>> GetSingle(long id)
        {
            var single = await _db.BrandAmounts.FindAsync(id);
            if (single == null)
                 return new Response<BrandAmount>(false, "Not Found", null);
           
                 return new Response<BrandAmount>(true, null, single);
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
