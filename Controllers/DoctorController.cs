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
    public class DoctorController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public DoctorController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<DoctorDTO>>> GetAll()
        {
            var list = await _db.Doctors.OrderBy(x=>x.Id).ToListAsync();
            List<DoctorDTO> listDTO = _mapper.Map<List<DoctorDTO>>(list);
           
            return new Response<List<DoctorDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<DoctorDTO>> GetSingle(long id)
        {
            var single = await _db.Doctors.FindAsync(id);
            
            if (single == null)
                return new Response<DoctorDTO>(false, "Not Found", null);
           
                 return new Response<DoctorDTO>(true, null, singleDTO);
        }

        [HttpPost]
        public async Task<ActionResult<Doctor>> Post(Doctor Doctor)
        {
            _db.Doctors.Update(Doctor);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Doctor.Id }, Doctor);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Doctor Doctor)
        {
            if (id != Doctor.Id)
                return BadRequest();

            _db.Entry(Doctor).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Doctors.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Doctors.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
