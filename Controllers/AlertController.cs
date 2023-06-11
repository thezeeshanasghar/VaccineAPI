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
    public class AlertController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public AlertController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        // [HttpGet("alert/{GapDays}/{OnlineClinicId}")]
        // public Response<IEnumerable<ScheduleDTO>>
        // GetAlert(int GapDays, long OnlineClinicId)
        // {
        //     {
        //         List<Schedule> schedules =
        //             GetAlertData(GapDays, OnlineClinicId, _db);
        //         IEnumerable<ScheduleDTO> scheduleDTO =
        //             _mapper.Map<IEnumerable<ScheduleDTO>>(schedules);
        //         return new Response<IEnumerable<ScheduleDTO>>(true,
        //             null,
        //             scheduleDTO);
        //     }
        // }

        // private static List<Schedule> GetAlertData(int GapDays, long OnlineClinicId, Context db)
        // {
        //     List<Schedule> schedules = new List<Schedule>();
        //     var doctor =
        //         db
        //             .Clinics
        //             .Where(x => x.Id == OnlineClinicId)
        //             .Include(x => x.Doctor)
        //             .First<Clinic>()
        //             .Doctor;
        //     var clinics =
        //         db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();

        //     // long[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<long>();
        //     long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
        //     DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
        //     DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
        //     DateTime NextDayTime = (CurrentPakDateTime.AddDays(1)).Date;

        //     if (GapDays == 0)
        //     {
        //         schedules =
        //             db
        //                 .Schedules
        //                 .Include(x => x.Child)
        //                 .ThenInclude(x => x.User)
        //                 .Include(x => x.Dose)
        //                 .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
        //                 .Where(c => c.Date.Date == CurrentPakDateTime.Date)
        //                 .Where(c => c.IsDone != true && c.IsSkip != true)
        //                 .OrderBy(x => x.Child.Id)
        //                 .ThenBy(x => x.Date)
        //                 .ToList<Schedule>();

        //         var sc =
        //             db
        //                 .Schedules
        //                 .Include(c => c.Child)
        //                 .ThenInclude(c => c.User)
        //                 .Include(c => c.Dose)
        //                 .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
        //                 .Where(c => c.Child.PreferredDayOfReminder != 0)
        //                 .Where(c => c.Date == NextDayTime.AddMinutes(-1)) //.AddDays (c.Child.PreferredDayOfReminder
        //                 .Where(c => c.IsDone != true && c.IsSkip != true)
        //                 .OrderBy(x => x.Child.Id)
        //                 .ThenBy(x => x.Date)
        //                 .ToList<Schedule>();

        //         schedules.AddRange(sc);
        //     }
        //     else if (GapDays > 0)
        //     {
        //         AddedDateTime = AddedDateTime.AddDays(1);
        //         schedules = db.Schedules
        //                 .Include(x => x.Child)
        //                 .ThenInclude(x => x.User)
        //                 .Include(x => x.Dose)
        //                 .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
        //                 .Where(c =>
        //                     c.Date.Date > CurrentPakDateTime.Date &&
        //                     c.Date.Date <= AddedDateTime.Date)
        //                 .Where(c => c.IsDone != true && c.IsSkip != true)
        //                 .OrderBy(x => x.Child.Id)
        //                 .ThenBy(x => x.Date)
        //                 .ToList<Schedule>();
        //     }
        //     else if (GapDays < 0)
        //     {
        //         schedules = db.Schedules
        //                 .Include(x => x.Child)
        //                 .ThenInclude(x => x.User)
        //                 .Include(x => x.Dose)
        //                 .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
        //                 .Where(c =>
        //                     c.Date < CurrentPakDateTime.Date &&
        //                     c.Date >= AddedDateTime)
        //                 .Where(c => c.IsDone != true && c.IsSkip != true)
        //                 .OrderBy(x => x.Child.Id)
        //                 .ThenBy(x => x.Date)
        //                 .ToList<Schedule>();
        //     }

        //     Dictionary<string, string> map = AddDoseNames(schedules);
        //     List<Schedule> listOfSchedules = new List<Schedule>();
        //     listOfSchedules = removeDuplicateRecords(schedules, map);

        //     return listOfSchedules;
        // }

        // private static Dictionary<String, String> AddDoseNames(List<Schedule> schedules)
        // {
        //     Dictionary<String, String> map = new Dictionary<string, string>();

        //     long childId = 0;
        //     foreach (Schedule s in schedules)
        //     {
        //         if (!map.ContainsKey(s.ChildId.ToString()))
        //         {
        //             map.Add(s.ChildId.ToString(), s.Dose.Name);
        //         }
        //         else
        //         {
        //             string name = map[s.ChildId.ToString()];
        //             name += ", " + s.Dose.Name;
        //             map[s.ChildId.ToString()] = name;
        //         }
        //         childId = s.ChildId;
        //     }
        //     return map;
        // }

        // private static List<Schedule> removeDuplicateRecords(List<Schedule> schedules, Dictionary<String, String> map)
        // {
        //     List<Schedule> uniqueSchedule = new List<Schedule>();

        //     Queue<Schedule> myQueue = new Queue<Schedule>();

        //     long childId = 0;
        //     foreach (Schedule s in schedules)
        //     {
        //         if (childId != s.ChildId)
        //         {
        //             string name = map[s.ChildId.ToString()];
        //             s.Dose.Name = name;
        //             uniqueSchedule.Add(s);

        //             string sms = "Reminder: Vaccination for ";
        //             sms += s.Child.Name + " is due on " + s.Date;
        //             sms += " (" + name + " )";
        //         }
        //         childId = s.ChildId;
        //     }
        //     return uniqueSchedule;
        // }

        // [HttpGet("sms-alert/{GapDays}/{OnlineClinicId}")]
        // public Response<IEnumerable<ScheduleDTO>>
        // SendSMSAlertToParent(int GapDays, int OnlineClinicId)
        // {
        //     {
        //         List<Schedule> Schedules =
        //             GetAlertData(GapDays, OnlineClinicId, _db);
        //         var dbChildren =
        //             Schedules.Select(x => x.Child).Distinct().ToList();
        //         foreach (var child in dbChildren)
        //         {
        //             if (child.Email != "")
        //             {
        //                 var dbSchedules = Schedules.Where(x => x.ChildId == child.Id).ToList();
        //                 var doseName = "";
        //                 DateTime scheduleDate = new DateTime();
        //                 foreach (var schedule in dbSchedules)
        //                 {
        //                     doseName += schedule.Dose.Name + ", ";
        //                     scheduleDate = schedule.Date;
        //                 }

        //                 UserEmail.ParentAlertEmail(doseName, scheduleDate, child);
        //             }
        //         }

        //         List<ScheduleDTO> scheduleDtos = _mapper.Map<List<ScheduleDTO>>(Schedules);
        //         return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDtos);
        //     }
        // }

        // [HttpGet("individual-sms-alert/{GapDays}/{childId}")]
        // public Response<IEnumerable<ScheduleDTO>> SendSMSAlertToOneChild(int GapDays, int childId)
        // {
        //     {
        //         IEnumerable<Schedule> Schedules = new List<Schedule>();
        //         DateTime AddedDateTime =
        //             DateTime.UtcNow.AddHours(5).AddDays(GapDays);
        //         DateTime pakistanDate = DateTime.UtcNow.AddHours(5).Date;
        //         if (GapDays == 0)
        //         {
        //             Schedules =
        //                 _db
        //                     .Schedules
        //                     .Include("Child")
        //                     .Include("Dose")
        //                     .Where(sc => sc.ChildId == childId)
        //                     .Where(sc => sc.Date == pakistanDate)
        //                     .Where(sc => sc.IsDone == false)
        //                     .OrderBy(x => x.Child.Id)
        //                     .ThenBy(y => y.Date)
        //                     .ToList<Schedule>();
        //         }
        //         if (GapDays > 0)
        //         {
        //             Schedules =
        //                 _db
        //                     .Schedules
        //                     .Include("Child")
        //                     .Include("Dose")
        //                     .Where(sc => sc.ChildId == childId)
        //                     .Where(sc => sc.IsDone == false)
        //                     .Where(sc =>
        //                         sc.Date >= pakistanDate &&
        //                         sc.Date <= AddedDateTime)
        //                     .OrderBy(x => x.Child.Id)
        //                     .ThenBy(y => y.Date)
        //                     .ToList<Schedule>();
        //         }
        //         if (GapDays < 0)
        //         {
        //             Schedules =
        //                 _db
        //                     .Schedules
        //                     .Include("Child")
        //                     .Include("Dose")
        //                     .Where(sc => sc.ChildId == childId)
        //                     .Where(sc => sc.IsDone == false)
        //                     .Where(sc =>
        //                         sc.Date <= pakistanDate &&
        //                         sc.Date >= AddedDateTime)
        //                     .OrderBy(x => x.Child.Id)
        //                     .ThenBy(y => y.Date)
        //                     .ToList<Schedule>();
        //         }

        //         var doseName = "";
        //         DateTime scheduleDate = new DateTime();
        //         var dbChild =
        //             _db
        //                 .Childs
        //                 .Include(x => x.User)
        //                 .Include(x => x.Clinic)
        //                 .Where(x => x.Id == childId)
        //                 .FirstOrDefault();
        //         var Childdoctor =
        //             _db
        //                 .Clinics
        //                 .Include(x => x.Doctor)
        //                 .Where(x => x.Id == dbChild.ClinicId)
        //                 .FirstOrDefault();
        //         var doctorUser =
        //             _db
        //                 .Doctors
        //                 .Include(x => x.User)
        //                 .Where(x => x.Id == dbChild.Clinic.DoctorId)
        //                 .FirstOrDefault();

        //         foreach (var schedule in Schedules)
        //         {
        //             doseName += schedule.Dose.Name.Trim() + ", ";
        //             scheduleDate = schedule.Date;
        //         }

        //         List<ScheduleDTO> scheduleDtos =
        //             _mapper.Map<List<ScheduleDTO>>(Schedules);
        //         return new Response<IEnumerable<ScheduleDTO>>(true,
        //             null,
        //             scheduleDtos);
        //     }
        // }

        // [HttpGet("send-msg/{GapDays}/{OnlineClinicId}")]
        // public Response<List<Messages>> SendMessages(int GapDays, long OnlineClinicId)
        // {
        //     List<Schedule> schedules = new List<Schedule>();
        //     var doctor = _db.Clinics
        //             .Where(x => x.Id == OnlineClinicId)
        //             .Include(x => x.Doctor)
        //             .First<Clinic>()
        //             .Doctor;
        //     var clinics = _db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();

        //     long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
        //     DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
        //     DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
        //     DateTime NextDayTime = (CurrentPakDateTime.AddDays(1)).Date;

        //     if (GapDays == 0)
        //     {
        //         schedules = _db.Schedules
        //                 .Include(x => x.Child)
        //                 .ThenInclude(x => x.User)
        //                 .Include(x => x.Dose)
        //                 .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
        //                 .Where(c => c.Date.Date == CurrentPakDateTime.Date)
        //                 .Where(c => c.IsDone != true && c.IsSkip != true)
        //                 .OrderBy(x => x.Child.Id)
        //                 .ThenBy(x => x.Date)
        //                 .ToList<Schedule>();

        //         var sc = _db.Schedules
        //                 .Include(c => c.Child)
        //                 .ThenInclude(c => c.User)
        //                 .Include(c => c.Dose)
        //                 .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
        //                 .Where(c => c.Child.PreferredDayOfReminder != 0)
        //                 .Where(c => c.Date == NextDayTime.AddMinutes(-1)) //.AddDays (c.Child.PreferredDayOfReminder
        //                 .Where(c => c.IsDone != true && c.IsSkip != true)
        //                 .OrderBy(x => x.Child.Id)
        //                 .ThenBy(x => x.Date)
        //                 .ToList<Schedule>();

        //         schedules.AddRange(sc);
        //     }

        //     Dictionary<String, String> map = new Dictionary<string, string>();

        //     long childId = 0;
        //     foreach (Schedule s in schedules)
        //     {
        //         if (!map.ContainsKey(s.ChildId.ToString()))
        //         {
        //             map.Add(s.ChildId.ToString(), s.Dose.Name);
        //         }
        //         else
        //         {
        //             string name = map[s.ChildId.ToString()];
        //             name += ", " + s.Dose.Name;
        //             map[s.ChildId.ToString()] = name;
        //         }
        //         childId = s.ChildId;
        //     }

        //     List<Schedule> uniqueSchedule = new List<Schedule>();
        //     Dictionary<String, String> phoneAndMsg = new Dictionary<string, string>();
        //     List<Messages> listMessages = new List<Messages>();

        //     childId = 0;
        //     foreach (Schedule s in schedules)
        //     {
        //         if (childId != s.ChildId && s.Child.IsInactive != true)
        //         {
        //             string name = map[s.ChildId.ToString()];
        //             s.Dose.Name = name;
        //             uniqueSchedule.Add(s);

        //             string sms = "Reminder: Vaccination for ";
        //             sms += s.Child.Name + " is due on " + s.Date.ToString("dd-MM-yyyy");
        //             sms += " (" + name + " )";

        //             Messages messages = new Messages();
        //             messages.SMS = sms;
        //             messages.ChildId = s.ChildId;
        //             messages.MobileNumber = s.Child.User.MobileNumber;
        //             listMessages.Add(messages);
        //         }
        //         childId = s.ChildId;
        //     }
        //     return new Response<List<Messages>>(true, null, listMessages);
        // }


    }
}