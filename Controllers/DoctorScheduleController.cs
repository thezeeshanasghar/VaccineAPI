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

        // GET: api/DoctorSchedule
        [HttpGet]
        public async Task<Response<List<DoctorScheduleDTO>>> GetAll()
        {
            var list = await _db.DoctorSchedules.OrderBy(x => x.Id).ToListAsync();
            List<DoctorScheduleDTO> listDTO = _mapper.Map<List<DoctorScheduleDTO>>(list);
            return new Response<List<DoctorScheduleDTO>>(true, null, listDTO);
        }

        // GET: api/DoctorSchedule/5
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

            var sortedDoctorSchedules = doctorSchedules
                .OrderBy(ds => ds.Dose.Name) // Ensure that Dose.Name is not null and sortable
                .ToList();

            var doctorScheduleDTOs = _mapper.Map<List<DoctorScheduleDTO>>(sortedDoctorSchedules);
            return new Response<List<DoctorScheduleDTO>>(true, null, doctorScheduleDTOs);
        }

        // POST: api/DoctorSchedule
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

        // PUT: api/DoctorSchedule/UpdateInvoiceId/5
       // ... existing code ...
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

// [HttpPut("UpdateInvoiceId/{id}")]
// public async Task<IActionResult> UpdateInvoiceId(long id, [FromBody] long? invoiceId)
// {
//     // Find the doctor schedule by ID
//     var doctorSchedule = await _db.DoctorSchedules.FindAsync(id);
//     if (doctorSchedule == null)
//     {
//         // Return 404 Not Found if the schedule does not exist
//         return NotFound(new { message = "DoctorSchedule not found." });
//     }

//     // Update the InvoiceId
//     doctorSchedule.Invoice = invoiceId.HasValue ? invoiceId.Value : (long?)null;
//     _db.Entry(doctorSchedule).State = EntityState.Modified;

//     try
//     {
//         // Save changes to the database
//         await _db.SaveChangesAsync();
//     }
//     catch (DbUpdateConcurrencyException)
//     {
//         if (!DoctorScheduleExists(id))
//         {
//             // Return 404 Not Found if the schedule no longer exists
//             return NotFound(new { message = "DoctorSchedule not found during update." });
//         }
//         else
//         {
//             // Re-throw the exception if it is a different concurrency issue
//             throw;
//         }
//     }

//     // Return 200 OK with the updated schedule
//     return Ok(new { message = "InvoiceId updated successfully.", doctorSchedule });
// }

// private bool DoctorScheduleExists(long id)
// {
//     return _db.DoctorSchedules.Any(e => e.Id == id);
// }






private bool DoctorScheduleExists(long id)
{
    return _db.DoctorSchedules.Any(e => e.Id == id);
}

        // PUT: api/DoctorSchedule
        [HttpPut]
        public Response<List<DoctorSchedule>> Put([FromBody] List<DoctorSchedule> dsDTOS)
        {
            foreach (var DoctorScheduedTO in dsDTOS)
            {
                var doctorSchduleDB = _db.DoctorSchedules.FirstOrDefault(c => c.Id == DoctorScheduedTO.Id);
                if (doctorSchduleDB != null)
                {
                    doctorSchduleDB.GapInDays = DoctorScheduedTO.GapInDays;
                    doctorSchduleDB.IsActive = DoctorScheduedTO.IsActive;
                }
            }
            _db.SaveChanges();
            return new Response<List<DoctorSchedule>>(true, null, dsDTOS);
        }

        // DELETE: api/DoctorSchedule/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.DoctorSchedules.FindAsync(id);
            if (obj == null) return NotFound();

            _db.DoctorSchedules.Remove(obj);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private bool DoctorScheduleExists(int id)
        {
            return _db.DoctorSchedules.Any(e => e.Id == id);
        }
    }
}
