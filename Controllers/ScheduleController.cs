using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduleController : ControllerBase
    {
        private readonly Context _db;

        private readonly IWebHostEnvironment _host;

        private readonly IMapper _mapper;

        public ScheduleController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _host = host;
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<ScheduleDTO>>> GetAll()
        {
            var list = await _db.Schedules.OrderBy(x => x.Id).ToListAsync();
            List<ScheduleDTO> listDTO = _mapper.Map<List<ScheduleDTO>>(list);

            return new Response<List<ScheduleDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public Response<ScheduleDTO> GetSingle(int Id)
        {
            var dbSchedule = _db.Schedules
                .Include(x => x.Dose)
                .ThenInclude(x => x.Vaccine)
                .Include(x => x.Brand)
                .Where(c => c.Id == Id)
                .FirstOrDefault();
            ScheduleDTO scheduleDTOs = _mapper.Map<ScheduleDTO>(dbSchedule);
            long vaccineId = dbSchedule.Dose.VaccineId;
            var dbBrands = _db.Brands.Where(b => b.VaccineId == vaccineId).ToList();
            List<BrandDTO> brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
            scheduleDTOs.Brands = brandDTOs;
            return new Response<ScheduleDTO>(true, null, scheduleDTOs);
        }

        [HttpPost("add-schedule")]
        public Response<ScheduleDTO> Insert([FromBody] ScheduleDTO scheduleDTO)
        {
            Schedule scheduleDb = _mapper.Map<Schedule>(scheduleDTO);
            _db.Schedules.Add(scheduleDb);
            _db.SaveChanges();
            return new Response<ScheduleDTO>(true, null, scheduleDTO);
        }

        [HttpPut("child-schedule")]
        public Response<ScheduleDTO> Update(ScheduleDTO scheduleDTO)
        {
            if (String.IsNullOrEmpty(scheduleDTO.DiseaseYear)) { scheduleDTO.DiseaseYear = ""; }
            {
                var dbSchedule = _db.Schedules
                    .Include(x => x.Dose)
                    .ThenInclude(x => x.Vaccine)
                    .Include(x => x.Child)
                    .Where(c => c.Id == scheduleDTO.Id)
                    .FirstOrDefault();
                var dbBrandInventory = _db.BrandInventorys
                    .Where(
                        b => b.BrandId == scheduleDTO.BrandId && b.DoctorId == scheduleDTO.DoctorId
                    )
                    .FirstOrDefault();

                var dbSchedule2 = _db.Schedules
                  .Include(x => x.Dose)
                      .ThenInclude(x => x.Vaccine)
                  .Include(x => x.Child)
                      .ThenInclude(x => x.Clinic)
                  .Where(c => c.Id == scheduleDTO.Id)
                  .FirstOrDefault();
                var dbBrandInventory2 = _db.BrandInventorys
                    .Where(
                        b => b.BrandId == dbSchedule2.BrandId && b.DoctorId == dbSchedule2.Child.Clinic.DoctorId
                    )
                    .FirstOrDefault();

                if (scheduleDTO.IsDone == false)
                {
                    dbSchedule.IsDone = scheduleDTO.IsDone;
                    dbSchedule.GivenDate = null;
                    dbSchedule.BrandId = null;
                    dbSchedule.IsSkip = scheduleDTO.IsSkip;



                    ScheduleDTO newData2 = _mapper.Map<ScheduleDTO>(dbSchedule);
                    if (dbBrandInventory2 != null)
                    {
                        DateTime currentDate = DateTime.Now.Date;
                        if (currentDate == dbSchedule.Date && dbSchedule.Brand == null)
                        {
                            dbBrandInventory2.Count = dbBrandInventory2.Count + 1;

                        }
                        else
                        {
                            dbBrandInventory2.Count = dbBrandInventory2.Count;
                        }

                    }

                    _db.SaveChanges();

                    return new Response<ScheduleDTO>(true, "congratulations", newData2);
                }
                if (dbBrandInventory != null)
                //not null
                {
                    DateTime currentDate = DateTime.Now.Date;
                    if (currentDate == dbSchedule.Date && dbSchedule.Brand == null)
                    {
                        dbBrandInventory.Count = dbBrandInventory.Count - 1;

                    }
                }

                // if (scheduleDTO.GivenDate.Date == DateTime.UtcNow.AddHours(5).Date)


                // to hide next doses if disease appeared
                if (scheduleDTO.IsDisease == true)
                {
                    var nextDoses = _db.Doses
                        .Where(x => x.VaccineId == dbSchedule.Dose.VaccineId)
                        .ToList();
                    foreach (var dose in nextDoses)
                    {
                        if (dose.Id != dbSchedule.DoseId)
                        {
                            var childschedule = _db.Schedules
                                .Where(x => x.ChildId == dbSchedule.Child.Id && x.DoseId == dose.Id)
                                .FirstOrDefault();
                            if (childschedule != null)
                                childschedule.IsSkip = true;
                        }
                    }
                }

                // hpv doses skip and add
                if (dbSchedule.Dose.Name.StartsWith("HPV") && dbSchedule.Dose.DoseOrder == 1)
                {
                    var daysDifference = Convert.ToInt32(
                        (scheduleDTO.GivenDate.Date - dbSchedule.Child.DOB.Date).TotalDays
                    );

                    // Console.WriteLine (daysDifference);
                    if (daysDifference > 5475)
                    {
                        // CHANGE NEXT DOSES
                        var nextDoses = _db.Doses
                            .Where(x => x.VaccineId == dbSchedule.Dose.VaccineId)
                            .ToList();
                        foreach (var dose in nextDoses)
                        {
                            if (dose.DoseOrder == 2)
                            {
                                var childschedule = _db.Schedules
                                    .Where(
                                        x => x.ChildId == dbSchedule.Child.Id && x.DoseId == dose.Id
                                    )
                                    .FirstOrDefault();
                                childschedule.IsSkip = false;
                                childschedule.Date = calculateDate(scheduleDTO.GivenDate.Date, 30);
                            }

                            if (dose.DoseOrder == 3)
                            {
                                var childschedule = _db.Schedules
                                    .Where(
                                        x => x.ChildId == dbSchedule.Child.Id && x.DoseId == dose.Id
                                    )
                                    .FirstOrDefault();
                                childschedule.IsSkip = false;
                                childschedule.Date = calculateDate(scheduleDTO.GivenDate.Date, 180);
                            }
                        }

                        // SAVE CURRENT DOSE
                        dbSchedule.BrandId = scheduleDTO.BrandId;
                        dbSchedule.Weight = scheduleDTO.Weight;
                        dbSchedule.Height = scheduleDTO.Height;
                        dbSchedule.Circle = scheduleDTO.Circle;
                        dbSchedule.IsDone = scheduleDTO.IsDone;
                        dbSchedule.GivenDate = scheduleDTO.GivenDate;
                        dbSchedule.DiseaseYear = scheduleDTO.DiseaseYear;
                        dbSchedule.IsDisease = scheduleDTO.IsDisease;

                        ScheduleDTO newData1 = _mapper.Map<ScheduleDTO>(dbSchedule);
                        _db.SaveChanges();
                        return new Response<ScheduleDTO>(true, "congratulations", newData1);
                    }
                }

                // for MENACWY Rules on brand Selection start
                if (dbSchedule.Dose.Name.StartsWith("MenACWY") && dbSchedule.Dose.DoseOrder == 1)
                {
                    var doseBrand = _db.Brands
                        .Where(x => x.Id == scheduleDTO.BrandId)
                        .FirstOrDefault();
                    var daysDifference = Convert.ToInt32(
                        (scheduleDTO.GivenDate.Date - dbSchedule.Child.DOB.Date).TotalDays
                    );

                    if (doseBrand != null)
                        if (daysDifference > 729 && doseBrand.Name.Equals("MENACTRA"))
                        {
                            var nextDose = _db.Doses
                                .Where(
                                    x =>
                                        x.VaccineId == dbSchedule.Dose.VaccineId && x.DoseOrder == 2
                                )
                                .FirstOrDefault();
                            var nextSchedule = _db.Schedules
                                .Where(
                                    x => x.ChildId == dbSchedule.Child.Id && x.DoseId == nextDose.Id
                                )
                                .FirstOrDefault();
                            if (nextSchedule != null)
                                nextSchedule.IsSkip = true;
                        }
                        else if (daysDifference > 364 && doseBrand.Name.Equals("NIMENRIX"))
                        {
                            var nextDose = _db.Doses
                                .Where(
                                    x =>
                                        x.VaccineId == dbSchedule.Dose.VaccineId && x.DoseOrder == 2
                                )
                                .FirstOrDefault();
                            var nextSchedule = _db.Schedules
                                .Where(
                                    x => x.ChildId == dbSchedule.Child.Id && x.DoseId == nextDose.Id
                                )
                                .FirstOrDefault();
                            nextSchedule.IsSkip = true;
                        }
                }

                // for MENACWY Rules on brand Selection end

                // // for flu and typhoid
                //   if (dbSchedule.Dose.Name.StartsWith ("Flu") || dbSchedule.Dose.Name.StartsWith ("Typhoid")) {
                //      var nextDose = _db.Doses.Where (x => x.VaccineId == dbSchedule.Dose.VaccineId && x.DoseOrder == (dbSchedule.Dose.DoseOrder + 1)).ToList ();
                //     if (nextDose != null){
                //         var nextschedule = _db.Schedules.Where(x => x.ChildId == dbSchedule.Child.Id && x.DoseId == nextDose.Id).FirstOrDefault();
                //     }
                //   }
                if (dbSchedule.Dose.DoseOrder != 1 && scheduleDTO.IsSkip != true)
                {
                    var prevdose = _db.Doses
                        .Where(
                            x =>
                                x.VaccineId == dbSchedule.Dose.VaccineId
                                && x.DoseOrder == (dbSchedule.Dose.DoseOrder - 1)
                        )
                        .FirstOrDefault();
                    var previousSchedule = _db.Schedules
                        .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == prevdose.Id)
                        .FirstOrDefault();
                    if (previousSchedule != null)
                    {
                        if (previousSchedule.IsSkip != true && previousSchedule.IsDone == false)
                            return new Response<ScheduleDTO>(
                                false,
                                "previous dose is not given",
                                null
                            );
                    }
                }

                dbSchedule.BrandId = scheduleDTO.BrandId;
                dbSchedule.Weight = scheduleDTO.Weight;
                dbSchedule.Height = scheduleDTO.Height;
                dbSchedule.Circle = scheduleDTO.Circle;
                dbSchedule.IsDone = scheduleDTO.IsDone;
                dbSchedule.GivenDate = scheduleDTO.GivenDate;
                dbSchedule.DiseaseYear = scheduleDTO.DiseaseYear;
                dbSchedule.IsDisease = scheduleDTO.IsDisease;
                ChangeDueDatesOfInjectedSchedule(scheduleDTO, dbSchedule);
                ScheduleDTO newData = _mapper.Map<ScheduleDTO>(dbSchedule);
                _db.SaveChanges();
                return new Response<ScheduleDTO>(true, "congratulations", newData);
            }
        }

        private void ChangeDueDatesOfInjectedSchedule(ScheduleDTO scheduleDTO, Schedule dbSchedule)
        {
            var daysDifference = Convert.ToInt32((scheduleDTO.GivenDate.Date - dbSchedule.Date.Date).TotalDays);
            var dbDose = _db.Doses.Include(x => x.Vaccine).ToList();
            var dbVacc = _db.Vaccines.Include(x => x.Doses).ToList();
            var AllDoses = dbSchedule.Dose.Vaccine.Doses;
            AllDoses = AllDoses.Where(x => x.DoseOrder > dbSchedule.Dose.DoseOrder).OrderBy(x => x.DoseOrder).ToList();
            var previousdosedate = scheduleDTO.GivenDate.Date;
            foreach (var d in AllDoses)
            {
                var minimumGap = d.MinGap;

                var TargetSchedule = _db.Schedules
                    .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                    .FirstOrDefault();
                if (TargetSchedule != null)
                {
                    var Targetdosegap = Convert.ToInt32(
                        (TargetSchedule.Date.Date - previousdosedate).TotalDays
                    );
                    if (Targetdosegap < minimumGap)
                    {
                        // TargetSchedule.Date =
                        //     calculateDate(TargetSchedule.Date, Convert.ToInt32(d.MinGap)); //TargetSchedule.Date.AddDays(daysDifference);
                        TargetSchedule.Date = calculateDate(
                                previousdosedate,
                                Convert.ToInt32(minimumGap)
                            ); //TargetSchedule.Date.AddDays(daysDifference);
                        previousdosedate = TargetSchedule.Date.Date;
                    }
                }
            }
        }

        [HttpPost]
        public Response<IEnumerable<ScheduleDTO>> Post(IEnumerable<ScheduleDTO> dsDTOS)
        {
            foreach (var SchedueDTO in dsDTOS)
            {
                if (String.IsNullOrEmpty(SchedueDTO.DiseaseYear))
                    SchedueDTO.DiseaseYear = "";

                var dbChild = _db.Childs.Where(x => x.Id == SchedueDTO.ChildId).FirstOrDefault();
                var dbDose = _db.Doses.Where(x => x.Id == SchedueDTO.DoseId).FirstOrDefault();
                SchedueDTO.Date = calculateDate(dbChild.DOB, dbDose.MinAge);
                Schedule SchduleDB = _mapper.Map<Schedule>(SchedueDTO);

                //  SchduleDB.Date = calculateDate(dbChild.DOB , dbDose.MinAge);
                _db.Schedules.Add(SchduleDB);
                _db.SaveChanges();
                SchedueDTO.Id = SchduleDB.Id;
            }
            return new Response<IEnumerable<ScheduleDTO>>(true, null, dsDTOS);
        }
        [HttpPost("regular")]
        public IActionResult AddSchedule(long DoctorId, long ChildId)
        {
            try
            {
                var dbDoses = _db.DoctorSchedules
                    .Where(ds => ds.DoctorId == DoctorId)
                    .Select(ds => ds.Dose)
                    .ToList();

                var dbChild = _db.Childs.FirstOrDefault(x => x.Id == ChildId);

                if (dbChild == null)
                {
                    return NotFound("Child not found");
                }

                List<ScheduleDTO> schedulesDTO = new List<ScheduleDTO>();
                foreach (var dbDose in dbDoses)
                {
                    ScheduleDTO scheduleDTO = new ScheduleDTO
                    {
                        Id = 0, // Assuming Id is auto-generated by the database
                        Date = calculateDate(dbChild.DOB, dbDose.MinAge),
                        ChildId = dbChild.Id,
                        DoseId = (int)dbDose.Id,
                        // Set other properties here if needed
                    };

                    schedulesDTO.Add(scheduleDTO);
                }

                // Map ScheduleDTO to Schedule entities using AutoMapper
                List<Schedule> schedules = _mapper.Map<List<Schedule>>(schedulesDTO);

                // Add schedules to the database context
                _db.Schedules.AddRange(schedules);
                _db.SaveChanges(); // Save changes to the database

                return Ok(schedulesDTO); // Return DTOs or entities as needed
            }
            catch (Exception ex)
            {
                // Log the exception or return an error response
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpPost("bulk-brand")]
        public Response<List<ScheduleDTO>> GetVaccineBrands(ScheduleDTO scheduleDto)
        {
            {
                var dbSchedule = _db.Schedules
                    .Include(x => x.Dose)
                    .Where(
                        x =>
                            x.Date.Date == scheduleDto.Date.Date && x.ChildId == scheduleDto.ChildId
                    )
                    .ToList();
                var dbDose = _db.Doses.Include(x => x.Vaccine).ToList();
                var dbVacc = _db.Vaccines.Include(x => x.Doses).Include(x => x.Brands).ToList();

                List<ScheduleDTO> scheduleDTOs = new List<ScheduleDTO>();
                foreach (var schedule in dbSchedule)
                {
                    ScheduleDTO scheduleDTO = new ScheduleDTO();
                    var dbBrands = schedule.Dose.Vaccine.Brands.ToList();
                    List<BrandDTO> brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
                    scheduleDTO.Dose = _mapper.Map<DoseDTO>(schedule.Dose);
                    scheduleDTO.Id = schedule.Id;
                    scheduleDTO.Brands = brandDTOs;
                    scheduleDTO.BrandId = schedule.BrandId;
                    var child = _db.Childs.Where(x => x.Id == schedule.ChildId).FirstOrDefault(); //child
                    var ClinicId = child.ClinicId;
                    var clinic = _db.Clinics.Where(x => x.Id == ClinicId).FirstOrDefault(); //clinic
                    var doctorId = clinic.DoctorId;


                    var brandAmount = _db.BrandAmounts
                        .Where(x => x.BrandId == schedule.BrandId && x.DoctorId == doctorId)
                        .FirstOrDefault();
                    if (brandAmount != null && schedule.Amount == null)
                        scheduleDTO.Amount = brandAmount.Amount;
                    else
                        scheduleDTO.Amount = schedule.Amount;
                    scheduleDTO.Date = schedule.Date;
                    scheduleDTO.InvoiceDate = schedule.GivenDate;
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
                var dbSchedules = _db.Schedules
                    .Where(
                        x =>
                            x.Child.ClinicId == clinic.Id
                            && x.Date.Date >= obj.FromDate.Date
                            && x.Date.Date <= obj.ToDate.Date
                    )
                    .ToList();

                foreach (Schedule schedule in dbSchedules)
                {
                    schedule.Date = obj.ToDate.AddDays(1);
                    _db.SaveChanges();
                }
            }

            return new Response<ScheduleDTO>(
                true,
                "Vacations are considered and appointments are moved to "
                    + obj.ToDate.AddDays(1).ToString("dd-MM-yyy")
                    + " date.",
                null
            );
        }

        [HttpPut("BulkReschedule")]
        public Response<ScheduleDTO> BulkReschedule(
            ScheduleDTO scheduleDTO,
            [FromQuery] bool ignoreMaxAgeRule = false,
            [FromQuery] bool ignoreMinAgeFromDOB = false,
            [FromQuery] bool ignoreMinGapFromPreviousDose = false
        )
        {
            var dbSchedule = _db.Schedules
                .Include(x => x.Dose)
                .Include(x => x.Child)
                .Where(x => x.Id == scheduleDTO.Id)
                .FirstOrDefault();

            var dbSchedules = _db.Schedules
                .Include(x => x.Dose)
                .Include(x => x.Child)
                .Where(
                    x =>
                        x.Date == dbSchedule.Date
                        && x.ChildId == dbSchedule.ChildId
                        && x.IsDone == false
                )
                .ToList();
            var dbDose = _db.Doses.Include(x => x.Vaccine).ToList();
            var dbVacc = _db.Vaccines.Include(x => x.Doses).ToList();
            string message;

            foreach (var schedule in dbSchedules)
            {
                message = ChangeDueDatesOfSchedule(
                    scheduleDTO,
                    _db,
                    schedule,
                    "bulk",
                    ignoreMaxAgeRule,
                    ignoreMinAgeFromDOB,
                    ignoreMinGapFromPreviousDose
                );
                if (message != "ok")
                    return new Response<ScheduleDTO>(false, message, null);
            }

            return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
        }

        [HttpPut("update-bulk-injection")]
        public Response<ScheduleDTO> UpdateBulkInjection(ScheduleDTO scheduleDTO)
        {
            {
                var dbSchedule = _db.Schedules
                    .Where(x => x.Id == scheduleDTO.Id)
                    .Include(x => x.Child)
                    .ThenInclude(x => x.Schedules)
                    .FirstOrDefault();

                var dbChildSchedules = dbSchedule.Child.Schedules
                    .Where(x => x.Date == dbSchedule.Date && x.IsSkip != true)
                    .ToList();

                foreach (var schedule in dbChildSchedules)
                {
                    schedule.Weight =
                        (scheduleDTO.Weight > 0) ? scheduleDTO.Weight : schedule.Weight;
                    schedule.Height =
                        (scheduleDTO.Height > 0) ? scheduleDTO.Height : schedule.Height;
                    schedule.Circle =
                        (scheduleDTO.Circle > 0) ? scheduleDTO.Circle : schedule.Circle;
                    schedule.IsDone = scheduleDTO.IsDone;
                    schedule.GivenDate = scheduleDTO.GivenDate.Date;

                    if (scheduleDTO.ScheduleBrands.Count > 0)
                    {
                        var scheduleBrand = scheduleDTO.ScheduleBrands.Find(
                            x => x.ScheduleId == schedule.Id
                        );
                        if (scheduleBrand != null)
                        {
                            schedule.BrandId = scheduleBrand.BrandId;
                            // if (scheduleDTO.GivenDate.Date == DateTime.UtcNow.AddHours(5).Date)
                            // {
                            var brandInventory = _db.BrandInventorys
                                .Where(
                                    b =>
                                        b.BrandId == scheduleBrand.BrandId
                                        && b.DoctorId == scheduleDTO.DoctorId
                                )
                                .FirstOrDefault();
                            if (brandInventory != null)
                                brandInventory.Count--;
                            // }
                        }
                    }
                    ChangeDueDatesOfInjectedSchedule(scheduleDTO, schedule);
                }
                _db.SaveChanges();
                return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
            }
        }

        [HttpPut("update-bulk-invoice")]
        public Response<IEnumerable<ScheduleDTO>> updateInvoice(IEnumerable<ScheduleDTO> dsDTOS)
        {
            foreach (var schedule in dsDTOS)
            {
                var schedulec = _db.Schedules.Where(x => x.Id == schedule.Id).FirstOrDefault();
                schedulec.Amount = schedule.Amount;
            }
            _db.SaveChanges();
            return new Response<IEnumerable<ScheduleDTO>>(
                true,
                "Invoice updated successfully.",
                null
            );
        }

        //date Function
        public static DateTime calculateDate(DateTime date, int GapInDays)
        {
            if (GapInDays == 30 || GapInDays == 31)
                return date.AddMonths(1);
            else if (GapInDays == 150)
                return date.AddMonths(5); // For 3 months
            else if (GapInDays == 84)
                return date.AddMonths(3); // For 9 Year 1 month
            else if (GapInDays == 3315)
                return date.AddYears(9).AddMonths(1); // For 10 Year 6 month
            else if (GapInDays == 3833)
                return date.AddYears(10).AddMonths(6); // For 1 to 15 years
            else if (
                GapInDays == 365
                || GapInDays == 730
                || GapInDays == 1095
                || GapInDays == 1460
                || GapInDays == 1825
                || GapInDays == 2190
                || GapInDays == 2555
                || GapInDays == 2920
                || GapInDays == 3285
                || GapInDays == 3650
                || GapInDays == 4015
                || GapInDays == 4380
                || GapInDays == 4745
                || GapInDays == 5110
                || GapInDays == 5475
            )
                return date.AddYears((int)(GapInDays / 365)); // From 6 months to 11 months
            else if (GapInDays >= 168 && GapInDays <= 334)
                return date.AddMonths((int)(GapInDays / 28)); // From 13 months to 20 months
            else if (GapInDays >= 395 && GapInDays <= 608)
                return date.AddMonths((int)(GapInDays / 29)); // From 21 months to 11 months
            else if (GapInDays >= 639 && GapInDays <= 1795)
                return date.AddMonths((int)(GapInDays / 30));
            else
                return date.AddDays(GapInDays);
        }

        //Reschedule Function
        private string ChangeDueDatesOfSchedule(
            ScheduleDTO scheduleDTO,
            Context db,
            Schedule dbSchedule,
            string mode,
            bool ignoreMaxAgeRule,
            bool ignoreMinAgeFromDOB,
            bool ignoreMinGapFromPreviousDose
        )
        {
            var daysDifference = Convert.ToInt32(
                (scheduleDTO.Date.Date - dbSchedule.Date.Date).TotalDays
            );
            var AllDoses = dbSchedule.Dose.Vaccine.Doses;
            string message;

            // FOR BCG Only or those vaccines who have only 1 dose
            if (AllDoses.Count == 1)
            {
                // for flu and typhoid
                if (dbSchedule.Dose.Vaccine.isInfinite)
                {
                    var TargetSchedule1 = db.Schedules
                        .Where(x => x.Id == dbSchedule.Id)
                        .FirstOrDefault();
                    TargetSchedule1.Date = TargetSchedule1.Date.AddDays(daysDifference);

                    _db.SaveChangesAsync();
                    message = "ok";
                    return message;
                }
                else
                {
                    // check for reschedule backward from DateOfBirth
                    // if (scheduleDTO.Date < dbSchedule.Child.DOB)
                    //     throw new Exception("Cannot reschedule to your selected date: " +
                    //                 Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy") + " because it is less than date of birth of child.");
                    if (scheduleDTO.Date < dbSchedule.Child.DOB)
                    {
                        message =
                            "Cannot reschedule to your selected date: "
                            + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                            + " because it is less than date of birth of child.";
                        return message;
                    }
                    Dose d = AllDoses.ElementAt<Dose>(0);
                    var TargetSchedule = db.Schedules
                        .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                        .FirstOrDefault();
                    if (daysDifference > d.MaxAge && !ignoreMaxAgeRule)
                        if (mode.Equals("bulk"))
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because it is greater than the Max Age of dose. ";

                            //    +
                            //    "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>");
                            return message;
                        }
                        else
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because it is greater than the Max Age of dose. ";

                            //     +
                            //    "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>");
                            return message;
                        }

                    TargetSchedule.Date = TargetSchedule.Date.AddDays(daysDifference);
                    //  calculateDate(TargetSchedule.Date, daysDifference); //
                }
            }
            else
            {
                // forward rescheduling
                if (daysDifference > 0)
                {
                    AllDoses = AllDoses
                        .Where(x => x.DoseOrder >= dbSchedule.Dose.DoseOrder)
                        .ToList();
                    DateTime previousDate = DateTime.UtcNow.AddHours(5);

                    //foreach (var d in AllDoses)
                    for (int i = 0; i < AllDoses.Count; i++)
                    {
                        var d = AllDoses.ElementAt(i);
                        int? MinGap = d.MinGap;
                        var TargetSchedule = db.Schedules
                            .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                            .FirstOrDefault();

                        // if MinGap is this dose < MinAge of Previouse Dose; then dont reschedule
                        // stop updating date of a dose if minimum gap is valid
                        if (TargetSchedule != null)
                        {
                            if (i != 0)
                            {
                                var doseDaysDifference = Convert.ToInt32(
                                    (TargetSchedule.Date.Date - previousDate.Date).TotalDays
                                );
                                if (doseDaysDifference <= MinGap)
                                    TargetSchedule.Date = TargetSchedule.Date.AddDays(
                                        daysDifference
                                    );
                                // calculateDate(TargetSchedule.Date,
                                // daysDifference); //
                            }
                            else
                            {
                                // check for MaxAge of any Dose
                                if (daysDifference > d.MaxAge && !ignoreMaxAgeRule)
                                    if (mode.Equals("bulk"))
                                    {
                                        message =
                                            "Cannot reschedule to your selected date: "
                                            + Convert
                                                .ToDateTime(scheduleDTO.Date.Date)
                                                .ToString("dd-MM-yyyy")
                                            + " because it is greater than the Max Age of dose.";

                                        //    +
                                        //    "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>";
                                        return message;
                                    }
                                    else
                                    {
                                        message =
                                            "Cannot reschedule to your selected date: "
                                            + Convert
                                                .ToDateTime(scheduleDTO.Date.Date)
                                                .ToString("dd-MM-yyyy")
                                            + " because it is greater than the Max Age of dose. ";

                                        //     +
                                        //    "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},true,false,false)'> Ignore Rule</ion-button>";
                                        return message;
                                    }
                                TargetSchedule.Date = TargetSchedule.Date.AddDays(daysDifference);
                                //calculateDate(TargetSchedule.Date,
                                // daysDifference); //
                            }
                            previousDate = TargetSchedule.Date;
                        }
                    }
                }
                else
                // backward rescheduling
                {
                    // find that dose and its previous dose
                    AllDoses = AllDoses
                        .Where(x => x.DoseOrder <= dbSchedule.Dose.DoseOrder)
                        .OrderBy(x => x.DoseOrder)
                        .ToList();

                    // if we rescdule the first dose of any vaccine
                    if (AllDoses.Count == 1)
                    {
                        Dose d = AllDoses.ElementAt<Dose>(0);
                        var FirstDoseSchedule = db.Schedules
                            .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                            .FirstOrDefault();

                        int diff = Convert.ToInt32(
                            (scheduleDTO.Date.Date - FirstDoseSchedule.Child.DOB).TotalDays
                        );
                        if (diff < 0)
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because it is less than date of birth of child.";
                            return message;
                        }
                        else if (diff < d.MinAge && !ignoreMinAgeFromDOB)
                            if (mode.Equals("bulk"))
                            {
                                message =
                                    "Cannot reschedule to your selected date: "
                                    + Convert
                                        .ToDateTime(scheduleDTO.Date.Date)
                                        .ToString("dd-MM-yyyy")
                                    + " because Minimum Age of this vaccine from date of birth should be "
                                    + d.MinAge
                                    + " days.";

                                //  +
                                // "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,true,false)'> Ignore Rule</ion-button>";
                                return message;
                            }
                            else
                            {
                                message =
                                    "Cannot reschedule to your selected date: "
                                    + Convert
                                        .ToDateTime(scheduleDTO.Date.Date)
                                        .ToString("dd-MM-yyyy")
                                    + " because Minimum Age of this vaccine from date of birth should be "
                                    + d.MinAge
                                    + " days.";
                                //     +
                                //    "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,true,false)'> Ignore Rule</ion-button>";
                            }
                        else
                            FirstDoseSchedule.Date = FirstDoseSchedule.Date.AddDays(daysDifference);
                        // calculateDate(FirstDoseSchedule.Date,
                        // daysDifference);
                    }
                    else
                    // if we rescdule other than first dose of any vaccine
                    {
                        var lastDose = AllDoses.Last<Dose>();
                        var secondLastDose = AllDoses.ElementAt(AllDoses.Count - 2);

                        var TargetSchedule = db.Schedules
                            .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == lastDose.Id)
                            .FirstOrDefault();
                        var TargetSchedulePrevious = db.Schedules
                            .Where(
                                x =>
                                    x.ChildId == dbSchedule.ChildId && x.DoseId == secondLastDose.Id
                            )
                            .FirstOrDefault();

                        long doseDaysDifference = 0;
                        if (TargetSchedulePrevious != null)
                        {
                            if (
                                TargetSchedulePrevious.IsDone
                                && TargetSchedulePrevious.GivenDate.HasValue
                            )
                                doseDaysDifference = Convert.ToInt32(
                                    (
                                        scheduleDTO.Date.Date
                                        - TargetSchedulePrevious.GivenDate.Value
                                    ).TotalDays
                                );
                            else
                                doseDaysDifference = Convert.ToInt32(
                                    (scheduleDTO.Date.Date - TargetSchedulePrevious.Date).TotalDays
                                );
                        }

                        if (doseDaysDifference < lastDose.MinGap && !ignoreMinGapFromPreviousDose)
                            if (mode.Equals("bulk"))
                            {
                                message =
                                    "Cannot reschedule to your selected date: "
                                    + Convert
                                        .ToDateTime(scheduleDTO.Date.Date)
                                        .ToString("dd-MM-yyyy")
                                    + " because Minimum Gap from previous dose of this vaccine should be "
                                    + lastDose.MinGap
                                    + " days.";

                                //  +
                                // "<ion-button (click)='BulkReschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,false,true);'> Ignore Rule</a>";
                                return message;
                            }
                            else
                            {
                                message =
                                    "Cannot reschedule to your selected date: "
                                    + Convert
                                        .ToDateTime(scheduleDTO.Date.Date)
                                        .ToString("dd-MM-yyyy")
                                    + " because Minimum Gap from previous dose of this vaccine should be "
                                    + lastDose.MinGap
                                    + " days.";

                                //  +
                                // "<ion-button (click)='Reschedule({Id:" + scheduleDTO.Id + ",Date:'" + scheduleDTO.Date.ToString("dd-MM-yyyy") + "'},false,false,true);'> Ignore Rule</ion-button>";
                                return message;
                            }
                        if (TargetSchedule != null)
                            TargetSchedule.Date = TargetSchedule.Date.AddDays(daysDifference);
                        // calculateDate(TargetSchedule.Date,
                        // daysDifference);
                    }
                }
            }
            db.SaveChanges();
            return "ok";
        }

        [HttpPut("Reschedule")]
        public Response<ScheduleDTO> Reschedule(
            ScheduleDTO scheduleDTO,
            [FromQuery] bool ignoreMaxAgeRule = false,
            [FromQuery] bool ignoreMinAgeFromDOB = false,
            [FromQuery] bool ignoreMinGapFromPreviousDose = false
        )
        {
            {
                var dbSchedule = _db.Schedules
                    .Include(x => x.Dose)
                    .Include(x => x.Child)
                    .Where(x => x.Id == scheduleDTO.Id)
                    .FirstOrDefault();
                var dbDose = _db.Doses.Include(X => X.Vaccine).ToList();
                var dbVacc = _db.Vaccines.Include(x => x.Doses).ToList();
                var message = ChangeDueDatesOfSchedule(
                    scheduleDTO,
                    _db,
                    dbSchedule,
                    "single",
                    ignoreMaxAgeRule,
                    ignoreMinAgeFromDOB,
                    ignoreMinGapFromPreviousDose
                );
                if (message == "ok")
                    return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
                else
                    return new Response<ScheduleDTO>(false, message, null);
            }
        }

        [HttpDelete("{ChildId}/{DoseId}/{Date}")]
        public async Task<Response<List<Schedule>>> Delete(long ChildId, long DoseId, String date)
        {
            DateTime dateOfInjection = DateTime.ParseExact(date, "dd-MM-yyyy", null);

            var objList = await _db.Schedules
                .Where(x => x.ChildId == ChildId)
                .Where(x => x.DoseId == DoseId)
                .Where(x => x.Date > dateOfInjection)
                .ToListAsync();

            List<Schedule> listDTO = _mapper.Map<List<Schedule>>(objList);

            if (listDTO == null)
            {
                return new Response<List<Schedule>>(false, "Error: failed to delete ", listDTO);
            }

            foreach (Schedule obj in listDTO)
            {
                _db.Schedules.Remove(obj);
            }
            await _db.SaveChangesAsync();

            return new Response<List<Schedule>>(true, null, listDTO);
        }

        [HttpGet("alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ScheduleDTO>> GetAlert(int GapDays, long OnlineClinicId)
        {
            {
                List<Schedule> schedules = GetAlertData(GapDays, OnlineClinicId, _db);
                IEnumerable<ScheduleDTO> scheduleDTO = _mapper.Map<IEnumerable<ScheduleDTO>>(
                    schedules
                );
                return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDTO);
            }
        }

        private static List<Schedule> GetAlertData(int GapDays, long OnlineClinicId, Context db)
        {
            List<Schedule> schedules = new List<Schedule>();
            var doctor = db.Clinics
                .Where(x => x.Id == OnlineClinicId)
                .Include(x => x.Doctor)
                .First<Clinic>()
                .Doctor;
            var clinics = db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();
            long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
            DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
            DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
            DateTime NextDayTime = CurrentPakDateTime.AddDays(1).Date;

            if (GapDays == 0)
            {
                schedules = db.Schedules
                    .Include(x => x.Child)
                    .ThenInclude(x => x.User)
                    .Include(x => x.Dose)
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date.Date == CurrentPakDateTime.Date)
                    .Where(c => c.IsDone != true && c.IsSkip != true)
                    .OrderBy(x => x.Child.Id)
                    .ThenBy(x => x.Date)
                    .ToList<Schedule>();
            }
            else if (GapDays > 0)
            {
                AddedDateTime = AddedDateTime.AddDays(1);
                schedules = db.Schedules
                    .Include(x => x.Child)
                    .ThenInclude(x => x.User)
                    .Include(x => x.Dose)
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date.Date > CurrentPakDateTime.Date && c.Date.Date <= AddedDateTime.Date)
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
                    .Where(c => c.Date < CurrentPakDateTime.Date && c.Date >= AddedDateTime)
                    .Where(c => c.IsDone != true && c.IsSkip != true)
                    .OrderBy(x => x.Child.Id)
                    .ThenBy(x => x.Date)
                    .ToList<Schedule>();
            }

            Dictionary<string, string> map = AddDoseNames(schedules);
            List<Schedule> listOfSchedules = new List<Schedule>();
            listOfSchedules = removeDuplicateRecords(schedules, map);

            return listOfSchedules;
        }


        ///////////////
        [HttpGet("alert2/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ChildDTO>> GetAlert2(int GapDays, long OnlineClinicId)
        {
            List<Schedule> schedules = GetAlertData2(GapDays, OnlineClinicId, _db);


            IEnumerable<ChildDTO> childInfoDTOs = schedules.Select(s => new ChildDTO
            {
                Id = s.Child.Id,
                Name = s.Child.Name,
                Email = s.Child.Email,
                ClinicId = s.Child.ClinicId,


            });


            foreach (var child in childInfoDTOs)
            {
                if (child.Email == "")
                {
                    continue;
                }
                else
                {

                    var ChildId = child.Id;
                    var ClinicId = child.ClinicId;

                    var doctor = _db.Clinics
                        .Where(x => x.Id == ClinicId)
                        .Include(x => x.Doctor)
                        .FirstOrDefault()
                        ?.Doctor;

                    var clinics = _db.Clinics.Where(x => x.Id == ClinicId).FirstOrDefault();

                    var dbSchedules = _db.Schedules.Where(x => x.ChildId == ChildId).ToList();

                    var specificDate = DateTime.Today;

                    var specificSchedule = dbSchedules
                        .FirstOrDefault(schedule => schedule.Date == specificDate);

                    string body;
                    if (specificSchedule != null)
                    {
                        var doseId = specificSchedule.DoseId;
                        body = $"Reminder: Vaccination!! Doctor: {doctor.FirstName}, Clinic: {clinics.Name}, Phone: {clinics.PhoneNumber}, Child: {child.Name}, Dose ID: {doseId}";
                    }
                    else
                    {
                        body = "No schedule found for the specified date.";
                    }

                    // Use the 'body' variable as needed (e.g., send an email)

                    try
                    {
                        UserEmail.SendEmail2(child.Email, body);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error sending email: " + ex.Message);


                    }

                }

            }

            return new Response<IEnumerable<ChildDTO>>(true, null, childInfoDTOs);
        }


        private static List<Schedule> GetAlertData2(int GapDays, long OnlineClinicId, Context db)
        {
            var doctor = db.Clinics
                .Where(x => x.Id == OnlineClinicId)
                .Include(x => x.Doctor)
                .First()
                .Doctor;

            var clinics = db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();
            long[] ClinicIDs = clinics.Select(x => x.Id).ToArray();

            DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
            DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
            DateTime NextDayTime = CurrentPakDateTime.AddDays(1).Date;


            List<Schedule> schedules = new List<Schedule>();

            if (GapDays == 0)
            {
                schedules = db.Schedules
                    .Include(x => x.Child)
                    .ThenInclude(x => x.User)
                    .Include(x => x.Dose)
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date.Date == CurrentPakDateTime.Date)
                    .Where(c => c.IsDone != true && c.IsSkip != true)
                    .OrderBy(x => x.Child.Id)
                    .ThenBy(x => x.Date)
                    .ToList();
            }

            Dictionary<string, string> map = AddDoseNames(schedules);
            List<Schedule> listOfSchedules = removeDuplicateRecords(schedules, map);

            return listOfSchedules;
        }


        private static Dictionary<String, String> AddDoseNames(List<Schedule> schedules)
        {
            Dictionary<String, String> map = new Dictionary<string, string>();

            long childId = 0;
            foreach (Schedule s in schedules)
            {
                if (!map.ContainsKey(s.ChildId.ToString()))
                {
                    map.Add(s.ChildId.ToString(), s.Dose.Name);
                }
                else
                {
                    string name = map[s.ChildId.ToString()];
                    name += ", " + s.Dose.Name;
                    map[s.ChildId.ToString()] = name;
                }
                childId = s.ChildId;
            }
            return map;
        }

        private static List<Schedule> removeDuplicateRecords(
            List<Schedule> schedules,
            Dictionary<String, String> map
        )
        {
            List<Schedule> uniqueSchedule = new List<Schedule>();

            // Dictionary<String, String> phoneAndMsg = new Dictionary<string, string>();
            Queue<Schedule> myQueue = new Queue<Schedule>();

            long childId = 0;
            foreach (Schedule s in schedules)
            {
                if (childId != s.ChildId)
                {
                    // Console.WriteLine();
                    // Console.WriteLine(s.Child.Id);
                    // Console.WriteLine(s.Child.Name);
                    // Console.WriteLine(s.Dose.Name);
                    string name = map[s.ChildId.ToString()];
                    s.Dose.Name = name;
                    uniqueSchedule.Add(s);

                    string sms = "Reminder: Vaccination for ";
                    sms += s.Child.Name + " is due on " + s.Date;
                    sms += " (" + name + " )";
                    // phoneAndMsg.Add(s.Child.User.MobileNumber.ToString(), sms);

                    // Console.WriteLine(s.Child.Name);
                    // Console.WriteLine(name);
                }
                childId = s.ChildId;
            }
            return uniqueSchedule;
        }

        [HttpGet("sms-alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ScheduleDTO>> SendSMSAlertToParent(
            int GapDays,
            int OnlineClinicId
        )
        {
            {
                List<Schedule> Schedules = GetAlertData(GapDays, OnlineClinicId, _db);
                var dbChildren = Schedules.Select(x => x.Child).Distinct().ToList();
                foreach (var child in dbChildren)
                {
                    if (child.Email != "")
                    {
                        var dbSchedules = Schedules.Where(x => x.ChildId == child.Id).ToList();
                        var doseName = "";
                        DateTime scheduleDate = new DateTime();
                        foreach (var schedule in dbSchedules)
                        {
                            doseName += schedule.Dose.Name + ", ";
                            scheduleDate = schedule.Date;
                        }

                        UserEmail.ParentAlertEmail(doseName, scheduleDate, child);
                    }
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
                    Schedules = _db.Schedules
                        .Include("Child")
                        .Include("Dose")
                        .Where(sc => sc.ChildId == childId)
                        .Where(sc => sc.Date == pakistanDate)
                        .Where(sc => sc.IsDone == false)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(y => y.Date)
                        .ToList<Schedule>();
                }
                if (GapDays > 0)
                {
                    Schedules = _db.Schedules
                        .Include("Child")
                        .Include("Dose")
                        .Where(sc => sc.ChildId == childId)
                        .Where(sc => sc.IsDone == false)
                        .Where(sc => sc.Date >= pakistanDate && sc.Date <= AddedDateTime)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(y => y.Date)
                        .ToList<Schedule>();
                }
                if (GapDays < 0)
                {
                    Schedules = _db.Schedules
                        .Include("Child")
                        .Include("Dose")
                        .Where(sc => sc.ChildId == childId)
                        .Where(sc => sc.IsDone == false)
                        .Where(sc => sc.Date <= pakistanDate && sc.Date >= AddedDateTime)
                        .OrderBy(x => x.Child.Id)
                        .ThenBy(y => y.Date)
                        .ToList<Schedule>();
                }

                var doseName = "";
                DateTime scheduleDate = new DateTime();
                var dbChild = _db.Childs
                    .Include(x => x.User)
                    .Include(x => x.Clinic)
                    .Where(x => x.Id == childId)
                    .FirstOrDefault();
                var Childdoctor = _db.Clinics
                    .Include(x => x.Doctor)
                    .Where(x => x.Id == dbChild.ClinicId)
                    .FirstOrDefault();
                var doctorUser = _db.Doctors
                    .Include(x => x.User)
                    .Where(x => x.Id == dbChild.Clinic.DoctorId)
                    .FirstOrDefault();

                foreach (var schedule in Schedules)
                {
                    doseName += schedule.Dose.Name.Trim() + ", ";
                    scheduleDate = schedule.Date;
                }

                List<ScheduleDTO> scheduleDtos = _mapper.Map<List<ScheduleDTO>>(Schedules);
                return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDtos);
            }
        }

        [HttpGet("send-msg/{GapDays}/{OnlineClinicId}")]
        public Response<List<Messages>> SendMessages(int GapDays, long OnlineClinicId)
        {
            List<Schedule> schedules = new List<Schedule>();
            var doctor = _db.Clinics
                .Where(x => x.Id == OnlineClinicId)
                .Include(x => x.Doctor)
                .First<Clinic>()
                .Doctor;
            var clinics = _db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();

            long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
            DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
            DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);
            DateTime NextDayTime = (CurrentPakDateTime.AddDays(1)).Date;

            if (GapDays == 0)
            {
                schedules = _db.Schedules
                    .Include(x => x.Child)
                    .ThenInclude(x => x.User)
                    .Include(x => x.Dose)
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.Date.Date == CurrentPakDateTime.Date)
                    .Where(c => c.IsDone != true && c.IsSkip != true)
                    .OrderBy(x => x.Child.Id)
                    .ThenBy(x => x.Date)
                    .ToList<Schedule>();

                var sc = _db.Schedules
                    .Include(c => c.Child)
                    .ThenInclude(c => c.User)
                    .Include(c => c.Dose)
                    .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                    .Where(c => c.IsDone != true && c.IsSkip != true)
                    .OrderBy(x => x.Child.Id)
                    .ThenBy(x => x.Date)
                    .ToList<Schedule>();

                schedules.AddRange(sc);
            }

            Dictionary<String, String> map = new Dictionary<string, string>();

            long childId = 0;
            foreach (Schedule s in schedules)
            {
                if (!map.ContainsKey(s.ChildId.ToString()))
                {
                    map.Add(s.ChildId.ToString(), s.Dose.Name);
                }
                else
                {
                    string name = map[s.ChildId.ToString()];
                    name += ", " + s.Dose.Name;
                    map[s.ChildId.ToString()] = name;
                }
                childId = s.ChildId;
            }

            List<Schedule> uniqueSchedule = new List<Schedule>();
            Dictionary<String, String> phoneAndMsg = new Dictionary<string, string>();
            List<Messages> listMessages = new List<Messages>();

            childId = 0;
            foreach (Schedule s in schedules)
            {
                if (childId != s.ChildId && s.Child.IsInactive != true)
                {
                    string name = map[s.ChildId.ToString()];
                    s.Dose.Name = name;
                    uniqueSchedule.Add(s);

                    string sms = "Reminder: Vaccination for ";
                    sms += s.Child.Name + " is due on " + s.Date.ToString("dd-MM-yyyy");
                    sms += " (" + name + " )";

                    Messages messages = new Messages();
                    messages.SMS = sms;
                    messages.ChildId = s.ChildId;
                    messages.MobileNumber = s.Child.User.MobileNumber;
                    listMessages.Add(messages);
                }
                childId = s.ChildId;
            }
            return new Response<List<Messages>>(true, null, listMessages);
        }

        [HttpPatch("{id}")]
        public async Task<ActionResult<IEnumerable<long>>> GetChildIdsWithSchedulesFromClinic(long id, [FromQuery] string fromDate, [FromQuery] string toDate)
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);

                // Query the database to find the children IDs associated with the clinic ID
                var childIds = await _db.Childs
                                        .Where(c => c.ClinicId == id)
                                        .Select(c => c.Id)
                                        .ToListAsync();

                if (childIds == null || !childIds.Any())
                {
                    return NotFound("No children found for the provided clinic ID");
                }

                List<long> childIdsWithSchedules = new List<long>();

                // Loop through each child ID
                foreach (var childId in childIds)
                {
                    var schedules = await _db.Schedules
                                            .Where(c => c.ChildId == childId && c.IsDone == false && c.Date >= parsedFromDate && c.Date <= parsedToDate)
                                            .ToListAsync();
                    if (schedules.Any())
                    {
                        var daysToAdd = parsedToDate.AddDays(1);
                        foreach (var schedule in schedules)
                        {
                            schedule.Date = daysToAdd;
                        }
                        await _db.SaveChangesAsync();
                    }

                    // For now, let's just add the child ID to the list
                    childIdsWithSchedules.Add(childId);
                }

                return Ok(childIdsWithSchedules);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while retrieving child IDs: {ex.Message}");
            }
        }


    }

}
