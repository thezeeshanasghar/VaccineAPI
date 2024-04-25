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
    public class ClinicTimingController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ClinicTimingController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<ClinicTimingDTO>>> GetAll()
        {
            var list = await _db.ClinicTimings.OrderBy(x=>x.Id).ToListAsync();
            List<ClinicTimingDTO> listDTO = _mapper.Map<List<ClinicTimingDTO>>(list);
           
            return new Response<List<ClinicTimingDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
       public async Task<Response<ClinicTimingDTO>> GetSingle(long id)
        {
            var dbclinictiming = await _db.ClinicTimings.FirstOrDefaultAsync();

           ClinicTimingDTO clinictimingDTO = _mapper.Map<ClinicTimingDTO>(dbclinictiming);
           
            if (dbclinictiming == null)
            return new Response<ClinicTimingDTO>(false, "Not Found", null);
           
            return new Response<ClinicTimingDTO>(true, null, clinictimingDTO);
        }

        [HttpPost]
        public async Task<ActionResult<ClinicTiming>> Post(ClinicTiming ClinicTiming)
        {
            _db.ClinicTimings.Update(ClinicTiming);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = ClinicTiming.Id }, ClinicTiming);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, ClinicTiming ClinicTiming)
        {
            if (id != ClinicTiming.Id)
                return BadRequest();

            _db.Entry(ClinicTiming).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.ClinicTimings.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.ClinicTimings.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
    
        }

        [Route("api/clintimings/{clinicId}")]
        [HttpPatch]
        public async Task<IActionResult> UpdateClinicTimings(long clinicId, [FromBody] List<ClinicTiming> updatedTimings)
        {
            try
            {
                if (updatedTimings == null || !updatedTimings.Any())
                {
                    return BadRequest("No updated clinic timings provided.");
                }

                var timingIds = updatedTimings.Select(t => t.Id).ToList();

                var existingTimings = await _db.ClinicTimings.Where(t => timingIds.Contains(t.Id) && t.ClinicId == clinicId).ToListAsync();

                if (existingTimings == null || existingTimings.Count == 0)
                {
                    return NotFound();
                }

                foreach (var updatedTiming in updatedTimings)
                {
                    var existingTiming = existingTimings.FirstOrDefault(t => t.Id == updatedTiming.Id);

                    if (existingTiming != null)
                    {
                        existingTiming.Day = updatedTiming.Day;
                        existingTiming.Session = updatedTiming.Session;
                        existingTiming.StartTime = updatedTiming.StartTime;
                        existingTiming.EndTime = updatedTiming.EndTime;
                        existingTiming.ClinicId = updatedTiming.ClinicId;
                    }
                }

                await _db.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }




        ////////// Updated APi
        public class ClinicIdsRequestModel
        {
            public List<long> ClinicIds { get; set; }
        }
        [HttpPatch("children/schedules")]
        public async Task<ActionResult<IEnumerable<long>>> GetChildIdsWithSchedulesFromClinic([FromBody] ClinicIdsRequestModel model, [FromQuery] string fromDate, [FromQuery] string toDate)
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);

                if (model == null || model.ClinicIds == null || !model.ClinicIds.Any())
                {
                    return BadRequest("No clinic IDs provided in the request.");
                }

                List<long> childIdsWithSchedules = new List<long>();


                foreach (var id in model.ClinicIds)
                {
                    var childIds = await _db.Childs
                                            .Where(c => c.ClinicId == id)
                                            .Select(c => c.Id)
                                            .ToListAsync();

                    if (childIds == null || !childIds.Any())
                    {

                        continue;
                    }


                    foreach (var childId in childIds)
                    {
                        var schedules = await _db.Schedules
                                                .Where(c => c.ChildId == childId && c.Date >= parsedFromDate && c.Date <= parsedToDate)
                                                .ToListAsync();
                        if (schedules.Any())
                        {
                            var daysToAdd = (parsedToDate - parsedFromDate).Days + 1;
                            foreach (var schedule in schedules)
                            {
                                schedule.Date = schedule.Date.AddDays(daysToAdd);
                            }
                            await _db.SaveChangesAsync();
                        }

                        childIdsWithSchedules.Add(childId);
                    }
                }

                return Ok(childIdsWithSchedules);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while retrieving child IDs: {ex.Message}");
            }
        }



        public class ClinicUpdateRequest
        {
            public ClinicDTO Clinic { get; set; }
            public List<ClinicTiming> ClinicTimings { get; set; }
        }

        [Route("api/clinic/update")]
        [HttpPut]
        public async Task<IActionResult> UpdateClinicAndTimings(long clinicId, [FromBody] ClinicUpdateRequest request)
        {
            try
            {
                // Update clinic data
                var dbClinic = await _db.Clinics.FindAsync(clinicId);
                if (dbClinic == null)
                {
                    return NotFound("Clinic not found.");
                }

                dbClinic.Name = request.Clinic.Name;
                dbClinic.ConsultationFee = request.Clinic.ConsultationFee;
                dbClinic.PhoneNumber = request.Clinic.PhoneNumber;
                dbClinic.Address = request.Clinic.Address;
                dbClinic.MonogramImage = request.Clinic.MonogramImage;
                dbClinic.IsOnline = request.Clinic.IsOnline;

                // Update clinic timings
                var timingIds = request.ClinicTimings.Select(t => t.Id).ToList();
                var existingTimings = await _db.ClinicTimings
                    .Where(t => timingIds.Contains(t.Id) && t.ClinicId == dbClinic.Id)
                    .ToListAsync();

                foreach (var updatedTiming in request.ClinicTimings)
                {
                    var existingTiming = existingTimings.FirstOrDefault(t => t.Id == updatedTiming.Id);

                    if (existingTiming != null)
                    {
                        existingTiming.Day = updatedTiming.Day;
                        existingTiming.Session = updatedTiming.Session;
                        existingTiming.StartTime = updatedTiming.StartTime;
                        existingTiming.EndTime = updatedTiming.EndTime;
                        existingTiming.ClinicId = dbClinic.Id;
                    }
                }

                await _db.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


    }

}
