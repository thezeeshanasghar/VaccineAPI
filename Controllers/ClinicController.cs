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
    public class ClinicController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ClinicController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
         public async Task<Response<List<ClinicDTO>>> GetAll()
        {
            var list = await _db.Clinics.Include(x=>x.ClinicTimings).OrderBy(x=>x.Id).ToListAsync();
            //var list = await _db.Clinics.OrderBy(x=>x.Id).ToListAsync();
            List<ClinicDTO> listDTO = _mapper.Map<List<ClinicDTO>>(list);
           
            return new Response<List<ClinicDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
      public async Task<Response<ClinicDTO>> GetSingle(long id)
        {
            var dbclinic = await _db.Clinics.Include(x=>x.ClinicTimings).FirstOrDefaultAsync();

           ClinicDTO clinicDTO = _mapper.Map<ClinicDTO>(dbclinic);
           
            if (dbclinic == null)
            return new Response<ClinicDTO>(false, "Not Found", null);
           
            return new Response<ClinicDTO>(true, null, clinicDTO);
        }

        [HttpPost]
        public async Task<ActionResult<Clinic>> Post(Clinic Clinic)
        {
            _db.Clinics.Update(Clinic);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Clinic.Id }, Clinic);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Clinic Clinic)
        {
            if (id != Clinic.Id)
                return BadRequest();

            _db.Entry(Clinic).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Clinics.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Clinics.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
