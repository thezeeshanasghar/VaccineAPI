using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VaccineController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;


        public VaccineController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<VaccineDTO>>> GetAll()
        {
            var list = await _db.Vaccines.OrderBy(x=>x.MinAge).ToListAsync();
            List<VaccineDTO> listDTO = _mapper.Map<List<VaccineDTO>>(list);
            foreach(var dto in listDTO)
            {
                var vaccine = _db.Vaccines.Where(x => x.Id == dto.Id).First();
                if(vaccine.Brands!=null)
                    dto.NumOfBrands = vaccine.Brands.Count();
                else
                    dto.NumOfBrands =0;
                // dto.NumOfDoses = _db.Vaccines.Where(x => x.Id == dto.Id).First().Doses.Count();
            }
            return new Response<List<VaccineDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<Vaccine>> GetSingle(long id)
        {
            
            var vaccine = await _db.Vaccines.FindAsync(id);
           
            if (vaccine == null)
            return new Response<Vaccine>(false, "Not Found", null);
           
            return new Response<Vaccine>(true, null, vaccine);
        }

        [HttpPost]
        public async Task<ActionResult<Vaccine>> Post(Vaccine Vaccine)
        {
            _db.Vaccines.Update(Vaccine);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Vaccine.Id }, Vaccine);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Vaccine Vaccine)
        {
            if (id != Vaccine.Id)
                return BadRequest();

            _db.Entry(Vaccine).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Vaccines.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Vaccines.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
