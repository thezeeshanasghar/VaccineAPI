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
    public class BrandInventoryController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandInventoryController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<BrandInventoryDTO>>> GetAll()
        {
            var list = await _db.BrandInventorys.OrderBy(x=>x.Id).ToListAsync();
            List<BrandInventoryDTO> listDTO = _mapper.Map<List<BrandInventoryDTO>>(list);
           
            return new Response<List<BrandInventoryDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<BrandInventory>> GetSingle(long id)
        {
            var single = await _db.BrandInventorys.FindAsync(id);
            if (single == null)
                 return new Response<BrandInventory>(false, "Not Found", null);
           
                 return new Response<BrandInventory>(true, null, single);
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
