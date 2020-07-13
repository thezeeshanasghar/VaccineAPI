using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
using Microsoft.AspNetCore.Hosting;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduleController : ControllerBase
    {
        private readonly Context _db;
        private readonly IHostingEnvironment _host;
        private readonly IMapper _mapper;

        public ScheduleController(Context context, IMapper mapper, IHostingEnvironment host)
        {
            _host = host;
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
       
         public async Task<Response<List<ScheduleDTO>>> GetAll()
        {
            var list = await _db.Schedules.OrderBy(x=>x.Id).ToListAsync();
            List<ScheduleDTO> listDTO = _mapper.Map<List<ScheduleDTO>>(list);
           
            return new Response<List<ScheduleDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
       public Response<ScheduleDTO> GetSingle(int Id)
            {
                
                    var dbSchedule = _db.Schedules.Include(x=>x.Dose).ThenInclude(x=>x.Vaccine).Include(x=>x.Brand).Where(c => c.Id == Id).FirstOrDefault();
                    ScheduleDTO scheduleDTOs = _mapper.Map<ScheduleDTO>(dbSchedule);
                    long vaccineId = dbSchedule.Dose.VaccineId;
                    var dbBrands = _db.Brands.Where(b => b.VaccineId == vaccineId).ToList();
                    List<BrandDTO> brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
                    scheduleDTOs.Brands = brandDTOs;
                    return new Response<ScheduleDTO>(true, null, scheduleDTOs);
            
            }



         [HttpGet("alert/{GapDays}/{OnlineClinicId}")]

        public Response<IEnumerable<ScheduleDTO>> GetAlert(int GapDays, long OnlineClinicId)
        {
            
                {
                    List<Schedule> schedules = GetAlertData(GapDays, OnlineClinicId, _db);
                    IEnumerable<ScheduleDTO> scheduleDTO = _mapper.Map<IEnumerable<ScheduleDTO>>(schedules);
                    return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDTO);
                }
            
        }
        private static List<Schedule> GetAlertData(int GapDays, long OnlineClinicId, Context db)
        {
            List<Schedule> schedules = new List<Schedule>();
            var doctor = db.Clinics.Where(x => x.Id == OnlineClinicId).Include(x=>x.Doctor).First<Clinic>().Doctor;
            var clinics = db.Clinics.Where(x=>x.DoctorId == doctor.Id).ToList();
           // long[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<long>();
           long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
            DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
            DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
            if (GapDays == 0)
            {
                schedules = db.Schedules.Include("Child")
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date == CurrentPakDateTime.Date)
                    .Where(c => c.IsDone == false)
                    .OrderBy(x => x.Child.Id).ThenBy(x => x.Date).ToList<Schedule>();
                      foreach(var sch in schedules)
                     {
                       var childss = db.Childs.Include("User").Where(x=> x.Id == sch.Child.Id).FirstOrDefault();
                    }
                var sc = db.Schedules.Include("Child")
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Child.PreferredDayOfReminder != 0)
                    .Where(c => c.Date == CurrentPakDateTime.Date.AddDays(c.Child.PreferredDayOfReminder))
                   // .Where(c => c.Date == AddedDateTime.AddDays(CurrentPakDateTime.Date, c.Child.PreferredDayOfReminder))
                    .Where(c => c.IsDone == false)
                    .OrderBy(x => x.Child.Id).ThenBy(x => x.Date).ToList<Schedule>();
                schedules.AddRange(sc);
            }
            else if (GapDays > 0)
            {
                AddedDateTime = AddedDateTime.AddDays(1);
                schedules = db.Schedules.Include("Child")
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date > CurrentPakDateTime.Date && c.Date <= AddedDateTime)
                    .Where(c => c.IsDone == false)
                    .OrderBy(x => x.Child.Id).ThenBy(x => x.Date)
                    .ToList<Schedule>();
                     foreach(var sch in schedules)
                     {
                       var childss = db.Childs.Include("User").Where(x=> x.Id == sch.Child.Id).FirstOrDefault();
                    }
                
            }
            else if (GapDays < 0)
            {
               // schedules = db.Schedules.Include("Child")
               schedules = db.Schedules.Include(x=>x.Child)
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date < CurrentPakDateTime.Date && c.Date >= AddedDateTime)
                    .Where(c => c.IsDone == false)
                    .OrderBy(x => x.Child.Id).ThenBy(x => x.Date)
                    .ToList<Schedule>();
                    // schchild = schedules.Child;
                     foreach(var sch in schedules)
                     {
                       var childss = db.Childs.Include("User").Where(x=> x.Id == sch.Child.Id).FirstOrDefault();
                      // sch.Child.User.MobileNumber = childss.User.MobileNumber;
                    }
                    
            }
            schedules = removeDuplicateRecords(schedules);
            return schedules;
        }

         private static List<Schedule> removeDuplicateRecords(List<Schedule> schedules)
        {
            List<Schedule> uniqueSchedule = new List<Schedule>();
            long childId = 0;
            foreach(Schedule s in schedules)
            {
                if(childId != s.ChildId)
                    uniqueSchedule.Add(s); 
                childId = s.ChildId;
            }
            return uniqueSchedule;
        }

          [HttpGet("sms-alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ScheduleDTO>> SendSMSAlertToParent(int GapDays, int OnlineClinicId)
        {    
                {
                    List<Schedule> Schedules = GetAlertData(GapDays, OnlineClinicId, _db);
                    var dbChildren = Schedules.Select(x => x.Child).Distinct().ToList();
                    foreach (var child in dbChildren)
                    {
                        var dbSchedules = Schedules.Where(x => x.ChildId == child.Id).ToList();
                        var doseName = "";
                        DateTime scheduleDate = new DateTime();
                        foreach (var schedule in dbSchedules)
                        {
                            doseName += schedule.Dose.Name + ", ";
                            scheduleDate = schedule.Date;
                        }
                         UserSMS u = new UserSMS(_db);
                         u.ParentSMSAlert(doseName ,scheduleDate, child );
                      //  UserSMS.ParentSMSAlert(doseName, scheduleDate, child);
                    }

                    List<ScheduleDTO> scheduleDtos = _mapper.Map<List<ScheduleDTO>>(Schedules);
                    return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDtos);
                }
        }


        [HttpGet("individual-sms-alert/{GapDays}/{childId}")]
        public Response<IEnumerable<ScheduleDTO>> SendSMSAlertToOneChild(int GapDays, int childId)
        {
            
                {
                    IEnumerable<Schedule> Schedules = new List<Schedule>();
                    DateTime AddedDateTime = DateTime.UtcNow.AddHours(5).AddDays(GapDays);
                    DateTime pakistanDate = DateTime.UtcNow.AddHours(5).Date;
                    if (GapDays == 0)
                    {
                        Schedules = _db.Schedules.Include("Child").Include("Dose")
                            .Where(sc => sc.ChildId == childId)
                            .Where(sc => sc.Date == pakistanDate)
                            .Where(sc => sc.IsDone == false)
                            .OrderBy(x => x.Child.Id).ThenBy(y => y.Date).ToList<Schedule>();
                    }
                    if (GapDays > 0)
                    {
                        Schedules = _db.Schedules.Include("Child").Include("Dose")
                            .Where(sc => sc.ChildId == childId)
                            .Where(sc => sc.IsDone == false)
                            .Where(sc => sc.Date >= pakistanDate && sc.Date <= AddedDateTime)
                            .OrderBy(x => x.Child.Id).ThenBy(y => y.Date).ToList<Schedule>();
                    }
                    if (GapDays < 0)
                    {
                        Schedules = _db.Schedules.Include("Child").Include("Dose")
                           .Where(sc => sc.ChildId == childId)
                           .Where(sc => sc.IsDone == false)
                           .Where(sc => sc.Date <= pakistanDate && sc.Date >= AddedDateTime)
                           .OrderBy(x => x.Child.Id).ThenBy(y => y.Date).ToList<Schedule>();
                    }

                    var doseName = "";
                    DateTime scheduleDate = new DateTime();
                    var dbChild = _db.Childs.Include(x=>x.User).Include(x=>x.Clinic).Where(x => x.Id == childId).FirstOrDefault();
                    var Childdoctor = _db.Clinics.Include(x=>x.Doctor).Where(x=>x.Id ==dbChild.ClinicId).FirstOrDefault();
                    var doctorUser = _db.Doctors.Include(x=>x.User).Where(x=>x.Id == dbChild.Clinic.DoctorId).FirstOrDefault();
                   
                    foreach (var schedule in Schedules)
                    {
                        doseName += schedule.Dose.Name.Trim() + ", ";
                        scheduleDate = schedule.Date;
                    }
                   //  UserSMS u = new UserSMS(_db);
                   //  u.ParentSMSAlert(doseName, scheduleDate, dbChild);

                    List<ScheduleDTO> scheduleDtos = _mapper.Map<List<ScheduleDTO>>(Schedules);
                    return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDtos);
                }

        }
        [HttpPut("child-schedule")]
        public Response<ScheduleDTO> Update(ScheduleDTO scheduleDTO)
        {
                {
                    var dbSchedule = _db.Schedules.Include(x=>x.Dose).Include(x=>x.Child).Where(c => c.Id == scheduleDTO.Id).FirstOrDefault();
                    var dbBrandInventory = _db.BrandInventorys.Where(b => b.BrandId == scheduleDTO.BrandId
                                            && b.DoctorId == scheduleDTO.DoctorId).FirstOrDefault();
                    if (scheduleDTO.IsDone == false)
                    {
                    dbSchedule.IsDone = scheduleDTO.IsDone;
                    dbSchedule.GivenDate = null;
                    dbSchedule.IsSkip =scheduleDTO.IsSkip ;
                    
                     _db.SaveChanges();
                    return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
                    }
                    if (dbBrandInventory != null && dbBrandInventory.Count > 0)
                        if (scheduleDTO.GivenDate.Date == DateTime.UtcNow.AddHours(5).Date)
                            dbBrandInventory.Count--;
                    dbSchedule.BrandId = scheduleDTO.BrandId;
                    dbSchedule.Weight = scheduleDTO.Weight;
                    dbSchedule.Height = scheduleDTO.Height;
                    dbSchedule.Circle = scheduleDTO.Circle;
                    dbSchedule.IsDone = scheduleDTO.IsDone;
                    dbSchedule.GivenDate = scheduleDTO.GivenDate;
                    dbSchedule.DiseaseYear =scheduleDTO.DiseaseYear;
                    dbSchedule.IsDisease =scheduleDTO.IsDisease;
                    ChangeDueDatesOfInjectedSchedule(scheduleDTO , dbSchedule);

                    _db.SaveChanges();
                    return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
                }
            }
        //    private void ChangeDueDatesOfInjectedSchedule(ScheduleDTO scheduleDTO, Context entities, Schedule dbSchedule)
        // {
        //     var daysDifference = Convert.ToInt32((scheduleDTO.GivenDate.Date - dbSchedule.Date.Date).TotalDays);

        //     var dbDose = _db.Doses.Include(x=>x.Vaccine).ToList();
        //     var dbVacc = _db.Vaccines.Include(x=>x.Doses).ToList();
        //     var AllDoses = dbSchedule.Dose.Vaccine.Doses;
        //     AllDoses = AllDoses.Where(x => x.DoseOrder > dbSchedule.Dose.DoseOrder).ToList();
        //     foreach (var d in AllDoses)
        //     {
        //         var TargetSchedule = entities.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id).FirstOrDefault();
        //         TargetSchedule.Date = calculateDate(TargetSchedule.Date, daysDifference); //TargetSchedule.Date.AddDays(daysDifference);
        //     }

        // }

         private void ChangeDueDatesOfInjectedSchedule(ScheduleDTO scheduleDTO,  Schedule dbSchedule)
        {
            var daysDifference = Convert.ToInt32((scheduleDTO.GivenDate.Date - dbSchedule.Date.Date).TotalDays);

            var dbDose = _db.Doses.Include(x=>x.Vaccine).ToList();
            var dbVacc = _db.Vaccines.Include(x=>x.Doses).ToList();
            var AllDoses = dbSchedule.Dose.Vaccine.Doses;
            AllDoses = AllDoses.Where(x => x.DoseOrder > dbSchedule.Dose.DoseOrder).ToList();
            foreach (var d in AllDoses)
            {
                var TargetSchedule = _db.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id).FirstOrDefault();
                TargetSchedule.Date = calculateDate(TargetSchedule.Date, daysDifference); //TargetSchedule.Date.AddDays(daysDifference);
            }

        }


          [HttpPost]
        public Response<IEnumerable<ScheduleDTO>> Post(IEnumerable<ScheduleDTO> dsDTOS)
        {
              
              
                
                    foreach (var SchedueDTO in dsDTOS)
                    {
                        var dbChild =  _db.Childs.Where(x=>x.Id == SchedueDTO.ChildId).FirstOrDefault();
                        var dbDose =  _db.Doses.Where(x=>x.Id == SchedueDTO.DoseId).FirstOrDefault();
                        SchedueDTO.Date = calculateDate(dbChild.DOB , dbDose.MinAge);
                        Schedule SchduleDB = _mapper.Map<Schedule>(SchedueDTO);
                      //  SchduleDB.Date = calculateDate(dbChild.DOB , dbDose.MinAge);
                        _db.Schedules.Add(SchduleDB);
                        _db.SaveChanges();
                        SchedueDTO.Id = SchduleDB.Id;
                    }
                    return new Response<IEnumerable<ScheduleDTO>>(true, null, dsDTOS);
                
            }

         [HttpPost("bulk-brand")]
        public Response<List<ScheduleDTO>> GetVaccineBrands(ScheduleDTO scheduleDto)
        {
           
                {
                    var dbSchedule = _db.Schedules.Include(x=>x.Dose).Where(x => x.Date == scheduleDto.Date && x.ChildId == scheduleDto.ChildId).ToList();
                    var dbDose = _db.Doses.Include(x=>x.Vaccine).ToList();
                    var dbVacc = _db.Vaccines.Include(x=>x.Doses).Include(x=>x.Brands).ToList();

                    List<ScheduleDTO> scheduleDTOs = new List<ScheduleDTO>();
                    foreach (var schedule in dbSchedule)
                    {
                        ScheduleDTO scheduleDTO = new ScheduleDTO();
                        var dbBrands = schedule.Dose.Vaccine.Brands.ToList();
                        List<BrandDTO> brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
                        scheduleDTO.Dose = _mapper.Map<DoseDTO>(schedule.Dose);
                        scheduleDTO.Id = schedule.Id;
                        scheduleDTO.Brands = brandDTOs;
                        scheduleDTO.Date = schedule.Date;
                        scheduleDTO.IsDone = schedule.IsDone;
                        scheduleDTOs.Add(scheduleDTO);
                    }

                    return new Response<List<ScheduleDTO>>(true, null, scheduleDTOs);
                }
            }

         [HttpPost("add-vacation")]
        public Response<ScheduleDTO> AddVacations(ScheduleDTO obj)
        {
                
                    foreach (var clinic in obj.Clinics)
                    {
                        var dbSchedules = _db.Schedules.Where(x => x.Child.ClinicId == clinic.Id
                        && x.Date >= obj.FromDate && x.Date <= obj.ToDate).ToList();

                        foreach (Schedule schedule in dbSchedules)
                        {
                            schedule.Date = obj.ToDate.AddDays(1);
                            _db.SaveChanges();
                        }
                    }

                    return new Response<ScheduleDTO>(true, "Vacations are considered and appointments are moved to " +
                        obj.ToDate.AddDays(1).ToString("dd-MM-yyy") + " date.", null);

                
            }


     
        
         [HttpPut("BulkReschedule")]
        public Response<ScheduleDTO> BulkReschedule(ScheduleDTO scheduleDTO, [FromQuery] bool ignoreMaxAgeRule = false, [FromQuery]bool ignoreMinAgeFromDOB = false,  [FromQuery]bool ignoreMinGapFromPreviousDose = false)
        {
            
                
                    var dbSchedule = _db.Schedules.Include(x=>x.Dose).Include(x=>x.Child).Where(x => x.Id == scheduleDTO.Id).FirstOrDefault();

                    var dbSchedules = _db.Schedules.Include(x=>x.Dose).Include(x=>x.Child).Where(x => x.Date == dbSchedule.Date 
                                                                && x.ChildId == dbSchedule.ChildId
                                                                && x.IsDone==false
                                                                ).ToList();
                     var dbDose = _db.Doses.Include(x=>x.Vaccine).ToList();
                     var dbVacc = _db.Vaccines.Include(x=>x.Doses).ToList();
                     string message;

                    foreach (var schedule in dbSchedules)
                    {
                        message = ChangeDueDatesOfSchedule(scheduleDTO, _db, schedule, "bulk", ignoreMaxAgeRule, ignoreMinAgeFromDOB, ignoreMinGapFromPreviousDose);
                        if (message != "ok")
                        return new Response<ScheduleDTO>(false, message , null);
                    
                    }

                    return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
                
            }

        [HttpPut("update-bulk-injection")]
        public Response<ScheduleDTO> UpdateBulkInjection(ScheduleDTO scheduleDTO)
        {
           
                {
                    var dbSchedule = _db.Schedules.Where(x => x.Id == scheduleDTO.Id).Include(x=>x.Child).ThenInclude(x=>x.Schedules).FirstOrDefault();
                    var dbChildSchedules = dbSchedule.Child.Schedules.Where(x => x.Date == dbSchedule.Date).ToList();

                    foreach (var schedule in dbChildSchedules)
                    {
                        //if (!schedule.IsDone)
                        //{
                        schedule.Weight = (scheduleDTO.Weight > 0) ? scheduleDTO.Weight : schedule.Weight;
                        schedule.Height = (scheduleDTO.Height > 0) ? scheduleDTO.Height : schedule.Height;
                        schedule.Circle = (scheduleDTO.Circle > 0) ? scheduleDTO.Circle : schedule.Circle;
                        schedule.IsDone = scheduleDTO.IsDone;
                        schedule.GivenDate = scheduleDTO.GivenDate.Date;

                        if (scheduleDTO.ScheduleBrands.Count > 0)
                        {
                            var scheduleBrand = scheduleDTO.ScheduleBrands.Find(x => x.ScheduleId == schedule.Id);
                            if (scheduleBrand != null && scheduleBrand.BrandId != null )
                            {
                                schedule.BrandId = scheduleBrand.BrandId;
                                if (scheduleDTO.GivenDate.Date == DateTime.UtcNow.AddHours(5).Date)
                                {
                                    var brandInventory = _db.BrandInventorys.Where(b => b.BrandId == scheduleBrand.BrandId && b.DoctorId == scheduleDTO.DoctorId).FirstOrDefault();
                                    brandInventory.Count--;
                                }
                            }
                        }
                        //ChangeDueDatesOfInjectedSchedule(scheduleDTO, schedule);
                    }
                    _db.SaveChanges();
                    return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
                }
            }

         //date Function
          public static DateTime calculateDate(DateTime date, int GapInDays)
        {
            // For 3 months
            if (GapInDays == 84)
                return date.AddMonths(3);
            // For 9 Year 1 month
            else if (GapInDays == 3315)
                return date.AddYears(9).AddMonths(1);
            // For 10 Year 6 month
            else if (GapInDays == 3833)
                return date.AddYears(10).AddMonths(6);
            // For 1 to 15 years
            else if (GapInDays == 365 || GapInDays == 730 || GapInDays == 1095 ||
                GapInDays == 1460 || GapInDays == 1825 || GapInDays == 2190 || GapInDays == 2555 ||
                GapInDays == 2920 || GapInDays == 3285 || GapInDays == 3650 || GapInDays == 4015 ||
                GapInDays == 4380 || GapInDays == 4745 || GapInDays == 5110 || GapInDays == 5475)
                return date.AddYears((int)(GapInDays / 365));
            // From 6 months to 11 months
            else if (GapInDays >= 168 && GapInDays <= 334)
                return date.AddMonths((int)(GapInDays / 28));
            // From 13 months to 20 months
            else if (GapInDays >= 395 && GapInDays <= 608)
                return date.AddMonths((int)(GapInDays / 29));
            // From 21 months to 11 months
            else if (GapInDays >= 639 && GapInDays <= 1795)
                return date.AddMonths((int)(GapInDays / 30));
            else
                return date.AddDays(GapInDays);
        }
         //Reschedule Function
           private string ChangeDueDatesOfSchedule(ScheduleDTO scheduleDTO, Context db, Schedule dbSchedule, string mode, bool ignoreMaxAgeRule, bool ignoreMinAgeFromDOB, bool ignoreMinGapFromPreviousDose)
        {
            var daysDifference = Convert.ToInt32((scheduleDTO.Date.Date - dbSchedule.Date.Date).TotalDays);
            var AllDoses = dbSchedule.Dose.Vaccine.Doses;
            string message;
            // FOR BCG Only or those vaccines who have only 1 dose 
            if (AllDoses.Count == 1)
            {
                // check for reschedule backward from DateOfBirth
                // if (scheduleDTO.Date < dbSchedule.Child.DOB)
                //     throw new Exception("Cannot reschedule to your selected date: " +
                //                 Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is less than date of birth of child.");

                 if (scheduleDTO.Date < dbSchedule.Child.DOB)
                 {
                 message = "Cannot reschedule to your selected date: " +
                                Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is less than date of birth of child.";
                  return message;
                 }
                Dose d = AllDoses.ElementAt<Dose>(0);
                var TargetSchedule = db.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id).FirstOrDefault();
                if (daysDifference > d.MaxAge && !ignoreMaxAgeRule)
                    if (mode.Equals("bulk"))
                    {
                        message = "Cannot reschedule to your selected date: " +
                           Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is greater than the Max Age of dose. " ;
                        //    +
                        //    "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>");
                           return message;
                    }
                    else
                    {
                        message = "Cannot reschedule to your selected date: " +
                       Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is greater than the Max Age of dose. ";
                    //     +
                    //    "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>");
                       return message;
                    }

                TargetSchedule.Date = calculateDate(TargetSchedule.Date, daysDifference);// TargetSchedule.Date.AddDays(daysDifference);
            }
            else
            {
                // forward rescheduling
                if (daysDifference > 0)
                {
                    AllDoses = AllDoses.Where(x => x.DoseOrder >= dbSchedule.Dose.DoseOrder).ToList();
                    DateTime previousDate = DateTime.UtcNow.AddHours(5);
                    //foreach (var d in AllDoses)
                    for (int i = 0; i < AllDoses.Count; i++)
                    {
                        var d = AllDoses.ElementAt(i);
                        int? MinGap = d.MinGap;
                        var TargetSchedule = db.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id).FirstOrDefault();
                        // if MinGap is this dose < MinAge of Previouse Dose; then dont reschedule
                        // stop updating date of a dose if minimum gap is valid
                        if (i != 0)
                        {
                            var doseDaysDifference = Convert.ToInt32((TargetSchedule.Date.Date - previousDate.Date).TotalDays);
                            if (doseDaysDifference <= MinGap)
                                TargetSchedule.Date = calculateDate(TargetSchedule.Date, daysDifference); // TargetSchedule.Date.AddDays(daysDifference);
                        }
                        else
                        {
                            // check for MaxAge of any Dose
                            if (daysDifference > d.MaxAge && !ignoreMaxAgeRule)
                                if (mode.Equals("bulk"))
                                {
                                    message = "Cannot reschedule to your selected date: " +
                                   Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is greater than the Max Age of dose."; 
                                //    +
                                //    "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>";
                                   return message;
                                }
                                else
                                {
                                    message = "Cannot reschedule to your selected date: " +
                                   Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is greater than the Max Age of dose. ";
                                //     +
                                //    "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>";
                                   return message;
                                }
                            TargetSchedule.Date = calculateDate(TargetSchedule.Date, daysDifference); //TargetSchedule.Date.AddDays(daysDifference);
                        }
                        previousDate = TargetSchedule.Date;
                    }
                }
                // backward rescheduling
                else
                {
                    // find that dose and its previous dose
                    AllDoses = AllDoses.Where(x => x.DoseOrder <= dbSchedule.Dose.DoseOrder).OrderBy(x => x.DoseOrder).ToList();
                    // if we rescdule the first dose of any vaccine
                    if (AllDoses.Count == 1)
                    {
                        Dose d = AllDoses.ElementAt<Dose>(0);
                        var FirstDoseSchedule = db.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id).FirstOrDefault();

                        int diff = Convert.ToInt32((scheduleDTO.Date.Date - FirstDoseSchedule.Child.DOB).TotalDays);
                        if (diff < 0)
                             {
                            message = "Cannot reschedule to your selected date: " +
                                Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is less than date of birth of child.";
                                return message;
                             }
                        else if (diff < d.MinAge && !ignoreMinAgeFromDOB)
                            if (mode.Equals("bulk"))
                               {
                                message = "Cannot reschedule to your selected date: " +
                                Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because Minimum Age of this vaccine from date of birth should be " + d.MinAge + " days.";
                                //  +
                                // "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,true,false)'> Ignore Rule</ion-button>";
                                return message ;
                               }
                            else
                              {
                                message = "Cannot reschedule to your selected date: " +
                               Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because Minimum Age of this vaccine from date of birth should be " + d.MinAge + " days.";
                            //     +
                            //    "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,true,false)'> Ignore Rule</ion-button>";
                              }
                        else
                            FirstDoseSchedule.Date = calculateDate(FirstDoseSchedule.Date, daysDifference);
                    }
                    // if we rescdule other than first dose of any vaccine
                    else
                    {
                        var lastDose = AllDoses.Last<Dose>();
                        var secondLastDose = AllDoses.ElementAt(AllDoses.Count - 2);

                        var TargetSchedule = db.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == lastDose.Id).FirstOrDefault();
                        var TargetSchedulePrevious = db.Schedules.Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == secondLastDose.Id).FirstOrDefault();

                        long doseDaysDifference = 0;

                        if (TargetSchedulePrevious.IsDone && TargetSchedulePrevious.GivenDate.HasValue)
                            doseDaysDifference = Convert.ToInt32((scheduleDTO.Date.Date - TargetSchedulePrevious.GivenDate.Value).TotalDays);
                        else
                            doseDaysDifference = Convert.ToInt32((scheduleDTO.Date.Date - TargetSchedulePrevious.Date).TotalDays);

                        if (doseDaysDifference < lastDose.MinGap && !ignoreMinGapFromPreviousDose)
                            if (mode.Equals("bulk"))
                             {
                                message = "Cannot reschedule to your selected date: " +
                                Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because Minimum Gap from previous dose of this vaccine should be " + lastDose.MinGap + " days.";
                                //  +
                                // "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,false,true);'> Ignore Rule</a>";
                                return message;
                             }
                            else
                            {
                                message = "Cannot reschedule to your selected date: " +
                                Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because Minimum Gap from previous dose of this vaccine should be " + lastDose.MinGap + " days.";
                                //  +
                                // "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,false,true);'> Ignore Rule</ion-button>";
                                return message;
                            }
                        TargetSchedule.Date = calculateDate(TargetSchedule.Date, daysDifference);
                    }
                }
            }
            db.SaveChanges();
            return "ok";
        }

         [HttpPut("Reschedule")]
        public Response<ScheduleDTO> Reschedule(ScheduleDTO scheduleDTO , [FromQuery]bool ignoreMaxAgeRule = false, [FromQuery]bool ignoreMinAgeFromDOB = false, [FromQuery]bool ignoreMinGapFromPreviousDose = false)
        {
                {
                    var dbSchedule = _db.Schedules.Include(x=>x.Dose).Include(x=>x.Child).Where(x => x.Id == scheduleDTO.Id).FirstOrDefault();
                    var dbDose = _db.Doses.Include(X=>X.Vaccine).ToList();
                    var dbVacc = _db.Vaccines.Include(x=>x.Doses).ToList();
                   var message = ChangeDueDatesOfSchedule(scheduleDTO, _db, dbSchedule, "single", ignoreMaxAgeRule, ignoreMinAgeFromDOB, ignoreMinGapFromPreviousDose);
                   if (message == "ok" ) 
                    return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
                   else 
                    return new Response<ScheduleDTO>(false, message, null);
                }
            }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Schedules.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Schedules.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
