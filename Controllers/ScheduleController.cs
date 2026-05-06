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
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;

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
                .Include(x => x.Child)
                .Where(c => c.Id == Id)
                .FirstOrDefault();
            ScheduleDTO scheduleDTOs = _mapper.Map<ScheduleDTO>(dbSchedule);
            var vaccineId = dbSchedule?.Dose?.VaccineId;
            var dbBrands = _db.VaccineBrands
                .Where(vb => vb.VaccineId == vaccineId)
                .Join(_db.Brands, vb => vb.BrandId, b => b.Id, (vb, b) => b)
                .Distinct()
                .OrderBy(x => x.Name)
                .ToList();
            List<BrandDTO> brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
            scheduleDTOs.Brands = brandDTOs;

            if (dbSchedule != null)
            {
                scheduleDTOs.Manufacturer = dbSchedule.Brand?.Manufacturer ?? "";
                var childClinicId = dbSchedule.Child != null ? dbSchedule.Child.ClinicId : 0;
                var doctorId = _db.Clinics
                    .Where(c => c.Id == childClinicId)
                    .Select(c => c.DoctorId)
                    .FirstOrDefault();
                var clinicId = ResolveClinicIdForStock(doctorId, childClinicId);
                var stock = IsInventoryEnabledForClinic(clinicId)
                    ? GetLatestStockByBrandAndClinic(dbSchedule.BrandId, clinicId)
                    : null;
                scheduleDTOs.Lot = stock?.BatchLot ?? "";
                scheduleDTOs.Expiry = stock?.Expiry;
            }

            return new Response<ScheduleDTO>(true, null, scheduleDTOs);
        }

        [HttpPost("add-schedule")]
        public Response<ScheduleDTO> Insert([FromBody] ScheduleDTO scheduleDTO)
        {
            // Check if DoseId is 131 and child is 5 years or older
            if (scheduleDTO.DoseId == 131)
            {
                var child = _db.Childs.FirstOrDefault(x => x.Id == scheduleDTO.ChildId);
                var dose = _db.Doses
                    .Include(x => x.Vaccine)
                    .FirstOrDefault(x => x.Id == scheduleDTO.DoseId);
                var doseName = dose?.Name
                    ?? dose?.Vaccine?.Name
                    ?? $"Dose {scheduleDTO.DoseId}";

                if (child != null)
                {
                    var childAgeInDays = (DateTime.UtcNow.AddHours(5).Date - child.DOB.Date).TotalDays;
                    var childAgeInYears = childAgeInDays / 365.25;
                    
                    if (childAgeInYears >= 5)
                    {
                        return new Response<ScheduleDTO>(
                            false,
                            $"Cannot add {doseName} for children 5 years or older.",
                            null
                        );
                    }
                }
            }
            
            Schedule scheduleDb = _mapper.Map<Schedule>(scheduleDTO);
            scheduleDb.BrandId = null;
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

                if (dbSchedule == null)
                {
                    return new Response<ScheduleDTO>(false, "Schedule not found", null);
                }

                var previousBrandId = dbSchedule.BrandId;
                var onlineClinicId = ResolveClinicIdForStock(
                    scheduleDTO.DoctorId,
                    dbSchedule.Child?.ClinicId ?? 0
                );

                if (onlineClinicId <= 0)
                {
                    return new Response<ScheduleDTO>(
                        false,
                        "Unable to resolve online clinic for inventory consumption.",
                        null
                    );
                }

                var inventoryEnabled = IsInventoryEnabledForActor(scheduleDTO.DoctorId, onlineClinicId);
                var inventoryDoctorId = 0L;
                BrandAmount dbBrandInventory = null;

                var dbSchedule2 = _db.Schedules
                  .Include(x => x.Dose)
                      .ThenInclude(x => x.Vaccine)
                  .Include(x => x.Child)
                      .ThenInclude(x => x.Clinic)
                  .Where(c => c.Id == scheduleDTO.Id)
                  .FirstOrDefault();
                if (dbSchedule2 == null)
                {
                    return new Response<ScheduleDTO>(false, "Schedule not found", null);
                }
                BrandAmount dbBrandInventory2 = null;
                var rollbackClinicId = onlineClinicId;

                if (inventoryEnabled)
                {
                    inventoryDoctorId = _db.Clinics
                        .Where(c => c.Id == onlineClinicId)
                        .Select(c => c.DoctorId)
                        .FirstOrDefault();

                    if (inventoryDoctorId <= 0)
                    {
                        return new Response<ScheduleDTO>(
                            false,
                            $"Unable to resolve inventory owner doctor for clinic {onlineClinicId}.",
                            null
                        );
                    }

                    dbBrandInventory = _db.BrandAmounts
                        .Where(
                            b =>
                                b.BrandId == scheduleDTO.BrandId
                                && b.DoctorId == inventoryDoctorId
                                && b.ClinicId == onlineClinicId
                        )
                        .FirstOrDefault();

                }

                if (scheduleDTO.IsDone == false)
                {
                    if (inventoryEnabled && previousBrandId.HasValue)
                    {
                        rollbackClinicId = ResolveClinicIdForUngive(dbSchedule, scheduleDTO.DoctorId, onlineClinicId);
                        var rollbackDoctorId = _db.Clinics
                            .Where(c => c.Id == rollbackClinicId)
                            .Select(c => c.DoctorId)
                            .FirstOrDefault();

                        if (rollbackDoctorId <= 0)
                        {
                            return new Response<ScheduleDTO>(
                                false,
                                $"Unable to resolve inventory owner doctor for rollback clinic {rollbackClinicId}.",
                                null
                            );
                        }

                        dbBrandInventory2 = _db.BrandAmounts
                            .Where(
                                b =>
                                    b.BrandId == previousBrandId
                                    && b.DoctorId == rollbackDoctorId
                                    && b.ClinicId == rollbackClinicId
                            )
                            .FirstOrDefault();
                    }

                    if (inventoryEnabled && previousBrandId.HasValue && dbBrandInventory2 == null)
                    {
                        return new Response<ScheduleDTO>(
                            false,
                            BuildInventoryContextMessage(
                                "Inventory row not found for previous brand",
                                previousBrandId,
                                rollbackClinicId
                            ),
                            null
                        );
                    }

                    dbSchedule.IsDone = scheduleDTO.IsDone;
                    dbSchedule.GivenDate = null;
                    dbSchedule.DoneAt = null;
                    dbSchedule.BrandId = null;
                    dbSchedule.IsSkip = scheduleDTO.IsSkip;

                    ScheduleDTO newData2 = _mapper.Map<ScheduleDTO>(dbSchedule);
                    if (inventoryEnabled && dbBrandInventory2 != null)
                    {
                        if (previousBrandId.HasValue)
                        {
                            dbBrandInventory2.Count += 1;
                        }
                    }
                    _db.SaveChanges();
                    return new Response<ScheduleDTO>(true, "Congratulations", newData2);
                }

                if (!previousBrandId.HasValue)
                {
                    // Null brand means OHF/external source; do not consume inventory.
                    if (inventoryEnabled && scheduleDTO.BrandId.HasValue && scheduleDTO.BrandId.Value > 0)
                    {
                        if (dbBrandInventory == null)
                        {
                            return new Response<ScheduleDTO>(
                                false,
                                BuildInventoryContextMessage(
                                    "Inventory row not found for brand",
                                    scheduleDTO.BrandId,
                                    onlineClinicId
                                ),
                                null
                            );
                        }

                        if (dbBrandInventory.Count <= 0)
                        {
                            return new Response<ScheduleDTO>(
                                false,
                                BuildInventoryContextMessage(
                                    "Insufficient inventory for brand",
                                    scheduleDTO.BrandId,
                                    onlineClinicId
                                ),
                                null
                            );
                        }

                        dbBrandInventory.Count -= 1;
                    }
                }

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
                        dbSchedule.DoneAt = scheduleDTO.IsDone ? DateTime.UtcNow : (DateTime?)null;
                        dbSchedule.DiseaseYear = scheduleDTO.DiseaseYear;
                        dbSchedule.IsDisease = scheduleDTO.IsDisease;
                        var stockClinicId = ResolveClinicIdForStock(scheduleDTO.DoctorId, dbSchedule.Child?.ClinicId ?? 0);
                        ApplyStockSourceFields(dbSchedule, scheduleDTO, stockClinicId);
                        dbSchedule.Validity = scheduleDTO.Validity;

                        ScheduleDTO newData1 = _mapper.Map<ScheduleDTO>(dbSchedule);
                        _db.SaveChanges();
                        return new Response<ScheduleDTO>(true, "congratulations", newData1);
                    }
                }

                // for MENACWY Rules on brand Selection start
                if (dbSchedule.Dose.Name.StartsWith("MenACWY") && dbSchedule.Dose.DoseOrder == 1)
                {
                    var doseBrand = _db.Brands.FirstOrDefault(x => x.Id == scheduleDTO.BrandId);
                    var daysDifference = Convert.ToInt32(
                        (scheduleDTO.GivenDate.Date - dbSchedule.Child.DOB.Date).TotalDays
                    );

                    if (doseBrand != null)
                    {
                        if (daysDifference > 729 && doseBrand.Name.Equals("MENACTRA"))
                        {
                            var nextDose = _db.Doses.FirstOrDefault(x =>
                                x.VaccineId == dbSchedule.Dose.VaccineId && x.DoseOrder == 2
                            );

                            if (nextDose != null)
                            {
                                var nextSchedule = _db.Schedules.FirstOrDefault(x =>
                                    x.ChildId == dbSchedule.Child.Id && x.DoseId == nextDose.Id
                                );

                                if (nextSchedule != null)
                                {
                                    nextSchedule.IsSkip = true;
                                }
                            }
                        }
                        else if (daysDifference > 364 && doseBrand.Name.Equals("NIMENRIX"))
                        {
                            var nextDose = _db.Doses.FirstOrDefault(x =>
                                x.VaccineId == dbSchedule.Dose.VaccineId && x.DoseOrder == 2
                            );

                            if (nextDose != null)
                            {
                                var nextSchedule = _db.Schedules.FirstOrDefault(x =>
                                    x.ChildId == dbSchedule.Child.Id && x.DoseId == nextDose.Id
                                );

                                if (nextSchedule != null)
                                {
                                    nextSchedule.IsSkip = true;
                                }
                            }
                        }
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
                    if (prevdose == null)
                    {
                        return new Response<ScheduleDTO>(false, "previous dose not found", null);
                    }
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
                dbSchedule.DoneAt = scheduleDTO.IsDone ? DateTime.UtcNow : (DateTime?)null;
                dbSchedule.DiseaseYear = scheduleDTO.DiseaseYear;
                dbSchedule.IsDisease = scheduleDTO.IsDisease;
                var onlineStockClinicId = ResolveClinicIdForStock(scheduleDTO.DoctorId, dbSchedule.Child?.ClinicId ?? 0);
                ApplyStockSourceFields(dbSchedule, scheduleDTO, onlineStockClinicId);
                dbSchedule.Validity = scheduleDTO.Validity;
                dbSchedule.IsPAApprove = scheduleDTO.IsPAApprove;
                ChangeDueDatesOfInjectedSchedule(scheduleDTO, dbSchedule);
                ScheduleDTO newData = _mapper.Map<ScheduleDTO>(dbSchedule);
                _db.SaveChanges();
                return new Response<ScheduleDTO>(true, "congratulations", newData);
            }
        }

        [HttpPatch("after-injection")]
        public Response<ScheduleDTO> AfterInjection(ScheduleDTO scheduleDTO)
        {
            var dbSchedule = _db.Schedules
                .Include(x => x.Dose)
                .Include(x => x.Child)
                .Where(x => x.Id == scheduleDTO.Id)
                .FirstOrDefault();

            if (dbSchedule == null)
            {
                return new Response<ScheduleDTO>(false, "Schedule not found", null);
            }

            dbSchedule.Weight = scheduleDTO.Weight;
            dbSchedule.Height = scheduleDTO.Height;
            dbSchedule.Circle = scheduleDTO.Circle;

            _db.SaveChanges();

            return new Response<ScheduleDTO>(true, "Schedule updated successfully", _mapper.Map<ScheduleDTO>(dbSchedule));
        }

        private Stock? GetLatestStockByBrandAndClinic(long? brandId, long clinicId)
        {
            if (!brandId.HasValue || brandId.Value <= 0 || clinicId <= 0)
            {
                return null;
            }

            if (!IsInventoryEnabledForClinic(clinicId))
            {
                return null;
            }

            var stock = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId.Value && s.Bill.ClinicId == clinicId);

            var today = DateTime.UtcNow.Date;

            var stockSelection = stock
                .Where(s => s.Expiry.HasValue && s.Expiry.Value.Date >= today)
                .OrderBy(s => s.Expiry)
                .ThenBy(s => s.Bill.BillDate)
                .ThenBy(s => s.Id)
                .FirstOrDefault();

            stockSelection ??= stock
                .Where(s => s.Expiry.HasValue)
                .OrderBy(s => s.Expiry)
                .ThenBy(s => s.Bill.BillDate)
                .ThenBy(s => s.Id)
                .FirstOrDefault();

            stockSelection ??= stock
                .OrderByDescending(s => s.Bill.BillDate)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();

            return stockSelection;
        }

        private void ApplyStockSourceFields(Schedule dbSchedule, ScheduleDTO scheduleDTO, long clinicId)
        {
            var brandId = scheduleDTO.BrandId;
            dbSchedule.Manufacturer = _db.Brands
                .Where(b => b.Id == (brandId ?? 0))
                .Select(b => b.Manufacturer)
                .FirstOrDefault() ?? "";
            dbSchedule.Lot = "";
            dbSchedule.Expiry = null;

            var selectedLot = string.IsNullOrWhiteSpace(scheduleDTO.Lot) ? null : scheduleDTO.Lot.Trim();
            var selectedExpiry = scheduleDTO.Expiry.HasValue ? scheduleDTO.Expiry.Value.Date : (DateTime?)null;

            Stock? stock = null;

            if (!string.IsNullOrWhiteSpace(selectedLot))
            {
                var selectedStockQuery = _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == (brandId ?? 0) && s.Bill.ClinicId == clinicId)
                    .Where(s => !string.IsNullOrWhiteSpace(s.BatchLot) && s.BatchLot.Trim() == selectedLot);

                if (selectedExpiry.HasValue)
                {
                    var selectedExpiryDate = selectedExpiry.Value;
                    selectedStockQuery = selectedStockQuery
                        .Where(s => s.Expiry.HasValue && s.Expiry.Value.Date == selectedExpiryDate);
                }

                stock = selectedStockQuery
                    .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                    .ThenBy(s => s.Expiry)
                    .ThenBy(s => s.Bill.BillDate)
                    .ThenBy(s => s.Id)
                    .FirstOrDefault();
            }

            stock ??= GetLatestStockByBrandAndClinic(brandId, clinicId);

            if (stock != null)
            {
                dbSchedule.Lot = stock.BatchLot ?? "";
                dbSchedule.Expiry = stock.Expiry;
            }
        }

        private long ResolveClinicIdForStock(long actorId, long fallbackClinicId)
        {
            if (actorId > 0)
            {
                var doctorOnlineClinicId = _db.Clinics
                    .Where(c => c.DoctorId == actorId && c.IsOnline)
                    .Select(c => c.Id)
                    .FirstOrDefault();

                if (doctorOnlineClinicId > 0)
                {
                    return doctorOnlineClinicId;
                }

                var paOnlineClinicId = _db.PaAccess
                    .Where(p => p.PersonalAssistantId == actorId && p.IsOnline)
                    .Select(p => p.ClinicId)
                    .FirstOrDefault();

                if (paOnlineClinicId > 0)
                {
                    return paOnlineClinicId;
                }
            }

            return fallbackClinicId;
        }

        private long ResolveClinicIdForUngive(Schedule schedule, long actorId, long fallbackClinicId)
        {
            var childClinicId = schedule.Child?.ClinicId ?? 0;

            // Prefer resolving from persisted stock source fields captured at give-time.
            if (schedule.BrandId.HasValue && schedule.BrandId.Value > 0)
            {
                var candidateStocks = _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == schedule.BrandId.Value && s.Bill != null);

                if (!string.IsNullOrWhiteSpace(schedule.Lot))
                {
                    candidateStocks = candidateStocks.Where(s => s.BatchLot == schedule.Lot);
                }

                if (schedule.Expiry.HasValue)
                {
                    var scheduleExpiryDate = schedule.Expiry.Value.Date;
                    candidateStocks = candidateStocks.Where(s => s.Expiry.HasValue && s.Expiry.Value.Date == scheduleExpiryDate);
                }
                else
                {
                    candidateStocks = candidateStocks.Where(s => !s.Expiry.HasValue);
                }

                var candidateClinicIds = candidateStocks
                    .Select(s => s.Bill.ClinicId)
                    .Distinct()
                    .ToList();

                if (candidateClinicIds.Count == 1)
                {
                    return candidateClinicIds[0];
                }

                if (childClinicId > 0 && candidateClinicIds.Contains(childClinicId))
                {
                    return childClinicId;
                }

                var actorClinicId = ResolveClinicIdForStock(actorId, childClinicId);
                if (actorClinicId > 0 && candidateClinicIds.Contains(actorClinicId))
                {
                    return actorClinicId;
                }

                if (candidateClinicIds.Count > 0)
                {
                    return candidateClinicIds[0];
                }
            }

            if (childClinicId > 0)
            {
                return childClinicId;
            }

            return fallbackClinicId;
        }

        private bool IsInventoryEnabledForClinic(long clinicId)
        {
            if (clinicId <= 0)
            {
                return true;
            }

            var allowInventory = _db.Clinics
                .Where(c => c.Id == clinicId)
                .Select(c => (bool?)c.Doctor.AllowInventory)
                .FirstOrDefault();

            return allowInventory ?? true;
        }

        private bool IsInventoryEnabledForActor(long actorId, long clinicId)
        {
            if (actorId > 0)
            {
                var doctorAllowInventory = _db.Doctors
                    .Where(d => d.Id == actorId)
                    .Select(d => (bool?)d.AllowInventory)
                    .FirstOrDefault();

                if (doctorAllowInventory.HasValue)
                {
                    return doctorAllowInventory.Value;
                }

                var paOnlineClinicId = _db.PaAccess
                    .Where(p => p.PersonalAssistantId == actorId && p.IsOnline)
                    .Select(p => (long?)p.ClinicId)
                    .FirstOrDefault();

                if (paOnlineClinicId.HasValue && paOnlineClinicId.Value > 0)
                {
                    return IsInventoryEnabledForClinic(paOnlineClinicId.Value);
                }
            }

            return IsInventoryEnabledForClinic(clinicId);
        }

        private static bool IsInfiniteDose(Dose? dose)
        {
            if (dose == null)
            {
                return false;
            }

            if (dose.Vaccine?.isInfinite == true)
            {
                return true;
            }

            var doseName = dose.Name ?? string.Empty;
            return doseName.StartsWith("Flu", StringComparison.OrdinalIgnoreCase)
                || doseName.StartsWith("Typhoid", StringComparison.OrdinalIgnoreCase)
                || doseName.StartsWith("Vitamin A", StringComparison.OrdinalIgnoreCase);
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

        // [HttpPost]
        // public Response<IEnumerable<ScheduleDTO>> Post(IEnumerable<ScheduleDTO> dsDTOS)
        // {
        //     foreach (var SchedueDTO in dsDTOS)
        //     {
        //         if (String.IsNullOrEmpty(SchedueDTO.DiseaseYear))
        //             SchedueDTO.DiseaseYear = "";

        //         var dbChild = _db.Childs.Where(x => x.Id == SchedueDTO.ChildId).FirstOrDefault();
        //         var dbDose = _db.Doses.Where(x => x.Id == SchedueDTO.DoseId).FirstOrDefault();
        //         SchedueDTO.Date = calculateDate(dbChild.DOB, dbDose.MinAge);
        //         Schedule SchduleDB = _mapper.Map<Schedule>(SchedueDTO);

        //         //  SchduleDB.Date = calculateDate(dbChild.DOB , dbDose.MinAge);
        //         _db.Schedules.Add(SchduleDB);
        //         _db.SaveChanges();
        //         SchedueDTO.Id = SchduleDB.Id;
        //     }
        //     return new Response<IEnumerable<ScheduleDTO>>(true, null, dsDTOS);
        // }
        [HttpPost]
        public Response<IEnumerable<ScheduleDTO>> Post(IEnumerable<ScheduleDTO> dsDTOS)
        {
            foreach (var scheduleDTO in dsDTOS)
            {
                if (String.IsNullOrEmpty(scheduleDTO.DiseaseYear))
                    scheduleDTO.DiseaseYear = "";

                var dbChild = _db.Childs.FirstOrDefault(x => x.Id == scheduleDTO.ChildId);
                var dbDose = _db.Doses.FirstOrDefault(x => x.Id == scheduleDTO.DoseId);
                scheduleDTO.Date = calculateDate(dbChild.DOB, dbDose.MinAge);
                if (string.IsNullOrEmpty(scheduleDTO.Expiry?.ToString()))
                {
                    scheduleDTO.Expiry = null;
                }
                Schedule scheduleDB = _mapper.Map<Schedule>(scheduleDTO);
                _db.Schedules.Add(scheduleDB);
                _db.SaveChanges();
                scheduleDTO.Id = scheduleDB.Id;
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
                    .Where(x =>x.Date.Date == scheduleDto.Date.Date && x.ChildId == scheduleDto.ChildId)
                    .ToList();
                var dbDose = _db.Doses.Include(x => x.Vaccine).ToList();
                var dbVacc = _db.Vaccines.Include(x => x.Doses).ToList();
                var vaccineIds = dbSchedule
                    .Select(x => x.Dose.VaccineId)
                    .Distinct()
                    .ToList();

                var vaccineBrandMap = _db.VaccineBrands
                    .Where(vb => vaccineIds.Contains(vb.VaccineId))
                    .Join(_db.Brands, vb => vb.BrandId, b => b.Id, (vb, b) => new { vb.VaccineId, Brand = b })
                    .ToList()
                    .GroupBy(x => x.VaccineId)
                    .ToDictionary(
                        x => x.Key,
                        x => _mapper.Map<List<BrandDTO>>(x.Select(y => y.Brand).Distinct().OrderBy(y => y.Name).ToList())
                    );

                List<ScheduleDTO> scheduleDTOs = new List<ScheduleDTO>();
                foreach (var schedule in dbSchedule)
                {
                    ScheduleDTO scheduleDTO = new ScheduleDTO();
                    scheduleDTO.Dose = _mapper.Map<DoseDTO>(schedule.Dose);
                    scheduleDTO.Id = schedule.Id;
                    if (vaccineBrandMap.TryGetValue(schedule.Dose.VaccineId, out var suggestedBrands))
                    {
                        scheduleDTO.Brands = suggestedBrands;
                    }
                    else
                    {
                        scheduleDTO.Brands = new List<BrandDTO>();
                    }
                    scheduleDTO.BrandId = schedule.BrandId;
                    var child = _db.Childs.Where(x => x.Id == schedule.ChildId).FirstOrDefault(); //child
                    var ClinicId = child.ClinicId;
                    var clinic = _db.Clinics.Where(x => x.Id == ClinicId).FirstOrDefault(); //clinic
                    var doctorId = clinic.DoctorId;

                    var brandAmount = _db.BrandAmounts
                        .Where(x => x.BrandId == schedule.BrandId && x.DoctorId == doctorId && x.Clinic.IsOnline == true)
                        .FirstOrDefault();
                    if (brandAmount != null)
                        scheduleDTO.Amount = brandAmount?.Amount;
                    else
                        scheduleDTO.Amount = schedule.Amount?? 0;
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
            var dbSchedule = _db.Schedules
                .Where(x => x.Id == scheduleDTO.Id)
                .Include(x => x.Child)
                .Include(x => x.Dose)
                .ThenInclude(x => x.Vaccine)
                .FirstOrDefault();

            if (dbSchedule == null)
            {
                return new Response<ScheduleDTO>(false, "Schedule not found", null);
            }

            // Fetch all schedules for the child on the same date with proper includes
            var dbChildSchedules = _db.Schedules
                .Include(x => x.Dose)
                .ThenInclude(x => x.Vaccine)
                .Where(x => x.ChildId == dbSchedule.ChildId 
                         && x.Date.Date == dbSchedule.Date.Date  // Compare only date part, ignore time
                         && x.IsSkip != true)
                .ToList();

            foreach (var schedule in dbChildSchedules)
            {
                schedule.Weight =(scheduleDTO.Weight > 0) ? scheduleDTO.Weight : schedule.Weight;
                schedule.Height =(scheduleDTO.Height > 0) ? scheduleDTO.Height : schedule.Height;
                schedule.Circle =(scheduleDTO.Circle > 0) ? scheduleDTO.Circle : schedule.Circle;
                schedule.IsDone = scheduleDTO.IsDone;
                schedule.GivenDate = scheduleDTO.GivenDate.Date;
                schedule.DoneAt = scheduleDTO.IsDone ? DateTime.UtcNow : (DateTime?)null;
                schedule.IsPAApprove= scheduleDTO.IsPAApprove;

                if (scheduleDTO.ScheduleBrands.Count > 0)
                {
                    var scheduleBrand = scheduleDTO.ScheduleBrands.Find(
                        x => x.ScheduleId == schedule.Id
                    );
                    if (scheduleBrand != null)
                    {
                        var previousBrandId = schedule.BrandId;
                        schedule.BrandId = scheduleBrand.BrandId;
                        if (scheduleDTO.GivenDate.Date == DateTime.UtcNow.AddHours(5).Date)
                        {
                            // Null brand means OHF/external source; do not consume inventory.
                            if (!scheduleBrand.BrandId.HasValue || scheduleBrand.BrandId.Value <= 0)
                            {
                                // Keep schedule update flow; only skip inventory consumption.
                            }

                            if (scheduleBrand.BrandId.HasValue && scheduleBrand.BrandId.Value > 0)
                            {
                                var onlineClinicId = ResolveClinicIdForStock(
                                    scheduleDTO.DoctorId,
                                    schedule.Child?.ClinicId ?? 0
                                );

                                if (onlineClinicId <= 0)
                                {
                                    return new Response<ScheduleDTO>(
                                        false,
                                        "Unable to resolve online clinic for inventory consumption.",
                                        null
                                    );
                                }

                                var inventoryEnabled = IsInventoryEnabledForActor(scheduleDTO.DoctorId, onlineClinicId);
                                if (inventoryEnabled)
                                {
                                    var inventoryDoctorId = _db.Clinics
                                        .Where(c => c.Id == onlineClinicId)
                                        .Select(c => c.DoctorId)
                                        .FirstOrDefault();

                                    if (inventoryDoctorId <= 0)
                                    {
                                        return new Response<ScheduleDTO>(
                                            false,
                                            $"Unable to resolve inventory owner doctor for clinic {onlineClinicId}.",
                                            null
                                        );
                                    }

                                    var brandInventory = _db.BrandAmounts
                                        .Where(
                                            b =>
                                                b.BrandId == scheduleBrand.BrandId.Value
                                                && b.DoctorId == inventoryDoctorId
                                                && b.ClinicId == onlineClinicId
                                        )
                                        .FirstOrDefault();

                                    if (!previousBrandId.HasValue)
                                    {
                                        if (brandInventory == null)
                                        {
                                            return new Response<ScheduleDTO>(
                                                false,
                                                BuildInventoryContextMessage(
                                                    "Inventory row not found for brand",
                                                    scheduleBrand.BrandId.Value,
                                                    onlineClinicId
                                                ),
                                                null
                                            );
                                        }

                                        if (brandInventory.Count <= 0)
                                        {
                                            return new Response<ScheduleDTO>(
                                                false,
                                                BuildInventoryContextMessage(
                                                    "Insufficient inventory for brand",
                                                    scheduleBrand.BrandId.Value,
                                                    onlineClinicId
                                                ),
                                                null
                                            );
                                        }

                                        brandInventory.Count--;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Only reschedule future doses for non-infinite vaccines
                if (!IsInfiniteDose(schedule.Dose))
                {
                    ChangeDueDatesOfInjectedSchedule(scheduleDTO, schedule);
                }
            }
            _db.SaveChanges();
            return new Response<ScheduleDTO>(true, "schedule updated successfully.", null);
        }

        [HttpPut("update-bulk-invoice")]
        public Response<object> updateInvoice([FromBody] BulkInvoiceSubmitDTO dto)
        {
            if (dto.PaId.HasValue)
            {
                var existing = _db.InvoiceSubmissions.FirstOrDefault(x =>
                    x.ChildId == dto.ChildId &&
                    x.DoctorId == dto.DoctorId &&
                    x.InvoiceDate.Date == dto.InvoiceDate.Date);

                if (existing != null)
                {
                    if (existing.EditCount >= 1)
                        return new Response<object>(false, "Invoice has already been edited once. Further changes are not allowed.", null);

                    if (existing.SubmittedAt.Date != DateTime.UtcNow.Date)
                        return new Response<object>(false, "Invoice can only be edited on the same day it was first submitted.", null);

                    if (existing.PaId != dto.PaId)
                        return new Response<object>(false, "Only the PA who submitted this invoice can edit it.", null);

                    existing.EditCount++;
                    existing.ConsultationFee = dto.ConsultationFee;
                    _db.Entry(existing).State = EntityState.Modified;

                    _db.PaActivityLogs.Add(new PaActivityLog
                    {
                        PaId = dto.PaId.Value,
                        DoctorId = dto.DoctorId,
                        ClinicId = dto.ClinicId,
                        PatientId = dto.ChildId,
                        ActionCode = "InvoiceEdit",
                        Description = "PA edited invoice prices after first submission",
                        Notes = "Consultation fee: " + dto.ConsultationFee,
                        IsReversal = false,
                        ActionDate = DateTime.UtcNow
                    });
                }
                else
                {
                    _db.InvoiceSubmissions.Add(new InvoiceSubmission
                    {
                        ChildId = dto.ChildId,
                        DoctorId = dto.DoctorId,
                        PaId = dto.PaId,
                        ClinicId = dto.ClinicId,
                        InvoiceDate = dto.InvoiceDate.Date,
                        SubmittedAt = DateTime.UtcNow,
                        ConsultationFee = dto.ConsultationFee,
                        EditCount = 0
                    });

                    _db.PaActivityLogs.Add(new PaActivityLog
                    {
                        PaId = dto.PaId.Value,
                        DoctorId = dto.DoctorId,
                        ClinicId = dto.ClinicId,
                        PatientId = dto.ChildId,
                        ActionCode = "InvoiceSubmit",
                        Description = "PA submitted invoice",
                        Notes = "Consultation fee: " + dto.ConsultationFee,
                        ActionDate = DateTime.UtcNow
                    });
                }
            }

            foreach (var item in dto.Schedules)
            {
                var schedulec = _db.Schedules.FirstOrDefault(x => x.Id == item.Id);
                if (schedulec != null)
                    schedulec.Amount = item.Amount;
            }

            _db.SaveChanges();
            return new Response<object>(true, "Invoice updated successfully.", null);
        }

        [HttpGet("invoice-status")]
        public ActionResult GetInvoiceStatus([FromQuery] long childId, [FromQuery] long doctorId, [FromQuery] DateTime invoiceDate)
        {
            var submission = _db.InvoiceSubmissions.FirstOrDefault(x =>
                x.ChildId == childId &&
                x.DoctorId == doctorId &&
                x.InvoiceDate.Date == invoiceDate.Date);

            if (submission == null)
                return Ok(new { isSubmitted = false, editCount = 0, canEdit = true, submittedByPaId = (long?)null });

            bool canEdit = submission.EditCount < 1 && submission.SubmittedAt.Date == DateTime.UtcNow.Date;
            return Ok(new { isSubmitted = true, editCount = submission.EditCount, canEdit, submittedByPaId = submission.PaId });
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
                || GapInDays == 5840
                || GapInDays == 6205
                || GapInDays == 6570
                || GapInDays == 6935
                || GapInDays == 7300
                || GapInDays == 7665
                || GapInDays == 8030
                || GapInDays == 8395
                || GapInDays == 8760
                || GapInDays == 9125
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

        private string BuildInventoryContextMessage(string prefix, long? brandId, long clinicId)
        {
            var brandName = _db.Brands
                .Where(b => b.Id == (brandId ?? 0))
                .Select(b => b.Name)
                .FirstOrDefault();

            var clinicName = _db.Clinics
                .Where(c => c.Id == clinicId)
                .Select(c => c.Name)
                .FirstOrDefault();

            var safeBrandName = string.IsNullOrWhiteSpace(brandName) ? "Unknown Brand" : brandName;
            var safeClinicName = string.IsNullOrWhiteSpace(clinicName) ? "Unknown Clinic" : clinicName;
            var brandIdLabel = brandId.HasValue ? brandId.Value.ToString() : "null";

            return $"{prefix} {safeBrandName} (ID: {brandIdLabel}) in online clinic {safeClinicName} (ID: {clinicId}).";
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
                if (IsInfiniteDose(dbSchedule.Dose))
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
        public async Task<Response<List<Schedule>>> Delete(long ChildId, long DoseId, string date)
        {
            DateTime dateOfInjection = DateTime.ParseExact(date, "dd-MM-yyyy", null);
            var dose = await _db.Doses.FirstOrDefaultAsync(d => d.Id == DoseId);
            if (dose == null)
                return new Response<List<Schedule>>(false, "Dose not found.", null);
            var infiniteVaccineNames = new[] { "Typhoid", "Flu", "Vitamin A" };
            bool isInfinite = infiniteVaccineNames.Any(name =>
                dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

            if (isInfinite)
            {
                var undoneSchedules = await _db.Schedules
                    .Include(x => x.Dose)
                    .Where(x => x.ChildId == ChildId
                        && x.Dose.VaccineId == dose.VaccineId
                        && x.IsDone == false
                        && x.IsSkip != true)
                    .OrderBy(x => x.Date)
                    .ToListAsync();

                if (undoneSchedules.Count == 0)
                {
                    return new Response<List<Schedule>>(false, "No undone infinite doses found.", null);
                }
                var scheduleToKeep = undoneSchedules.First();
                var schedulesToDelete = undoneSchedules.Skip(1).ToList();

                if (schedulesToDelete.Any())
                {
                    _db.Schedules.RemoveRange(schedulesToDelete);
                }
                await _db.SaveChangesAsync();
                return new Response<List<Schedule>>(true, "Only one infinite dose left as undone.",null);
            }
            else
            {
                var objList = await _db.Schedules
                    .Include(x => x.Dose)
                    .Where(x => x.ChildId == ChildId)
                    .Where(x => x.DoseId == DoseId)
                    .Where(x => x.IsDone == false)
                    .ToListAsync();

                var futureDoses = objList.Where(x => x.Date > dateOfInjection).ToList();
        
                if (!futureDoses.Any())
                {
                    return new Response<List<Schedule>>(false, "No future doses found to delete.", null);
                }
                _db.Schedules.RemoveRange(futureDoses);
                await _db.SaveChangesAsync();
                return new Response<List<Schedule>>(true, null, futureDoses);
            }
        }

        [HttpGet("alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ScheduleDTO>> GetAlert(DateTime inputDate, int GapDays, long OnlineClinicId)
        {
            {
                List<Schedule> schedules = GetAlertData(inputDate, GapDays, OnlineClinicId, _db);
                IEnumerable<ScheduleDTO> scheduleDTO = _mapper.Map<IEnumerable<ScheduleDTO>>(
                        schedules
                    );
                return new Response<IEnumerable<ScheduleDTO>>(true, null, scheduleDTO);
            }
        }

        private static List<Schedule> GetAlertData(DateTime inputDate, int GapDays, long OnlineClinicId, Context db)
        {
            List<Schedule> schedules = new List<Schedule>();
            var doctor = db.Clinics
               .Where(x => x.Id == OnlineClinicId)
               .Include(x => x.Doctor)
               .First<Clinic>()
               .Doctor;
            var clinics = db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();
            long[] ClinicIDs = clinics.Select(x => x.Id).ToArray<long>();
            // DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
            DateTime AddedDateTime = inputDate.AddDays(GapDays);
            // DateTime NextDayTime = inputDate.AddDays(1).Date;

            if (GapDays == 0)
            {
                schedules = db.Schedules
                    .Include(x => x.Child)
                    .ThenInclude(x => x.User)
                    .Include(x => x.Dose)
                    .Where(c => c.Child.ClinicId == OnlineClinicId)
                    .Where(c => c.Date.Date == inputDate.Date)
                    .Where(c => c.IsDone != true && c.IsSkip != true && c.Child.IsInactive != true)
                    .OrderBy(x => x.Child.Name)
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
                    .Where(c => c.Child.ClinicId == OnlineClinicId)
                    .Where(c => c.Date.Date > inputDate.Date && c.Date.Date <= AddedDateTime.Date)
                    .Where(c => c.IsDone != true && c.IsSkip != true && c.Child.IsInactive != true)
                    .OrderBy(x => x.Child.Name)
                    .ThenBy(x => x.Date)
                    .ToList<Schedule>();
            }
            else if (GapDays < 0)
            {
                schedules = db.Schedules
                    .Include(x => x.Child)
                    .ThenInclude(x => x.User)
                    .Include(x => x.Dose)
                    .Where(c => c.Child.ClinicId == OnlineClinicId)
                    .Where(c => c.Date < inputDate.Date && c.Date >= AddedDateTime)
                    .Where(c => c.IsDone != true && c.IsSkip != true && c.Child.IsInactive != true)
                    .OrderBy(x => x.Child.Name)
                    .ThenBy(x => x.Date)
                    .ToList<Schedule>();
            }
            Dictionary<string, string> map = AddDoseNames(schedules);
            List<Schedule> listOfSchedules = removeDuplicateRecords(schedules, map);
            return listOfSchedules;
        }

        ///////////////
       [HttpGet("alert2/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ChildDTO>> GetAlert2(int GapDays, long OnlineClinicId)
        {
            List<Schedule> schedules = GetAlertData2(GapDays, OnlineClinicId, _db);

            // Fetch all child IDs from schedules
            var childIds = schedules.Select(s => s.Child.Id).Distinct().ToList();

            // Fetch all children with User info in one query
            var children = _db.Childs
                .Include(c => c.User)
                .Where(c => childIds.Contains(c.Id))
                .ToList();

            // Map to DTOs and include mobile/password
            var childInfoDTOs = children.Select(c => new ChildDTO
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                ClinicId = c.ClinicId,
                MobileNumber = c.User?.MobileNumber,
                Password = c.User?.Password
            }).ToList();

            foreach (var child in childInfoDTOs)
            {
                if (string.IsNullOrEmpty(child.Email))
                    continue;

                var ClinicId = child.ClinicId;
                var doctor = _db.Clinics
                    .Where(x => x.Id == ClinicId)
                    .Include(x => x.Doctor)
                    .FirstOrDefault()
                    ?.Doctor;
                var clinics = _db.Clinics.FirstOrDefault(x => x.Id == ClinicId);

                var dbSchedules = _db.Schedules
                    .Where(x => x.ChildId == child.Id && x.Date == DateTime.Today)
                    .Include(x => x.Dose)
                    .ToList();

                string body;
                if (dbSchedules.Any())
                {
                    var doseNames = string.Join(", ", dbSchedules.Select(s => s.Dose.Name));
                    body = $"Reminder: Vaccination {doseNames} for Child: {child.Name} is due today.\n" +
                        $"Kindly book an appointment at Clinic: {clinics?.Name}, with Doctor: {doctor?.FirstName}, at Phone: {clinics?.PhoneNumber}\n" +
                         $"Login and check your record at https://client.vaccinationcentre.com\n" +
                        $"Mobile Number: {child.MobileNumber ?? "N/A"}\n" +
                        $"Password: {child.Password ?? "N/A"}";
                }
                else
                {
                    body = "No schedule found for the specified date.";
                }

                try
                {
                    UserEmail.SendEmail(child.Email, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error sending email: " + ex.Message);
                }
            }

            return new Response<IEnumerable<ChildDTO>>(true, null, childInfoDTOs);
        }

        [HttpGet("alertone/{ChildId}")]
        public Response<object> SendAlertEmail(long ChildId)
        {
            var child = _db.Childs
                .Include(c => c.Clinic)
                .Include(c => c.User) 
                .FirstOrDefault(c => c.Id == ChildId);

            if (child == null || string.IsNullOrEmpty(child.Email))
            {
                return new Response<object>(false, "Child not found or email is missing.", null);
            }

            var specificDate = DateTime.Today;
            var todaySchedules = _db.Schedules
                .Where(s => s.ChildId == ChildId && s.Date == specificDate)
                .Include(s => s.Dose)
                .ToList();

            string body;
            if (todaySchedules.Any())
            {
                var clinic = _db.Clinics.Include(x => x.Doctor).FirstOrDefault(x => x.Id == child.ClinicId);
                var doseDetails = todaySchedules.Select(s => $" {s.Dose.Name},").ToList();

                body = $"Reminder: Vaccination {string.Join(" ", doseDetails)} of {child.Name} is due.\n" +
                    $"Please confirm your appointment. Thanks! Dr {clinic?.Doctor?.FirstName} {clinic?.Name}\n" +
                    $"Phone Number: {clinic?.PhoneNumber}\n" +
                    $"Login and check your record at https://client.vaccinationcentre.com\n" +
                    $"Mobile Number: {child.User?.MobileNumber ?? "N/A"}\n" +
                    $"Password: {child.User?.Password ?? "N/A"}";
            }
            else
            {
                body = "No schedule found for today.";
            }

            try
            {
                UserEmail.SendEmail(child.Email, body);
                return new Response<object>(true, "Email sent successfully.", new
                {
                    child.Id,
                    child.Email,
                    MobileNumber = child.User?.MobileNumber,
                    Password = child.User?.Password
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending email: " + ex.Message);
                return new Response<object>(false, "Failed to send email.", null);
            }
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
                    .Where(c => c.IsDone != true && c.IsSkip != true && c.Child.IsInactive != true)
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
            DateTime inputDate,
            int GapDays,
            int OnlineClinicId
        )
        {
            {
                List<Schedule> Schedules = GetAlertData(inputDate, GapDays, OnlineClinicId, _db);
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

        [HttpGet("doses-for-child/{childId}/{onlineClinicId}")]
        public Response<List<DoseDTO>> GetAllDosesDueForChild(int childId, long onlineClinicId, DateTime? date = null)
        {
            DateTime selectedDate = date?.Date ?? DateTime.Today;

            // Fetch the child with their associated clinic and doctor
            var child = _db.Childs
                .Include(c => c.Clinic)
                .ThenInclude(clinic => clinic.Doctor) // Include the doctor associated with the clinic
                .Include(c => c.User) // Include the user associated with the child
                .FirstOrDefault(c => c.Id == childId);

            if (child == null)
            {
                return new Response<List<DoseDTO>>(false, "Child not found.", null);
            }

            // Fetch schedules for the child on the selected date and for the specific clinic
            var schedules = _db.Schedules
                .Include(s => s.Dose)
                .ThenInclude(dose => dose.Vaccine) // Include vaccine details
                .Include(s => s.Child.Clinic.Doctor) // Include clinic and doctor details
                .Where(s => s.ChildId == childId &&
                            //s.Child.ClinicId == onlineClinicId && // Filter by the current online clinic
                            (s.IsDone == false || s.IsDone == null) &&
                            (s.IsSkip == false || s.IsSkip == null) &&
                            s.Date.Date == selectedDate)
                .OrderBy(s => s.Date)
                .ToList();

            // Map schedules to DoseDTO
            var doseDtos = schedules
                .Select(s => new DoseDTO
                {
                    Id = s.Dose.Id,
                    Name = s.Dose.Name,
                    CountryCode = s.Child.User.CountryCode ?? "+92",    // Use child's user info
                    PhoneNumber = s.Child.User.MobileNumber ?? "Unknown",
                    Vaccine = _mapper.Map<VaccineDTO>(s.Dose.Vaccine),
                    Clinic = new ClinicDTO // Include clinic details in the response
                    {
                        Id = s.Child.Clinic.Id,
                        Name = s.Child.Clinic.Name,
                        PhoneNumber = s.Child.Clinic.PhoneNumber,
                        DoctorName = s.Child.Clinic.Doctor?.DisplayName ?? "Unknown Doctor"
                    }
                })
                .ToList();

            if (!doseDtos.Any())
            {
                return new Response<List<DoseDTO>>(false, $"No doses due on {selectedDate:yyyy-MM-dd} for the given child ID and clinic.", null);
            }

            // Prepare the response message
            var doctorName = child.Clinic?.Doctor?.DisplayName ?? "Unknown Doctor";
            var clinicName = child.Clinic?.Name ?? "Unknown Clinic";
            var clinicPhoneNumber = child.Clinic?.PhoneNumber ?? "Unknown Phone Number";
            var message = $"Doses due for {child.Name} at {clinicName} (Phone: {clinicPhoneNumber}) by Dr. {doctorName}.";

            return new Response<List<DoseDTO>>(true, message, doseDtos);
        }

        [HttpGet("doctor-sales-pdf/{doctorId}")]
        public IActionResult GetDoctorSalesPdf(long doctorId)
        {
            try
            {
                var today = DateTime.Today;
                var schedules = _db.Schedules
                    .Include(s => s.Child)
                    .Include(s => s.Dose)
                        .ThenInclude(d => d.Vaccine)
                    .Include(s => s.Brand)
                        .ThenInclude(b => b.BrandAmounts)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date == today
                            && s.IsDone == true)
                    .OrderBy(s => s.Child.Name) // Order by patient name
                    .ToList();

                if (!schedules.Any())
                    return NotFound("No vaccines administered today");

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    document.Open();

                    // Header setup
                    Font titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    Paragraph title = new Paragraph("Daily Vaccine Sales Report", titleFont);
                    title.Alignment = Element.ALIGN_CENTER;
                    title.SpacingAfter = 20f;
                    document.Add(title);

                    // Create table
                    PdfPTable table = new PdfPTable(6);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1.5f, 2f, 1.5f, 1.2f, 1.2f, 1.2f });

                    // Add headers
                    string[] headers = { "Patient", "Vaccines", "Purchase Value", "Sale Value", "Profit", "Consultation" };
                    foreach (string header in headers)
                    {
                        var cell = new PdfPCell(new Phrase(header, headerFont))
                        {
                            // BackgroundColor = BaseColor.LIGHT_GRAY,
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5
                        };
                        table.AddCell(cell);
                    }

                    string currentPatient = "";
                    int index = 1;
                    decimal totalPurchase = 0;
                    decimal totalSale = 0;
                    decimal totalProfit = 0;

                    foreach (var schedule in schedules)
                    {
                        var brandAmount = schedule.Brand?.BrandAmounts?
                            .FirstOrDefault(ba => ba.DoctorId == doctorId);
                        decimal purchaseAmount = brandAmount?.PurchasedAmt ?? 0;
                        decimal saleAmount = brandAmount?.Amount ?? 0;
                        decimal profit = saleAmount - purchaseAmount;
                        decimal consultation = 0;

                        totalPurchase += purchaseAmount;
                        totalSale += saleAmount;
                        totalProfit += profit;

                        // Add row
                        // table.AddCell(new PdfPCell(new Phrase(index++.ToString(), normalFont))
                        // { HorizontalAlignment = Element.ALIGN_CENTER });

                        // Only show patient name if it's different from the previous row
                        if (currentPatient != schedule.Child.Name)
                        {
                            currentPatient = schedule.Child.Name;
                            table.AddCell(new PdfPCell(new Phrase(currentPatient, normalFont))
                            { HorizontalAlignment = Element.ALIGN_LEFT });
                        }
                        else
                        {
                            table.AddCell(new PdfPCell(new Phrase("", normalFont))
                            { HorizontalAlignment = Element.ALIGN_CENTER });
                        }

                        // table.AddCell(new PdfPCell(new Phrase(schedule.Dose.Vaccine.Name, normalFont))
                        // { HorizontalAlignment = Element.ALIGN_LEFT });

                        table.AddCell(new PdfPCell(new Phrase(schedule.Brand?.Name ?? "", normalFont))
                        { HorizontalAlignment = Element.ALIGN_LEFT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{purchaseAmount:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{saleAmount:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{profit:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{consultation:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });
                    }

                    // Add totals row
                    var totalCell = new PdfPCell(new Phrase("Totals", headerFont))
                    {
                        Colspan = 4,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        // BackgroundColor = BaseColor.LIGHT_GRAY
                    };
                    table.AddCell(totalCell);

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalPurchase:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalSale:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalProfit:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalProfit:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    document.Add(table);

                    // Add summary
                    Paragraph summary = new Paragraph(
                        $"\nTotal Patients: {schedules.Select(s => s.Child.Name).Distinct().Count()}" +
                        $"\nTotal Vaccines: {schedules.Count}" +
                        $"\nTotal Purchase Value: {schedules.Count}" +
                        $"\nTotal Sale Value: ₹{totalSale:N0}" +
                        $"\nTotal Profit: ₹{totalProfit:N0}" +
                        $"\nTotal Consultation: ₹{totalPurchase:N0}" +
                        $"\nGrand total cash: ₹{totalSale:N0}",
                        headerFont);
                    summary.SpacingBefore = 20f;
                    document.Add(summary);

                    document.Close();
                    return File(ms.ToArray(), "application/pdf", $"DailySales_{today:yyyyMMdd}.pdf");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }

        private PdfPCell CreateCell(
            string text,
            string fontStyle,
            int colspan,
            string alignment,
            string description
        )
        {
            Font font =
                fontStyle == "bold"
                    ? FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)
                    : FontFactory.GetFont(FontFactory.HELVETICA, 10);

            PdfPCell cell = new PdfPCell(new Phrase(text, font))
            {
                Colspan = colspan,
                Border = 0,
                HorizontalAlignment =
                    alignment == "left" ? Element.ALIGN_LEFT : Element.ALIGN_RIGHT,
                Padding = 5,
            };

            return cell;
        }

        [HttpPatch("{id}/ispaapprove")]
        public async Task<IActionResult> PatchIsPAApprove(long id)
        {
            try
            {
                var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id);
                if (schedule == null)
                {
                    return NotFound(new { message = "Schedule not found." });
                }
                schedule.IsPAApprove = true;
                await _db.SaveChangesAsync();
                return Ok(new { message = "IsPAApprove updated successfully.", schedule });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
            }
        }

         public class PdfFooter : PdfPageEventHelper
        {
            public override void OnEndPage(PdfWriter writer, Document document)
            {
                TimeZoneInfo pakistanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
                DateTime pakistanTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pakistanTimeZone);
                string dateTimeStamp = pakistanTime.ToString("yyyy-MM-dd hh:mm tt");
                Font footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                PdfPTable footerTable = new PdfPTable(1);
                footerTable.TotalWidth =document.PageSize.Width - document.LeftMargin - document.RightMargin;
                footerTable.DefaultCell.Border = Rectangle.NO_BORDER;
                footerTable.DefaultCell.HorizontalAlignment = Element.ALIGN_CENTER;
                footerTable.AddCell(new Phrase($"Printed on: {dateTimeStamp}", footerFont));
                footerTable.WriteSelectedRows(0,-1,document.LeftMargin,document.BottomMargin - 10,writer.DirectContent);
            }
        }

        [HttpGet("clinic-report-pdf/{clinicId}")]
        public IActionResult GenerateClinicReportPdf(long clinicId,[FromQuery] string fromDate,[FromQuery] string toDate)
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);
                var clinic = _db
                    .Clinics.Include(c => c.Doctor)
                    .FirstOrDefault(c => c.Id == clinicId);

                if (clinic == null)
                {
                    return NotFound("Clinic not found.");
                }

                var doctorName = clinic.Doctor?.DisplayName ?? "Unknown Doctor";
                var additionalInfo = clinic.Doctor?.AdditionalInfo ?? "No additional info";
                var clinicName = clinic.Name ?? "Unknown Clinic";
                var monogramImage = clinic.MonogramImage ?? "default-monogram.png";
                var address = clinic.Address ?? "Unknown Address";
                var phoneNumber = clinic.PhoneNumber ?? "Unknown Phone Number";

                var nextDay = parsedToDate.Date.AddDays(1);

                var rawSchedules = _db.Schedules
                    .Where(s =>
                        s.Child.ClinicId == clinicId
                        && s.GivenDate.HasValue
                        && s.GivenDate.Value >= parsedFromDate.Date
                        && s.GivenDate.Value < nextDay
                        && s.IsDone == true
                    )
                    .Select(s => new
                    {
                        Id = s.Child.Id,
                        Name = s.Child.Name,
                        ChildId = s.ChildId,
                        s.DoseId,
                        VaccineName = s.Dose.Vaccine.Name,
                        DoseName = s.Dose.Name,
                        GivenDate = s.GivenDate.Value,
                        DoctorName = s.Child.Clinic.Doctor.DisplayName,
                        BrandName = s.Brand.Name ?? "Unknown Brand",
                    })
                    .OrderBy(s => s.GivenDate)
                    .ToList();

                if (!rawSchedules.Any())
                {
                    return NotFound("No data found for the specified clinic and date range.");
                }

                var childIds = rawSchedules.Select(s => s.ChildId).Distinct().ToList();
                var doseIds = rawSchedules.Select(s => s.DoseId).Distinct().ToList();

                var invoiceMap = _db.Invoices
                    .Where(i =>
                        childIds.Contains(i.ChildId)
                        && i.ClinicId == clinicId
                        && doseIds.Contains(i.DoseId)
                    )
                    .Select(i => new { i.InvoiceId, i.ChildId, i.DoseId, i.Amount })
                    .ToList()
                    .GroupBy(i => (i.ChildId, i.DoseId))
                    .ToDictionary(g => g.Key, g => g.First());

                var invoiceIds = invoiceMap.Values.Select(i => i.InvoiceId).ToList();

                var feeMap = _db.Fee
                    .Where(f => invoiceIds.Contains(f.InvoiceId))
                    .Select(f => new { f.InvoiceId, f.Amount })
                    .ToList()
                    .ToDictionary(f => f.InvoiceId, f => (decimal)f.Amount);

                var schedules = rawSchedules.Select(s =>
                {
                    invoiceMap.TryGetValue((s.ChildId, s.DoseId), out var invoice);
                    var feeAmt = invoice != null && feeMap.TryGetValue(invoice.InvoiceId, out var fa) ? fa : 0m;
                    return new
                    {
                        s.Id,
                        s.Name,
                        s.DoseId,
                        s.VaccineName,
                        s.DoseName,
                        s.GivenDate,
                        s.DoctorName,
                        InvoicePrice = invoice?.Amount ?? 0m,
                        ConsultationFee = feeAmt,
                        s.BrandName,
                    };
                }).ToList();

                var groupedSchedules = schedules
                    .GroupBy(s => new { s.Id, s.Name })
                    .Select(patientGroup => new
                    {
                        Patient = patientGroup.Key,
                        Dates = patientGroup.GroupBy(s => s.GivenDate.Date),
                    });

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    writer.PageEvent = new PdfFooter();
                    document.Open();
                    PdfPTable upperTable = new PdfPTable(2);
                    float[] upperTableWidths = new float[] { 350f, 160f };
                    upperTable.HorizontalAlignment = 0;
                    upperTable.TotalWidth = 510f;
                    upperTable.LockedWidth = true;
                    upperTable.SetWidths(upperTableWidths);
                    Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                    Phrase phrase = new Phrase();
                    phrase.Add(new Chunk(doctorName + "\n", boldFont));
                    phrase.Add(new Chunk(additionalInfo + "\n", regularFont));
                    phrase.Add(new Chunk(clinicName + "\n", boldFont));
                    phrase.Add(new Chunk(address + "\n", regularFont));
                    phrase.Add(new Chunk(phoneNumber, regularFont));

                    // Create the cell
                    PdfPCell leftCell = new PdfPCell(phrase)
                    {
                        Border = 0,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 5,
                    };

                    upperTable.AddCell(leftCell);

                    var logoPath = Path.Combine(_host.ContentRootPath, monogramImage);
                    PdfPCell imageCell = new PdfPCell(new Phrase(""))
                    {
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    };
                    if (System.IO.File.Exists(logoPath))
                    {
                        var img = Image.GetInstance(logoPath);
                        img.ScaleAbsolute(160f, 50f);
                        imageCell = new PdfPCell(img, false)
                        {
                            Border = 0,
                            FixedHeight = 50f,
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                        };
                    }
                    upperTable.AddCell(imageCell);

                    document.Add(upperTable);

                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    Paragraph titletext = new Paragraph(
                        $"Sales Report",
                        headerFont
                    );
                    titletext.Alignment = Element.ALIGN_CENTER;
                    document.Add(titletext);

                    Paragraph dateRange = new Paragraph(
                        $"FROM {parsedFromDate:dd-MM-yyyy} TO {parsedToDate:dd-MM-yyyy}",
                        normalFont
                    );
                    dateRange.Alignment = Element.ALIGN_CENTER;
                    dateRange.SpacingAfter = 10f;
                    document.Add(dateRange);

                    PdfPTable table = new PdfPTable(6);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1.5f, 2f, 2f, 2f, 1.5f, 2f });

                    string[] headers ={"Date","Patient","Consultation Fee","Item","Quantity","Price",};
                    foreach (string header in headers)
                    {
                        var cell = new PdfPCell(new Phrase(header, headerFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 6,
                            BackgroundColor = BaseColor.LightGray,
                        };
                        table.AddCell(cell);
                    }

                    decimal grandTotalConsultationFee = 0;

                    foreach (var patientGroup in groupedSchedules)
                    {
                        decimal totalConsultationForPatient = patientGroup
                            .Dates.SelectMany(d => d)
                            .GroupBy(s => s.ConsultationFee) // Group by ConsultationFee to ensure it's added only once per invoice
                            .Select(g => g.Key) // Get unique ConsultationFee values
                            .Sum();

                        grandTotalConsultationFee += totalConsultationForPatient;

                        decimal totalPriceForPatient = patientGroup.Dates.Sum(d =>
                            d.Sum(s => s.InvoicePrice)
                        );

                        decimal totalPrice = totalPriceForPatient + totalConsultationForPatient;

                        foreach (var dateGroup in patientGroup.Dates)
                        {
                            bool isFirstRowForDate = true;
                            foreach (var schedule in dateGroup)
                            {
                                if (isFirstRowForDate)
                                {
                                    table.AddCell(
                                        new PdfPCell(
                                            new Phrase(
                                                schedule.GivenDate.ToString("dd-MM-yyyy"),
                                                normalFont
                                            )
                                        )
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(
                                            new Phrase(
                                                patientGroup.Patient.Name ?? "Unknown Patient",
                                                headerFont
                                            )
                                        )
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(
                                            new Phrase(
                                                $"₹{schedule.ConsultationFee:N2}",
                                                normalFont
                                            )
                                        )
                                        {
                                            HorizontalAlignment = Element.ALIGN_RIGHT,
                                        }
                                    );

                                    isFirstRowForDate = false;
                                }
                                else
                                {
                                    table.AddCell(
                                        new PdfPCell(new Phrase("", normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(new Phrase("", normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(new Phrase("", normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                }

                                table.AddCell(
                                    new PdfPCell(
                                        new Phrase(
                                            schedule.BrandName ?? "Unknown Vaccine",
                                            normalFont
                                        )
                                    )
                                    {
                                        HorizontalAlignment = Element.ALIGN_LEFT,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase("1", normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_RIGHT,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(
                                        new Phrase($"₹{schedule.InvoicePrice:N2}", normalFont)
                                    )
                                    {
                                        HorizontalAlignment = Element.ALIGN_RIGHT,
                                    }
                                );
                            }
                        }

                        var totalCell = new PdfPCell(
                            new Phrase(
                                $"Total for {patientGroup.Patient.Name}: ₹{totalPrice:N2}",
                                headerFont
                            )
                        )
                        {
                            Colspan = 6,
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                            Padding = 6,
                        };
                        table.AddCell(totalCell);
                    }


                    document.Add(table);

                    Paragraph summary = new Paragraph(
                        $"\nTotal Patients: {groupedSchedules.Count()}"
                            + $"\nTotal Vaccination Fee: ₹{grandTotalConsultationFee:N2}"
                            + $"\nTotal Items Price: ₹{schedules.Sum(s => s.InvoicePrice):N2}"
                            + $"\nGrand Total Cash: ₹{schedules.Sum(s => s.InvoicePrice) + grandTotalConsultationFee:N2}",
                        headerFont
                    );
                    summary.SpacingBefore = 20f;
                    document.Add(summary);

                    document.Close();
                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        $"SalesReport_{clinicId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
                    );
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }
    }
}
