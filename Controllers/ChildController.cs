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
    public class ChildController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ChildController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
       public async Task<Response<List<ChildDTO>>> GetAll()
        {
            var list = await _db.Childs.OrderBy(x=>x.Id).ToListAsync();
            List<ChildDTO> listDTO = _mapper.Map<List<ChildDTO>>(list);
           
            return new Response<List<ChildDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<Child>> GetSingle(long id)
        {
            var single = await _db.Childs.FindAsync(id);
            if (single == null)
            return new Response<Child>(false, "Not Found", null);
           
                 return new Response<Child>(true, null, single);
        }

        [HttpPost]
        public async Task<ActionResult<Child>> Post(Child Child)
        {
            _db.Childs.Update(Child);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Child.Id }, Child);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Child Child)
        {
            if (id != Child.Id)
                return BadRequest();

            _db.Entry(Child).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Childs.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Childs.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
