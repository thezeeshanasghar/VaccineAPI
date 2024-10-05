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
            var doctorSchedules = _db.DoctorSchedules
                .Include(ds => ds.Dose)
                .ThenInclude(d => d.Vaccine)
                .Include(ds => ds.Doctor)
                .Where(ds => ds.DoctorId == id)
                .ToList();

            if (doctorSchedules == null || doctorSchedules.Count == 0)
                return new Response<List<DoctorScheduleDTO>>(false, "DoctorSchedule not found", null);

            // Order the schedules by Dose Name
            var sortedDoctorSchedules = doctorSchedules
                .OrderBy(ds => ds.Dose.Name) // Ensure that Dose.Name is not null and sortable
                .ToList();

            var doctorScheduleDTOs = _mapper.Map<List<DoctorScheduleDTO>>(sortedDoctorSchedules);
            return new Response<List<DoctorScheduleDTO>>(true, null, doctorScheduleDTOs);
        }
        [HttpPut("UpdateInvoiceId/{id}")]
        public async Task<IActionResult> UpdateInvoiceId(long id, [FromBody] long invoiceId)
        {
            var doctorSchedule = await _db.DoctorSchedules.FindAsync(id);
            if (doctorSchedule == null)
            {
                return NotFound(new { message = "DoctorSchedule not found." });
            }
            doctorSchedule.InvoiceId = invoiceId;
            _db.Entry(doctorSchedule).State = EntityState.Modified;
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DoctorScheduleExists(id))
                {
                    return NotFound(new { message = "DoctorSchedule not found during update." });
                }
                else
                {
                    throw;
                }
            }

            return Ok(new { message = "InvoiceId updated successfully.", doctorSchedule });
        }
        private bool DoctorScheduleExists(long id)
        {
            return _db.DoctorSchedules.Any(e => e.Id == id);
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
