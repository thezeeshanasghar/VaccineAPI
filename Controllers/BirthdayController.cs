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
    public class BirthdayController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BirthdayController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        // [HttpGet]
        // public async Task<Response<List<FollowUpDTO>>> GetAll()
        // {
        //     var list = await _db.Schedules.OrderBy(x => x.Id).ToListAsync();
        //     List<FollowUpDTO> listDTO = _mapper.Map<List<FollowUpDTO>>(list);

        //     return new Response<List<FollowUpDTO>>(true, null, listDTO);
        // }

        // [HttpGet("{id}")]
        // public async Task<Response<FollowUp>> GetSingle(long id)
        // {
        //     var single = await _db.FollowUps.FindAsync(id);
        //     if (single == null)
        //         return new Response<FollowUp>(false, "Not Found", null);

        //     return new Response<FollowUp>(true, null, single);
        // }
        [HttpGet("birthday/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ScheduleDTO>>
        GetBirthdayAlert(int GapDays, long OnlineClinicId)
        {
            List<Schedule> schedules = GetBirthdayAlertData(GapDays, OnlineClinicId, _db);
            IEnumerable<ScheduleDTO> scheduleDTO = _mapper.Map<IEnumerable<ScheduleDTO>>(schedules);
            return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDTO);
        }

        private static List<Schedule> GetBirthdayAlertData(int GapDays, long OnlineClinicId, Context db)
        {
            List<Schedule> schedules = new List<Schedule>();
            var doctor = db.Clinics
                    .Where(x => x.Id == OnlineClinicId)
                    .Include(x => x.Doctor)
                    .First<Clinic>()
                    .Doctor;
            var clinics = db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();

            // long[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<long>();
            long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
            DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
            DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
            DateTime NextDayTime = (CurrentPakDateTime.AddDays(1)).Date;

            if (GapDays == 0)
            {
                schedules =
                    db
                        .Schedules
                        .Include(x => x.Child)
                        .ThenInclude(x => x.User)
                        .Include(x => x.Dose)
                        .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                        .Where(c => c.Date.Date == CurrentPakDateTime.Date)
                        .Where(c => c.IsDone != true && c.IsSkip != true)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(x => x.Date)
                        .ToList<Schedule>();

                var sc =
                    db
                        .Schedules
                        .Include(c => c.Child)
                        .ThenInclude(c => c.User)
                        .Include(c => c.Dose)
                        .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                        .Where(c => c.Child.PreferredDayOfReminder != 0)
                        .Where(c => c.Date == NextDayTime.AddMinutes(-1)) //.AddDays (c.Child.PreferredDayOfReminder
                        .Where(c => c.IsDone != true && c.IsSkip != true)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(x => x.Date)
                        .ToList<Schedule>();

                schedules.AddRange(sc);
            }
            else if (GapDays > 0)
            {
                AddedDateTime = AddedDateTime.AddDays(1);
                schedules = db.Schedules
                        .Include(x => x.Child)
                        .ThenInclude(x => x.User)
                        .Include(x => x.Dose)
                        .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                        .Where(c =>
                            c.Date.Date > CurrentPakDateTime.Date &&
                            c.Date.Date <= AddedDateTime.Date)
                        .Where(c => c.IsDone != true && c.IsSkip != true)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(x => x.Date)
                        .ToList<Schedule>();
            }
            else if (GapDays < 0)
            {
                schedules = db.Schedules
                        .Include(x => x.Child)
                        .ThenInclude(x => x.User)
                        .Include(x => x.Dose)
                        .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                        .Where(c =>
                            c.Date < CurrentPakDateTime.Date &&
                            c.Date >= AddedDateTime)
                        .Where(c => c.IsDone != true && c.IsSkip != true)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(x => x.Date)
                        .ToList<Schedule>();
            }

            List<Schedule> listOfSchedules = new List<Schedule>();
            listOfSchedules = removeDuplicateRecords(schedules, map);

            return listOfSchedules;
        }

    }
}
