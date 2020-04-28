using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using AutoMapper;


namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DoseController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public DoseController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<DoseDTO>>> GetAll()
        {
            var list = await _db.Doses.OrderBy(x=>x.Id).ToListAsync();
            List<DoseDTO> listDTO = _mapper.Map<List<DoseDTO>>(list);
           
            return new Response<List<DoseDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<DoseDTO>> GetSingle(long id)
        {
            var dbdose = await _db.Doses.Where(x=> x.Id == id).FirstOrDefaultAsync();

           DoseDTO doseDTO = _mapper.Map<DoseDTO>(dbdose);
           
            if (dbdose == null)
            return new Response<DoseDTO>(false, "Not Found", null);
           
            return new Response<DoseDTO>(true, null, doseDTO);
        }

        [HttpPost]
        public Response<DoseDTO> Post(DoseDTO doseDTO)
        {
             Dose doseDb = _mapper.Map<Dose>(doseDTO);
                    _db.Doses.Add(doseDb);
                    _db.SaveChanges();
                    doseDTO.Id = doseDb.Id;
                    return new Response<DoseDTO>(true, null, doseDTO);
        }

        [HttpPut("{id}")]
        public Response<DoseDTO> Put(int Id, DoseDTO doseDTO)
        {
            var dbDose = _db.Doses.Where(c => c.Id == Id).FirstOrDefault();
                    dbDose.Name = doseDTO.Name;
                    dbDose.MinAge = doseDTO.MinAge;
                    dbDose.MaxAge = doseDTO.MaxAge;
                    dbDose.MinGap = doseDTO.MinGap;
                    dbDose.DoseOrder = doseDTO.DoseOrder;
                    dbDose.IsSpecial = doseDTO.IsSpecial;
                    _db.SaveChanges();
                    return new Response<DoseDTO>(true, null, doseDTO);
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id)
        {
            var dbDose = _db.Doses.Where(c => c.Id == Id).FirstOrDefault();
                    _db.Doses.Remove(dbDose);
                    _db.SaveChanges();
                    return new Response<string>(true, null, "record deleted");
        }
    }
}
