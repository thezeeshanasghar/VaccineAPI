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
    public class ScheduleController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ScheduleController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
       
         public async Task<Response<List<ScheduleDTO>>> GetAll()
        {
            var list = await _db.Schedules.OrderBy(x=>x.Id).ToListAsync();
            List<ScheduleDTO> listDTO = _mapper.Map<List<ScheduleDTO>>(list);
           
            return new Response<List<ScheduleDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<Schedule>> GetSingle(long id)
        {
            var single = await _db.Schedules.FindAsync(id);
            if (single == null)
                 return new Response<Schedule>(false, "Not Found", null);
           
            return new Response<Schedule>(true, null, single);

        
        }

        [HttpPost]
        public async Task<ActionResult<Schedule>> Post(Schedule Schedule)
        {
            _db.Schedules.Update(Schedule);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Schedule.Id }, Schedule);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Schedule Schedule)
        {
            if (id != Schedule.Id)
                return BadRequest();

            _db.Entry(Schedule).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Schedules.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Schedules.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
