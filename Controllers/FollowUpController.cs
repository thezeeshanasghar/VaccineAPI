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
    public class FollowUpController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public FollowUpController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
      public async Task<Response<List<FollowUpDTO>>> GetAll()
        {
            var list = await _db.FollowUps.OrderBy(x=>x.Id).ToListAsync();
            List<FollowUpDTO> listDTO = _mapper.Map<List<FollowUpDTO>>(list);
           
            return new Response<List<FollowUpDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<FollowUp>> GetSingle(long id)
        {
            var single = await _db.FollowUps.FindAsync(id);
            if (single == null)
             return new Response<FollowUp>(false, "Not Found", null);
           
             return new Response<FollowUp>(true, null, single);   

            
        }
         [HttpGet("alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<FollowUpDTO>> GetAlert(DateTime inputDate, int GapDays, long OnlineClinicId)
        {
                {
                    var doctor = _db.Clinics.Where(x => x.Id == OnlineClinicId).Include(x=>x.Doctor).First<Clinic>().Doctor;
                    long[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<long>();
                  
                  //  int[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<int>();

                    IEnumerable<FollowUp> followups = new List<FollowUp>();
                    DateTime AddedDateTime = DateTime.UtcNow.AddHours(5).AddDays(GapDays);
                    DateTime pakistanTime = DateTime.UtcNow.AddHours(5);
                    if (GapDays == 0)
                        followups = _db.FollowUps.Include(x=> x.Child).ThenInclude(x=>x.User)
                            .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                       //     .Where(c => System.Data.Entity.DbFunctions.TruncateTime(c.NextVisitDate) == System.Data.Entity.DbFunctions.TruncateTime(pakistanTime))
                           .Where(c => c.NextVisitDate == inputDate.Date)
                            .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate).ToList<FollowUp>();
                    else if (GapDays > 0)
                    {
                        AddedDateTime = AddedDateTime.AddDays(1);
                        followups = _db.FollowUps.Include(x=> x.Child).ThenInclude(x=>x.User)
                        
                            .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                            .Where(c => c.NextVisitDate > pakistanTime && c.NextVisitDate <= AddedDateTime)
                            .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate)
                            .ToList<FollowUp>();

                    }
                    else if (GapDays < 0)
                    {
                        followups = _db.FollowUps.Include(x=> x.Child).ThenInclude(x=>x.User)
                        //    .Where(c => ClinicIDs.Contains(c.Child.ClinicID))
                            .Where(c => c.NextVisitDate < pakistanTime.Date && c.NextVisitDate >= AddedDateTime)
                            .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate)
                            .ToList<FollowUp>();
                    }
                        
                    IEnumerable<FollowUpDTO> followUpDTO = _mapper.Map<IEnumerable<FollowUpDTO>>(followups);
                    return new Response<IEnumerable<FollowUpDTO>>(true, null, followUpDTO);
                }
            }

             [HttpGet("sms-alert/{childId}")]
        public Response<FollowUpDTO> SendSMSAlertToOneChild(int childId)
        {
            
                {
                    var dbChildFollowup = _db.FollowUps.Where(x => x.ChildId == childId).OrderByDescending(x => x.Id).FirstOrDefault();
                    UserSMS u = new UserSMS(_db);
                    u.ParentFollowUpSMSAlert(dbChildFollowup);
                    FollowUpDTO followupDTO = _mapper.Map<FollowUpDTO>(dbChildFollowup);
                    return new Response<FollowUpDTO>(true, null, followupDTO);
                }

            }

        [HttpPost]
        public Response<FollowUpDTO> Post(FollowUpDTO FollowUpDto)
        
            {
                {
                    FollowUp dbFollowUp = _mapper.Map<FollowUp>(FollowUpDto);
                    _db.FollowUps.Add(dbFollowUp);
                    _db.SaveChanges();
                    return new Response<FollowUpDTO>(true, null, FollowUpDto);
                }
            }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, FollowUp FollowUp)
        {
            if (id != FollowUp.Id)
                return BadRequest();

            _db.Entry(FollowUp).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.FollowUps.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.FollowUps.Remove(obj);
            await _db.SaveChangesAsync();

            return Ok(new { Message = "Follow-up deleted successfully." });
        }
    }
}
