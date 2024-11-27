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
            var list = await _db.DoctorSchedules.OrderBy(x => x.Id).ToListAsync();
            List<DoctorScheduleDTO> listDTO = _mapper.Map<List<DoctorScheduleDTO>>(list);
            return new Response<List<DoctorScheduleDTO>>(true, null, listDTO);
        }

        // [HttpGet("{id}")]
        // public Response<List<DoctorScheduleDTO>> GetSingle(int Id)
        // {
        //     List<DoctorSchedule> doctorSchduleDBs =
        //      _db.DoctorSchedules.Include("Dose")
        //      .Include("Dose.Vaccine")
        //         .Include("Doctor")
        //         .Where(x => x.DoctorId == Id)
        //         .OrderBy(x => x.Dose.MinAge)
        //         .ThenBy(x => x.Dose.Name).ToList();
        //     if (doctorSchduleDBs == null || doctorSchduleDBs.Count() == 0)
        //         return new Response<List<DoctorScheduleDTO>>(false, "DoctorSchedule not found", null);
        //     List<DoctorScheduleDTO> DoctorScheduleDTOs = _mapper.Map<List<DoctorScheduleDTO>>(doctorSchduleDBs);
        //     return new Response<List<DoctorScheduleDTO>>(true, null, DoctorScheduleDTOs);


        // }

        [HttpGet("{id}")]
        public Response<List<DoctorScheduleDTO>> GetSingle(long id)
        {
            // Check if the doctor has any schedules
            var doctorSchedules = _db.DoctorSchedules
                .Include(ds => ds.Dose)
                .ThenInclude(d => d.Vaccine)
                .Include(ds => ds.Doctor)
                .Where(ds => ds.DoctorId == id)
                .ToList();

            if (doctorSchedules == null || doctorSchedules.Count == 0)
            {
                // If no schedules exist, copy from DoctorId = 1
                var defaultDoctorSchedules = _db.DoctorSchedules
                    .Where(ds => ds.DoctorId == 1)
                    .ToList();

                if (defaultDoctorSchedules == null || defaultDoctorSchedules.Count == 0)
                {
                    return new Response<List<DoctorScheduleDTO>>(false, "Default schedules not found", null);
                }

                // Create schedules for the new doctor
                foreach (var schedule in defaultDoctorSchedules)
                {
                    var newSchedule = new DoctorSchedule
                    {
                        DoctorId = id,
                        DoseId = schedule.DoseId,
                        GapInDays = schedule.GapInDays,
                        IsActive = schedule.IsActive
                    };
                    _db.DoctorSchedules.Add(newSchedule);
                }
                _db.SaveChanges();

                // Fetch the newly created schedules for the new doctor
                doctorSchedules = _db.DoctorSchedules
                    .Include(ds => ds.Dose)
                    .ThenInclude(d => d.Vaccine)
                    .Include(ds => ds.Doctor)
                    .Where(ds => ds.DoctorId == id)
                    .OrderBy(ds => ds.Dose.Name)
                    .ToList();
            }

            // Map and return the schedules
            var doctorScheduleDTOs = _mapper.Map<List<DoctorScheduleDTO>>(doctorSchedules);
            return new Response<List<DoctorScheduleDTO>>(true, null, doctorScheduleDTOs);
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

                var dose = _db.Doses.SingleOrDefault(a => a.Id == DoctorSchedueDTO.DoseId);
                var vac = _db.Vaccines.Where(a => a.Id == dose.VaccineId).FirstOrDefault();
                var brand = _db.Brands.Where(a => a.VaccineId == vac.Id).FirstOrDefault();
                var count = 0;
                var amount = 0;
                var brandAmount = new BrandAmount
                {
                    BrandId = brand.Id,
                    Count = count,
                    Amount = amount,
                    DoctorId = DoctorSchedueDTO.DoctorId,
                };

                _db.BrandAmounts.Add(brandAmount);
                _db.SaveChanges();
            }
            return new Response<IEnumerable<DoctorScheduleDTO>>(true, null, dsDTOS);

        }

        [HttpPut]
        public Response<List<DoctorSchedule>> Put([FromBody] List<DoctorSchedule> dsDTOS)
        {
            foreach (var DoctorScheduedTO in dsDTOS)
            {
                var doctorSchduleDB = _db.DoctorSchedules.Where(c => c.Id == DoctorScheduedTO.Id).FirstOrDefault();
                doctorSchduleDB.GapInDays = DoctorScheduedTO.GapInDays;
                doctorSchduleDB.IsActive = DoctorScheduedTO.IsActive;
            }
            _db.SaveChanges();
            return new Response<List<DoctorSchedule>>(true, null, dsDTOS);
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.DoctorSchedules.FindAsync(id);
            if (obj == null) return NotFound();
            _db.DoctorSchedules.Remove(obj);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
