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
    public class FollowUpController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public FollowUpController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
      public async Task<Response<List<FollowUpDTO>>> GetAll()
        {
            var list = await _db.FollowUps.OrderBy(x=>x.Id).ToListAsync();
            List<FollowUpDTO> listDTO = _mapper.Map<List<FollowUpDTO>>(list);
           
            return new Response<List<FollowUpDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<FollowUp>> GetSingle(long id)
        {
            var single = await _db.FollowUps.FindAsync(id);
            if (single == null)
             return new Response<FollowUp>(false, "Not Found", null);
           
             return new Response<FollowUp>(true, null, single);   

            
        }

        [HttpPost]
        public async Task<ActionResult<FollowUp>> Post(FollowUp FollowUp)
        {
            _db.FollowUps.Update(FollowUp);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = FollowUp.Id }, FollowUp);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, FollowUp FollowUp)
        {
            if (id != FollowUp.Id)
                return BadRequest();

            _db.Entry(FollowUp).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.FollowUps.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.FollowUps.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
