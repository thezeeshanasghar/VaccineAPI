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
    public class VaccineInfoController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public VaccineInfoController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<VaccineInfoDTO>>> GetAll()
        {
            var list = await _db.VaccineInfos.OrderBy(x => x.Name).ToListAsync();
            List<VaccineInfoDTO> listDTO = _mapper.Map<List<VaccineInfoDTO>>(list);
            return new Response<List<VaccineInfoDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<VaccineInfoDTO>> GetSingle(long id)
        {
            var dbVaccineInfo = await _db.VaccineInfos.Where(x => x.Id == id).FirstOrDefaultAsync();
            if (dbVaccineInfo == null)
                return new Response<VaccineInfoDTO>(false, "Not Found", null);

            VaccineInfoDTO vaccineInfoDTO = _mapper.Map<VaccineInfoDTO>(dbVaccineInfo);
            return new Response<VaccineInfoDTO>(true, null, vaccineInfoDTO);
        }

        [HttpPost]
        public async Task<Response<VaccineInfoDTO>> Post(VaccineInfoDTO vaccineInfoDTO)
        {
            VaccineInfo vaccineInfoDb = _mapper.Map<VaccineInfo>(vaccineInfoDTO);
            _db.VaccineInfos.Add(vaccineInfoDb);
            await _db.SaveChangesAsync();
            vaccineInfoDTO.Id = vaccineInfoDb.Id;
            return new Response<VaccineInfoDTO>(true, null, vaccineInfoDTO);
        }

        [HttpPut("{id}")]
        public Response<VaccineInfoDTO> Put(long id, VaccineInfoDTO vaccineInfoDTO)
        {
            var dbVaccineInfo = _db.VaccineInfos.Where(x => x.Id == id).FirstOrDefault();
            if (dbVaccineInfo == null)
            {
                return new Response<VaccineInfoDTO>(false, "VaccineInfo not found", null);
            }

            dbVaccineInfo.Name = vaccineInfoDTO.Name;
            dbVaccineInfo.Icon = vaccineInfoDTO.Icon;
            dbVaccineInfo.ProtectsAgainstHtml = vaccineInfoDTO.ProtectsAgainstHtml;
            dbVaccineInfo.WhoShouldGetItHtml = vaccineInfoDTO.WhoShouldGetItHtml;
            dbVaccineInfo.ScheduleHtml = vaccineInfoDTO.ScheduleHtml;
            dbVaccineInfo.WhyVaccinateHtml = vaccineInfoDTO.WhyVaccinateHtml;
            dbVaccineInfo.ImportantNoteHtml = vaccineInfoDTO.ImportantNoteHtml;
            dbVaccineInfo.BrandsAvailableHtml = vaccineInfoDTO.BrandsAvailableHtml;
            _db.SaveChanges();
            return new Response<VaccineInfoDTO>(true, null, vaccineInfoDTO);
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(long id)
        {
            var dbVaccineInfo = _db.VaccineInfos.Where(x => x.Id == id).FirstOrDefault();
            if (dbVaccineInfo == null)
                return new Response<string>(false, "VaccineInfo not found", null);
            _db.VaccineInfos.Remove(dbVaccineInfo);
            _db.SaveChanges();
            return new Response<string>(true, null, "record deleted");
        }

    }

}
