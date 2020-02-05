using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
using System.IO;
using System.Web;
using System.Globalization;
using System.Net.Http;
using System.Net;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DoctorScheduleController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
        public DoctorScheduleController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<DoctorScheduleDTO>>> GetAll()
        {
            var list = await _db.DoctorSchedules.OrderBy(x=>x.Id).ToListAsync();
            List<DoctorScheduleDTO> listDTO = _mapper.Map<List<DoctorScheduleDTO>>(list);
           
            return new Response<List<DoctorScheduleDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
       public Response<List<DoctorScheduleDTO>> GetSingle(int Id)
        
           
                {

                    List<DoctorSchedule> doctorSchduleDBs = _db.DoctorSchedules.Include("Dose").Include("Doctor").Where(x => x.DoctorId == Id)
                        .OrderBy(x => x.Dose.MinAge).ThenBy(x => x.Dose.Name).ToList();
                    if (doctorSchduleDBs == null || doctorSchduleDBs.Count() == 0)
                        return new Response<List<DoctorScheduleDTO>>(false, "DoctorSchedule not found", null);

                    List<DoctorScheduleDTO> DoctorScheduleDTOs = _mapper.Map<List<DoctorScheduleDTO>>(doctorSchduleDBs);
                    return new Response<List<DoctorScheduleDTO>>(true, null, DoctorScheduleDTOs);
                }
            

        [HttpPost]
        public Response<IEnumerable<DoctorScheduleDTO>> Post(IEnumerable<DoctorScheduleDTO> dsDTOS)
        {
            
                
                    foreach (var DoctorSchedueDTO in dsDTOS)
                    {
                        DoctorSchedule doctorSchduleDB = _mapper.Map<DoctorSchedule>(DoctorSchedueDTO);
                        _db.DoctorSchedules.Add(doctorSchduleDB);
                        _db.SaveChanges();
                        DoctorSchedueDTO.Id = doctorSchduleDB.Id;
                    }
                    return new Response<IEnumerable<DoctorScheduleDTO>>(true, null, dsDTOS);
                
            }

    
        [HttpPut]
        public Response<List<DoctorScheduleDTO>> Put(List<DoctorScheduleDTO> dsDTOS)
        
                
                {
                    foreach (var DoctorSchedueDTO in dsDTOS)
                    {
                        var doctorSchduleDB = _db.DoctorSchedules.Where(c => c.Id == DoctorSchedueDTO.Id).FirstOrDefault();
                        doctorSchduleDB.GapInDays = DoctorSchedueDTO.GapInDays;
                       // _db.SaveChanges();
                    }
                     _db.SaveChanges();
                    return new Response<List<DoctorScheduleDTO>>(true, null, dsDTOS);
                }
            

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.DoctorSchedules.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.DoctorSchedules.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
