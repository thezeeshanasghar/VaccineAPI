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
        public async Task<Response<VaccineDTO>> GetSingle(long id)
        {
            
        
            var dbvaccine = await _db.Vaccines.FindAsync(id);
            VaccineDTO vaccineDTO = _mapper.Map<VaccineDTO>(dbvaccine);
           
            if (dbvaccine == null)
            return new Response<VaccineDTO>(false, "Not Found", null);
           
            return new Response<VaccineDTO>(true, null, vaccineDTO);
        }

         [HttpGet("{id}/dosses")]
        public async Task<Response<List<DoseDTO>>> GetDosses(long id)
        {
            
        
            var dbvaccine = await _db.Vaccines.Include(x=>x.Doses).FirstOrDefaultAsync(x=> x.Id == id);
           
            if (dbvaccine == null)
            return new Response<List<DoseDTO>>(false, "Vaccine Not Found", null);
           
           else {
               var dbDosses = dbvaccine.Doses;
            var dossesDTOs = _mapper.Map<List<DoseDTO>>(dbDosses);
            return new Response<List<DoseDTO>>(true, null, dossesDTOs);
           }
        }

         [HttpGet("{id}/brands")]
        public async Task<Response<List<BrandDTO>>> GetBrands(long id)
        {
            
        
            var dbvaccine = await _db.Vaccines.Include(x=>x.Brands).FirstOrDefaultAsync(x=> x.Id == id);
           
            if (dbvaccine == null)
            return new Response<List<BrandDTO>>(false, "Vaccine Not Found", null);
           
           else {
               var dbBrands = dbvaccine.Brands;
            var brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
            return new Response<List<BrandDTO>>(true, null, brandDTOs);
           }
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
