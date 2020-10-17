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
    public class ClinicTimingController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ClinicTimingController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<ClinicTimingDTO>>> GetAll()
        {
            var list = await _db.ClinicTimings.OrderBy(x=>x.Id).ToListAsync();
            List<ClinicTimingDTO> listDTO = _mapper.Map<List<ClinicTimingDTO>>(list);
           
            return new Response<List<ClinicTimingDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
       public async Task<Response<ClinicTimingDTO>> GetSingle(long id)
        {
            var dbclinictiming = await _db.ClinicTimings.FirstOrDefaultAsync();

           ClinicTimingDTO clinictimingDTO = _mapper.Map<ClinicTimingDTO>(dbclinictiming);
           
            if (dbclinictiming == null)
            return new Response<ClinicTimingDTO>(false, "Not Found", null);
           
            return new Response<ClinicTimingDTO>(true, null, clinictimingDTO);
        }

        [HttpPost]
        public async Task<ActionResult<ClinicTiming>> Post(ClinicTiming ClinicTiming)
        {
            _db.ClinicTimings.Update(ClinicTiming);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = ClinicTiming.Id }, ClinicTiming);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, ClinicTiming ClinicTiming)
        {
            if (id != ClinicTiming.Id)
                return BadRequest();

            _db.Entry(ClinicTiming).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.ClinicTimings.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.ClinicTimings.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
