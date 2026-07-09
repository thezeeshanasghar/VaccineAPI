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
using VaccineAPI.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using VaccineAPI.helper;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduleController : ControllerBase
    {
        private readonly Context _db;

        private readonly IWebHostEnvironment _host;

        private readonly IMapper _mapper;

        private readonly InventoryTransactionService _inventory;

        public ScheduleController(Context context, IMapper mapper, IWebHostEnvironment host, InventoryTransactionService inventory)
        {
            _host = host;
            _db = context;
            _mapper = mapper;
            _inventory = inventory;
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
            // PA permission check
            if (scheduleDTO.PaId.HasValue)
            {
                var paPerm = _db.PaPermissions.FirstOrDefault(p => p.PaId == scheduleDTO.PaId.Value);
                if (paPerm == null || !paPerm.AddSpecialDoses)
                    return new Response<ScheduleDTO>(false, "You do not have permission to add vaccines to the schedule.", null);
            }

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

            if (scheduleDTO.PaId.HasValue)
            {
                var addedDose = _db.Doses.Include(x => x.Vaccine).FirstOrDefault(x => x.Id == scheduleDTO.DoseId);
                var addedDoseName = addedDose?.Name ?? addedDose?.Vaccine?.Name ?? $"Dose {scheduleDTO.DoseId}";
                _db.PaActivityLogs.Add(new PaActivityLog
                {
                    PaId = scheduleDTO.PaId.Value,
                    DoctorId = scheduleDTO.DoctorId,
                    ClinicId = null,
                    PatientId = scheduleDTO.ChildId,
                    ActionCode = "SCHEDULE_ADD_VACCINE",
                    Description = $"Added {addedDoseName} to schedule for patient {scheduleDTO.ChildId}",
                    Notes = "",
                    IsReversal = false,
                    ActionDate = DateTime.UtcNow
                });
            }

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

                // CDC 4-day grace outcome for this give (set in the MinGap check below, carried
                // onto the success Response so the client can show the informational note).
                bool graceApplied = false;
                string? graceMessage = null;

                // BUG-16 — a give must carry a real GivenDate. A missing/default value
                // (0001-01-01) or a date on/before the child's DOB is invalid and would slip
                // past the age/future guards below. Reject it before any other give-time check.
                if (scheduleDTO.IsDone == true)
                {
                    if (scheduleDTO.GivenDate.Date <= dbSchedule.Child.DOB.Date)
                        return new Response<ScheduleDTO>(false,
                            "The given date is invalid — it must be after the child's date of birth.", null);
                }

                if (scheduleDTO.IsDone == true && scheduleDTO.BrandId.HasValue)
                {
                    var brand = _db.Brands.FirstOrDefault(b => b.Id == scheduleDTO.BrandId.Value);
                    if (brand != null && brand.MinAge.HasValue)
                    {
                        var givenDate = scheduleDTO.GivenDate.Date;
                        var minAllowedDate = calculateDate(dbSchedule.Child.DOB, brand.MinAge.Value).Date;
                        if (givenDate < minAllowedDate)
                            return new Response<ScheduleDTO>(false,
                                brand.Name + " cannot be given before " + minAllowedDate.ToString("dd-MM-yyyy") + ".", null);
                    }
                }

                // Step 2 — Dose.MinAge check at give-time
                if (scheduleDTO.IsDone == true && !scheduleDTO.IgnoreMinAgeAtGiveTime)
                {
                    var dose = dbSchedule.Dose;
                    if (dose != null && dose.MinAge > 0)
                    {
                        var minAgeDate = calculateDate(dbSchedule.Child.DOB, dose.MinAge).Date;
                        if (scheduleDTO.GivenDate.Date < minAgeDate)
                        {
                            var doseName = dose.Name ?? "This dose";
                            if (scheduleDTO.PaId.HasValue)
                                return new Response<ScheduleDTO>(false,
                                    doseName + " cannot be given before " + minAgeDate.ToString("dd-MM-yyyy") + " (minimum age not reached).", null);
                            else
                                return Response<ScheduleDTO>.Warning(
                                    doseName + " minimum age is not reached — earliest allowed date is " + minAgeDate.ToString("dd-MM-yyyy") + ". Override?");
                        }
                    }
                }

                // Step 2b — Dose.MaxAge check at give-time
                if (scheduleDTO.IsDone == true && !scheduleDTO.IgnoreMaxAgeAtGiveTime)
                {
                    var dose = dbSchedule.Dose;
                    if (dose != null && dose.MaxAge.HasValue)
                    {
                        var maxAgeDate = calculateDate(dbSchedule.Child.DOB, dose.MaxAge.Value).Date;
                        if (scheduleDTO.GivenDate.Date > maxAgeDate)
                        {
                            var doseName = dose.Name ?? "This dose";
                            if (scheduleDTO.PaId.HasValue)
                                return new Response<ScheduleDTO>(false,
                                    doseName + " cannot be given after " + maxAgeDate.ToString("dd-MM-yyyy") + " (maximum age exceeded).", null);
                            else
                                return Response<ScheduleDTO>.Warning(
                                    doseName + " maximum age is exceeded — latest allowed date was " + maxAgeDate.ToString("dd-MM-yyyy") + ". Override?");
                        }
                    }
                }

                // Step 3 — MinGap check at give-time
                if (scheduleDTO.IsDone == true && (dbSchedule.Dose.DoseOrder ?? 0) > 1 && !scheduleDTO.IgnoreMinGapAtGiveTime)
                {
                    var dose = dbSchedule.Dose;
                    if (dose != null && dose.MinGap.HasValue)
                    {
                        var prevDoseForGap = _db.Doses
                            .FirstOrDefault(x => x.VaccineId == dose.VaccineId && x.DoseOrder == (dose.DoseOrder - 1));
                        if (prevDoseForGap != null)
                        {
                            var prevScheduleForGap = _db.Schedules
                                .FirstOrDefault(x => x.ChildId == dbSchedule.ChildId && x.DoseId == prevDoseForGap.Id);
                            var doseName = dose.Name ?? "This dose";
                            // BUG-9 — the previous dose must be recorded as given first. This is a
                            // hard block for everyone (no override): a later dose cannot be given
                            // while its predecessor is unrecorded.
                            if (prevScheduleForGap == null || !prevScheduleForGap.IsDone || !prevScheduleForGap.GivenDate.HasValue)
                            {
                                return new Response<ScheduleDTO>(false,
                                    doseName + " cannot be given until the previous dose of this vaccine is recorded as given.", null);
                            }

                            var minGapDate = calculateDate(prevScheduleForGap.GivenDate.Value.Date, dose.MinGap.Value).Date;
                            // CDC 4-day grace: unless this vaccine requires exact intervals
                            // (cholera/rabies), a give within 4 days before the floor is valid.
                            var exactInterval = dose.Vaccine != null && dose.Vaccine.ExactIntervalRequired;
                            var enforcedGapDate = exactInterval ? minGapDate : minGapDate.AddDays(-CdcGraceDays);
                            if (scheduleDTO.GivenDate.Date < enforcedGapDate)
                            {
                                if (scheduleDTO.PaId.HasValue)
                                    return new Response<ScheduleDTO>(false,
                                        doseName + " cannot be given before " + minGapDate.ToString("dd-MM-yyyy") + " (minimum gap from previous dose not met).", null);
                                else
                                    return Response<ScheduleDTO>.Warning(
                                        doseName + " minimum gap is not met — earliest allowed date is " + minGapDate.ToString("dd-MM-yyyy") + ". Override?");
                            }
                            // Accepted, but inside the grace window (1–4 days early) → flag it so
                            // the client shows the CDC note. Not set for exact-interval vaccines.
                            else if (!exactInterval && scheduleDTO.GivenDate.Date < minGapDate)
                            {
                                graceApplied = true;
                                graceMessage = doseName + " was given " + (minGapDate - scheduleDTO.GivenDate.Date).Days
                                    + " day(s) before the " + minGapDate.ToString("dd-MM-yyyy")
                                    + " minimum interval — accepted as valid under the CDC 4-day grace period.";
                            }
                        }
                    }
                }

                var previousBrandId = dbSchedule.BrandId;
                // Capture prior IsDone before any mutation — this is the reliable signal for
                // whether inventory was actually deducted for this schedule, since BrandId can
                // be persisted on a not-yet-given schedule by earlier partial saves.
                var wasGiven = dbSchedule.IsDone;
                // v2: the exact Stock batch the give consumed (from AdministerSync). Drives
                // Schedule.StockId and the certificate lot/expiry. null = OHF / non-consuming /
                // give-at-zero → lot/expiry stay blank (no fabricated fallback, §6.3).
                int? giveConsumedStockId = null;
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
                    // v2 §6.5a: ungiving a PRE-RESET historical dose (given before the clinic's
                    // StockPeriodStart) is doctor-only and never returns stock. A PA is blocked
                    // outright; the doctor is allowed (the frontend shows the "history only" warning
                    // and only reaches here on confirm). Restore is 0 either way — the give's
                    // ledger row is consumesStock=false (PRE_PERIOD), so UnadministerSync mirrors it.
                    if (dbSchedule.IsDone == true && IsPreResetDose(dbSchedule))
                    {
                        // PA: blocked outright.
                        if (scheduleDTO.PaId.HasValue)
                            return new Response<ScheduleDTO>(false,
                                "This is a historical dose from before the current stock period. Ask the doctor to undo it.", null);

                        // Doctor: warn once (no stock is returned; this removes a real vaccination
                        // from the child's history). Client re-submits with ConfirmPreResetUngive=true.
                        if (!scheduleDTO.ConfirmPreResetUngive)
                            return Response<ScheduleDTO>.Warning(
                                "This dose was given before the current stock period. Undoing it returns no stock and removes a real vaccination from this child's history. Undo anyway?");
                    }

                    // PA-only: own actions today only
                    if (scheduleDTO.PaId.HasValue)
                    {
                        var today = DateTime.UtcNow.AddHours(5).Date;

                        if (dbSchedule.IsDone == true)
                        {
                            if (dbSchedule.GivenByPaId != scheduleDTO.PaId)
                                return new Response<ScheduleDTO>(false, "You can only undo your own actions.", null);
                            if (!dbSchedule.DoneAt.HasValue || dbSchedule.DoneAt.Value.AddHours(5).Date != today)
                                return new Response<ScheduleDTO>(false, "You can only undo actions performed today.", null);
                        }

                        if (dbSchedule.IsSkip == true && scheduleDTO.IsSkip == false)
                        {
                            if (dbSchedule.SkippedByPaId != scheduleDTO.PaId)
                                return new Response<ScheduleDTO>(false, "You can only undo your own actions.", null);
                            if (!dbSchedule.SkippedAt.HasValue || dbSchedule.SkippedAt.Value.AddHours(5).Date != today)
                                return new Response<ScheduleDTO>(false, "You can only undo actions performed today.", null);
                        }
                    }

                    // PA ungive counter: max 2 per dose
                    if (scheduleDTO.PaId.HasValue && dbSchedule.IsDone == true)
                    {
                        if (dbSchedule.UngiveCount >= 2)
                            return new Response<ScheduleDTO>(false, "This vaccine has already been ungiven twice. Contact the doctor.", null);
                        dbSchedule.UngiveCount++;
                    }

                    // PA unskip counter: max 2 per dose
                    if (scheduleDTO.PaId.HasValue && scheduleDTO.IsSkip == false && dbSchedule.IsSkip == true)
                    {
                        if (dbSchedule.UnskipCount >= 2)
                            return new Response<ScheduleDTO>(false, "This vaccine has already been unskipped twice. Contact the doctor.", null);
                        dbSchedule.UnskipCount++;
                    }

                    // PA skip counter: max 2 per dose (when ungive also sets IsSkip=true)
                    if (scheduleDTO.PaId.HasValue && scheduleDTO.IsSkip == true && dbSchedule.IsSkip != true)
                    {
                        if (dbSchedule.SkipCount >= 2)
                            return new Response<ScheduleDTO>(false, "This vaccine has already been skipped twice. Contact the doctor.", null);
                        dbSchedule.SkipCount++;
                    }

                    if (inventoryEnabled && wasGiven && previousBrandId.HasValue)
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

                    if (inventoryEnabled && wasGiven && previousBrandId.HasValue && dbBrandInventory2 == null)
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
                    dbSchedule.GivenByPaId = null;
                    dbSchedule.PaymentMode = "Cash";
                    dbSchedule.OnlineService = null;
                    dbSchedule.IsPaymentApproved = false;
                    dbSchedule.BrandId = null;
                    dbSchedule.Amount = null;
                    dbSchedule.VaccineCost = null;
                    dbSchedule.IsSkip = scheduleDTO.IsSkip;
                    if (scheduleDTO.IsSkip == true)
                    {
                        dbSchedule.SkippedByPaId = scheduleDTO.PaId;
                        dbSchedule.SkippedAt = scheduleDTO.PaId.HasValue ? DateTime.UtcNow : (DateTime?)null;
                    }
                    else
                    {
                        dbSchedule.SkippedByPaId = null;
                        dbSchedule.SkippedAt = null;
                    }

                    ScheduleDTO newData2 = _mapper.Map<ScheduleDTO>(dbSchedule);
                    if (inventoryEnabled && dbBrandInventory2 != null)
                    {
                        if (wasGiven && previousBrandId.HasValue)
                        {
                            _inventory.UnadministerSync(dbBrandInventory2.DoctorId, rollbackClinicId,
                                previousBrandId.Value, dbSchedule.Id, scheduleDTO.GivenDate, scheduleDTO.PaId);
                        }
                    }
                    using (var tx = _db.Database.BeginTransaction())
                    {
                        try
                        {
                            _db.SaveChanges();
                            tx.Commit();
                        }
                        catch (DbUpdateConcurrencyException)
                        {
                            tx.Rollback();
                            return new Response<ScheduleDTO>(false, "Inventory was updated by another action just now. Please retry.", null);
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                    return new Response<ScheduleDTO>(true, "Congratulations", newData2);
                }

                if (!wasGiven)
                {
                    // v2 date policy (§8): a dose can never be marked given with a FUTURE date,
                    // on any path. Reject before any inventory/IsDone work.
                    if (scheduleDTO.GivenDate.Date > ClinicClock.TodayPkt())
                    {
                        return new Response<ScheduleDTO>(false,
                            "The given date cannot be in the future.", null);
                    }

                    // v2 deduction-decision model (§6.2a). Resolves whether this give consumes
                    // stock and why. For a backdated, in-period, brand give the frontend must have
                    // supplied ReRecordHistorical (the "from our stock / just recording" answer).
                    var stockPeriodStart = _db.Clinics
                        .Where(c => c.Id == onlineClinicId).Select(c => c.StockPeriodStart).FirstOrDefault();
                    var decision = InventoryTransactionService.ResolveGiveDecision(
                        scheduleDTO.BrandId, scheduleDTO.GivenDate, stockPeriodStart, scheduleDTO.ReRecordHistorical);

                    if (decision.NeedsPrompt)
                    {
                        // Backdated in-period brand give with no answer — the client should have
                        // prompted. Reject rather than silently deduct or silently skip.
                        return new Response<ScheduleDTO>(false,
                            "This dose is backdated. Choose whether it came from your stock before saving.", null);
                    }

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

                        // v2: a give never hard-blocks on zero stock — a physically-given dose is
                        // always recordable (§2.8 exception). AdministerSync handles the give-at-zero
                        // case (records, floors Count at 0, flags NeedsReconcile). The old
                        // Count<=0 rejection is removed for the consuming path.
                        _inventory.AdministerSync(dbBrandInventory, onlineClinicId, dbSchedule.Id,
                            scheduleDTO.GivenDate, scheduleDTO.PaId,
                            decision.ConsumesStock, decision.Reason, out giveConsumedStockId);

                        // Persist the inventory deduction in its own transaction, right here,
                        // rather than deferring to whichever SaveChanges() this method happens to
                        // hit later (there are two further down, on different branches). Same
                        // narrow-transaction pattern used across the stock controllers.
                        using (var tx = _db.Database.BeginTransaction())
                        {
                            try
                            {
                                _db.SaveChanges();
                                tx.Commit();
                            }
                            catch (DbUpdateConcurrencyException)
                            {
                                tx.Rollback();
                                return new Response<ScheduleDTO>(false, "Inventory was updated by another action just now. Please retry.", null);
                            }
                            catch
                            {
                                tx.Rollback();
                                throw;
                            }
                        }
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
                                childschedule.Date = calculateDate(scheduleDTO.GivenDate.Date, dose.MinGap ?? 30);
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
                        // Site: nurse-chosen at give-time (not derived from Brand like Route). Validated
                        // against the brand's route — forced-single routes coerce, invalid ones drop.
                        dbSchedule.Site = NormalizeSiteForRoute(BrandRoute(scheduleDTO.BrandId), scheduleDTO.Site);
                        dbSchedule.Weight = scheduleDTO.Weight;
                        dbSchedule.Height = scheduleDTO.Height;
                        dbSchedule.Circle = scheduleDTO.Circle;
                        dbSchedule.IsDone = scheduleDTO.IsDone;
                        dbSchedule.GivenDate = scheduleDTO.GivenDate;
                        dbSchedule.DoneAt = scheduleDTO.IsDone ? DateTime.UtcNow : (DateTime?)null;
                        if (scheduleDTO.PaymentMode != null) dbSchedule.PaymentMode = scheduleDTO.PaymentMode;
                        dbSchedule.OnlineService = scheduleDTO.OnlineService;
                        dbSchedule.IsPaymentApproved = false;
                        dbSchedule.DiseaseYear = scheduleDTO.DiseaseYear;
                        dbSchedule.IsDisease = scheduleDTO.IsDisease;
                        var stockClinicId = ResolveClinicIdForStock(scheduleDTO.DoctorId, dbSchedule.Child?.ClinicId ?? 0);
                        var stockPeriodStartForStamp = _db.Clinics
                            .Where(c => c.Id == onlineClinicId).Select(c => c.StockPeriodStart).FirstOrDefault();
                        var stampDecision = InventoryTransactionService.ResolveGiveDecision(
                            scheduleDTO.BrandId, scheduleDTO.GivenDate, stockPeriodStartForStamp, scheduleDTO.ReRecordHistorical);
                        // v2 lot/expiry stamping:
                        //  - consuming brand give that drew a batch → stamp that exact batch.
                        //  - consuming brand give at ZERO stock (no batch) → blank (no fabrication).
                        //  - non-consuming give (OHF / historical) → keep operator-typed lot if any.
                        bool blankIfNoBatch = stampDecision.ConsumesStock
                            && scheduleDTO.BrandId.HasValue && scheduleDTO.BrandId.Value > 0;
                        dbSchedule.StockId = giveConsumedStockId;
                        ApplyStockSourceFields(dbSchedule, scheduleDTO, stockClinicId,
                            giveConsumedStockId, blankIfNoBatch);
                        dbSchedule.Validity = scheduleDTO.Validity;

                        ScheduleDTO newData1 = _mapper.Map<ScheduleDTO>(dbSchedule);
                        _db.SaveChanges();
                        return new Response<ScheduleDTO>(true, "congratulations", newData1)
                        { GraceApplied = graceApplied, GraceMessage = graceMessage };
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
                        // Match the trade name case-insensitively so the DB collation flip to
                        // utf8mb4_bin (case-sensitive) can't silently break the dose-2 skip rule
                        // when a brand is stored as "Menactra"/"Nimenrix" rather than upper-case.
                        string brandName = (doseBrand.Name ?? "").Trim();
                        if (daysDifference > 729 && brandName.Equals("MENACTRA", StringComparison.OrdinalIgnoreCase))
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
                        else if (daysDifference > 364 && brandName.Equals("NIMENRIX", StringComparison.OrdinalIgnoreCase))
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

                // PA give counter: max 2 per dose
                if (scheduleDTO.PaId.HasValue && scheduleDTO.IsDone == true)
                {
                    if (dbSchedule.GiveCount >= 2)
                        return new Response<ScheduleDTO>(false, "This vaccine has already been given twice. Contact the doctor.", null);
                    dbSchedule.GiveCount++;
                }

                // PA skip counter (standalone skip, not from ungive path): max 2 per dose
                if (scheduleDTO.PaId.HasValue && scheduleDTO.IsSkip == true && dbSchedule.IsDone == false && dbSchedule.IsSkip != true)
                {
                    if (dbSchedule.SkipCount >= 2)
                        return new Response<ScheduleDTO>(false, "This vaccine has already been skipped twice. Contact the doctor.", null);
                    dbSchedule.SkipCount++;
                }

                dbSchedule.BrandId = scheduleDTO.BrandId;
                dbSchedule.Weight = scheduleDTO.Weight;
                dbSchedule.Height = scheduleDTO.Height;
                dbSchedule.Circle = scheduleDTO.Circle;

                // Ungive-after-download: create an InvoiceAmendment so it appears on the
                // doctor's Payment Reconciliation page. PA payable stays unchanged until doctor acts.
                // Doctor APPROVE → payable drops to 0. Doctor REJECT → PA still owes full amount.
                if (scheduleDTO.IsDone == false && dbSchedule.IsDone == true && scheduleDTO.PaId.HasValue)
                {
                    var invoiceDateMin2 = DateTime.UtcNow.Date.AddDays(-1);
                    var invoiceDateMax2 = DateTime.UtcNow.Date.AddDays(1);
                    var invSub = _db.InvoiceSubmissions.FirstOrDefault(x =>
                        x.ChildId == dbSchedule.ChildId &&
                        x.DoctorId == scheduleDTO.DoctorId &&
                        x.InvoiceDate.Date >= invoiceDateMin2 &&
                        x.InvoiceDate.Date <= invoiceDateMax2 &&
                        x.TotalAmount > 0);

                    if (invSub != null && !invSub.HasPendingAmendment)
                    {
                        _db.InvoiceAmendments.Add(new InvoiceAmendment
                        {
                            InvoiceSubmissionId = invSub.Id,
                            AmendmentType = "Ungive",
                            OldAmount = invSub.TotalAmount,
                            NewAmount = 0,
                            PaId = scheduleDTO.PaId.Value,
                            DoctorId = scheduleDTO.DoctorId,
                            Notes = $"PA ungave vaccine after invoice was downloaded. ScheduleId: {dbSchedule.Id}. Payment collected: {dbSchedule.IsPaymentCollected}",
                            CreatedAt = DateTime.UtcNow
                        });
                        invSub.InvoiceStatus = "UngiveReversal";
                        invSub.HasPendingAmendment = true;
                        _db.Entry(invSub).State = EntityState.Modified;
                    }
                }

                // Void the Invoice row immediately on ungive so the QR code on any downloaded PDF
                // shows "INVOICE CANCELLED" rather than the original valid invoice.
                if (scheduleDTO.IsDone == false && dbSchedule.IsDone == true)
                {
                    var invoiceToVoid = _db.Invoices
                        .FirstOrDefault(i => i.DoseId == dbSchedule.DoseId
                                          && i.ChildId == dbSchedule.ChildId
                                          && i.DoctorId == scheduleDTO.DoctorId
                                          && i.IsVoided == false);
                    if (invoiceToVoid != null)
                    {
                        invoiceToVoid.IsVoided = true;
                        invoiceToVoid.SupersededBy = "UNGIVEN";
                        _db.Entry(invoiceToVoid).State = EntityState.Modified;
                    }
                }

                dbSchedule.IsDone = scheduleDTO.IsDone;
                dbSchedule.GivenDate = scheduleDTO.GivenDate;
                dbSchedule.DoneAt = scheduleDTO.IsDone ? DateTime.UtcNow : (DateTime?)null;
                dbSchedule.GivenByPaId = scheduleDTO.IsDone ? scheduleDTO.PaId : null;
                if (scheduleDTO.IsDone && scheduleDTO.PaId.HasValue)
                    dbSchedule.PaymentCollectorPaId = scheduleDTO.PaId;
                else if (scheduleDTO.IsDone)
                    dbSchedule.PaymentCollectorPaId = GetActivePaIdForChild(dbSchedule.ChildId);
                else
                    dbSchedule.PaymentCollectorPaId = null;
                if (scheduleDTO.PaymentMode != null) dbSchedule.PaymentMode = scheduleDTO.PaymentMode;
                dbSchedule.OnlineService = scheduleDTO.OnlineService;
                dbSchedule.IsPaymentApproved = false;
                dbSchedule.DiseaseYear = scheduleDTO.DiseaseYear;
                dbSchedule.IsDisease = scheduleDTO.IsDisease;
                var onlineStockClinicId = ResolveClinicIdForStock(scheduleDTO.DoctorId, dbSchedule.Child?.ClinicId ?? 0);
                ApplyStockSourceFields(dbSchedule, scheduleDTO, onlineStockClinicId);
                dbSchedule.Validity = scheduleDTO.Validity;
                dbSchedule.IsPAApprove = scheduleDTO.IsPAApprove;
                ChangeDueDatesOfInjectedSchedule(scheduleDTO, dbSchedule);
                ScheduleDTO newData = _mapper.Map<ScheduleDTO>(dbSchedule);
                // Auto-create assignment when PA gives a vaccine with no prior assignment today,
                // and pin this dose to it — covers both "first dose under a brand new
                // assignment" and "extra dose given mid-visit, not in the original pinned set."
                if (scheduleDTO.IsDone && scheduleDTO.PaId.HasValue && scheduleDTO.DoctorId > 0)
                {
                    var assignmentId = EnsurePAAssignment(dbSchedule.ChildId, scheduleDTO.PaId.Value, scheduleDTO.DoctorId, dbSchedule.Child != null ? (long?)dbSchedule.Child.ClinicId : null);
                    LinkScheduleToAssignment(assignmentId, dbSchedule.Id);
                }
                _db.SaveChanges();
                return new Response<ScheduleDTO>(true, "congratulations", newData)
                { GraceApplied = graceApplied, GraceMessage = graceMessage };
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

            // Same-day-only restriction (PKT = UTC+5): vitals can only be edited via after-fill
            // on the same calendar day the dose was given. DoneAt is the authoritative UTC
            // timestamp set when IsDone was marked; fall back to GivenDate if DoneAt is missing.
            var today = DateTime.UtcNow.AddHours(5).Date;
            DateTime? visitDate = dbSchedule.DoneAt.HasValue
                ? dbSchedule.DoneAt.Value.AddHours(5).Date
                : dbSchedule.GivenDate?.Date;

            if (!dbSchedule.IsDone || !visitDate.HasValue || visitDate.Value != today)
            {
                return new Response<ScheduleDTO>(false, "This visit was not completed today. Vitals can only be updated on the same day as the visit.", null);
            }

            dbSchedule.Weight = scheduleDTO.Weight;
            dbSchedule.Height = scheduleDTO.Height;
            dbSchedule.Circle = scheduleDTO.Circle;

            _db.SaveChanges();

            UpsertFollowUpVitalsForToday(dbSchedule, scheduleDTO, today);

            return new Response<ScheduleDTO>(true, "Schedule updated successfully", _mapper.Map<ScheduleDTO>(dbSchedule));
        }

        // Propagates after-fill vitals into today's auto-vaccine FollowUp row (Disease == "Vaccination"),
        // created by autoCreateFollowUp/autoCreateFollowUpForBulk in VacDoc. After-fill is same-day-only,
        // so (ChildId, CurrentVisitDate.Date == today) reliably identifies the matching row. No-op if
        // no such row exists.
        private void UpsertFollowUpVitalsForToday(Schedule dbSchedule, ScheduleDTO scheduleDTO, DateTime today)
        {
            FollowUp? existing = _db.FollowUps
                .Where(f => f.ChildId == dbSchedule.ChildId
                         && f.CurrentVisitDate.HasValue
                         && f.CurrentVisitDate.Value.Date == today
                         && f.Disease == "Vaccination")
                .OrderByDescending(f => f.Id)
                .FirstOrDefault();

            if (existing == null)
            {
                return;
            }

            if (scheduleDTO.Weight.HasValue) existing.Weight = scheduleDTO.Weight;
            if (scheduleDTO.Height.HasValue) existing.Height = scheduleDTO.Height;
            if (scheduleDTO.Circle.HasValue) existing.OFC = scheduleDTO.Circle;

            _db.SaveChanges();
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
                .Where(s => s.BrandId == brandId.Value
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)));

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

        // Returns the active assignment's Id (creating one if needed), or 0 if no
        // assignment exists/could be created. Flushes immediately on create so the
        // returned Id is usable right away by LinkScheduleToAssignment, ahead of the
        // caller's own later SaveChanges()/transaction commit.
        private long EnsurePAAssignment(long childId, long paId, long doctorId, long? clinicId)
        {
            // Validate doctorId is a real doctor — if caller passed a PA ID by mistake, resolve from child's clinic
            var isRealDoctor = _db.Doctors.Any(d => d.Id == doctorId);
            if (!isRealDoctor)
            {
                doctorId = _db.Clinics
                    .Where(c => c.Id == (clinicId ?? 0))
                    .Select(c => c.DoctorId)
                    .FirstOrDefault();
                if (doctorId <= 0) return 0;
            }

            var today = DateTime.UtcNow.AddHours(5).Date; // PKT = UTC+5
            var existing = _db.PAAssignments
                .Where(a => a.ChildId == childId &&
                            a.PersonalAssistantId == paId &&
                            a.AssignedAt >= today && a.AssignedAt < today.AddDays(1) &&
                            !a.IsCancelled)
                .Select(a => a.Id)
                .FirstOrDefault();
            if (existing > 0) return existing;

            var newAssignment = new PAAssignment
            {
                ChildId = childId,
                PersonalAssistantId = paId,
                DoctorId = doctorId,
                ClinicId = clinicId,
                AssignedAt = DateTime.UtcNow,
                IsCompleted = false,
                IsAutoCreated = true
            };
            _db.PAAssignments.Add(newAssignment);
            _db.SaveChanges();
            return newAssignment.Id;
        }

        // Appends a PAAssignmentSchedule row linking scheduleId to assignmentId, if not
        // already linked. No-op if assignmentId is 0 (no assignment exists/was created).
        private void LinkScheduleToAssignment(long assignmentId, long scheduleId)
        {
            if (assignmentId <= 0) return;
            var alreadyLinked = _db.PAAssignmentSchedules
                .Any(l => l.AssignmentId == assignmentId && l.ScheduleId == scheduleId);
            if (!alreadyLinked)
                _db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = assignmentId, ScheduleId = scheduleId });
        }

        // v2 §6.5a: a dose is "pre-reset" if it was given before its clinic's StockPeriodStart —
        // frozen historical fact. Ungiving it must never move stock and is doctor-only. Uses the
        // stock clinic the dose was debited against (StockClinicId), falling back to the child's
        // clinic for older rows. If the clinic has no StockPeriodStart set (not cut over), nothing
        // is pre-reset.
        private bool IsPreResetDose(Schedule dbSchedule)
        {
            if (!dbSchedule.GivenDate.HasValue) return false;
            var stockClinicId = dbSchedule.StockClinicId ?? dbSchedule.Child?.ClinicId ?? 0;
            if (stockClinicId <= 0) return false;
            var periodStart = _db.Clinics
                .Where(c => c.Id == stockClinicId).Select(c => c.StockPeriodStart).FirstOrDefault();
            return periodStart.HasValue && dbSchedule.GivenDate.Value.Date < periodStart.Value.Date;
        }

        // Site of administration is constrained by the brand's Route (single source of truth,
        // mirrored on the VacDoc give-UI). Server-authoritative: never trust a client site that is
        // not valid for the route — coerce forced-single routes to their only site, and drop an
        // invalid multi-site value rather than persisting a wrong vaccine→site record.
        private static readonly Dictionary<string, string[]> RouteSites =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Oral",       new[] { "Oral" } },
            { "Intranasal", new[] { "Intranasal" } },
            { "ID",         new[] { "R Arm", "L Arm" } },
            { "IM",         new[] { "R Thigh", "L Thigh", "R Deltoid", "L Deltoid" } },
            { "SC",         new[] { "R Thigh", "L Thigh", "R Deltoid", "L Deltoid" } },
        };

        // Returns the site to persist given a brand's route and the requested (client) site.
        //  - unknown/empty route → keep whatever was requested (can't validate without a route).
        //  - single-site route   → force the only valid site (ignore client).
        //  - multi-site route    → keep the requested site only if valid, else null (no guessing).
        private string? NormalizeSiteForRoute(string? route, string? requestedSite)
        {
            var site = string.IsNullOrWhiteSpace(requestedSite) ? null : requestedSite.Trim();
            if (string.IsNullOrWhiteSpace(route) || !RouteSites.TryGetValue(route.Trim(), out var valid))
                return site;
            if (valid.Length == 1)
                return valid[0];
            return (site != null && valid.Contains(site, StringComparer.OrdinalIgnoreCase)) ? site : null;
        }

        // Route as stored on the brand (for site validation at give-time).
        private string BrandRoute(long? brandId)
        {
            return _db.Brands.Where(b => b.Id == (brandId ?? 0)).Select(b => b.Route).FirstOrDefault() ?? "";
        }

        private void ApplyStockSourceFields(Schedule dbSchedule, ScheduleDTO scheduleDTO, long clinicId,
            int? consumedStockId = null, bool blankIfNoBatch = false)
        {
            // v2: when the give consumed a specific batch, stamp the certificate lot/expiry from
            // THAT exact batch.
            if (consumedStockId.HasValue)
            {
                var consumed = _db.Stocks.FirstOrDefault(s => s.Id == consumedStockId.Value);
                dbSchedule.Manufacturer = _db.Brands
                    .Where(b => b.Id == (scheduleDTO.BrandId ?? 0)).Select(b => b.Manufacturer).FirstOrDefault() ?? "";
                dbSchedule.Route = _db.Brands
                    .Where(b => b.Id == (scheduleDTO.BrandId ?? 0)).Select(b => b.Route).FirstOrDefault() ?? "";
                dbSchedule.StockClinicId = clinicId;
                if (consumed != null)
                {
                    dbSchedule.Lot = consumed.BatchLot ?? "";
                    dbSchedule.Expiry = consumed.Expiry;
                    dbSchedule.VaccineCost = consumed.StockAmount;
                }
                return;
            }

            // v2 give-at-zero: a consuming brand give that drew from NO batch → leave lot/expiry
            // blank rather than falling back to a depleted/latest batch (no fabrication, §6.3).
            // The operator fills the real batch in later via the label-only correction (§6.3a).
            if (blankIfNoBatch)
            {
                dbSchedule.Manufacturer = _db.Brands
                    .Where(b => b.Id == (scheduleDTO.BrandId ?? 0)).Select(b => b.Manufacturer).FirstOrDefault() ?? "";
                dbSchedule.Route = _db.Brands
                    .Where(b => b.Id == (scheduleDTO.BrandId ?? 0)).Select(b => b.Route).FirstOrDefault() ?? "";
                dbSchedule.Lot = "";
                dbSchedule.Expiry = null;
                dbSchedule.StockClinicId = clinicId;
                return;
            }

            ApplyStockSourceFields(dbSchedule, scheduleDTO.BrandId, scheduleDTO.Lot, scheduleDTO.Expiry, clinicId);
        }

        private void ApplyStockSourceFields(Schedule dbSchedule, long? brandId, string? lot, DateTime? expiry, long clinicId,
            int? consumedStockId, bool blankIfNoBatch)
        {
            // v2 give path: stamp from the exact consumed batch, or blank at give-at-zero.
            if (consumedStockId.HasValue)
            {
                var consumed = _db.Stocks.FirstOrDefault(s => s.Id == consumedStockId.Value);
                dbSchedule.Manufacturer = _db.Brands
                    .Where(b => b.Id == (brandId ?? 0)).Select(b => b.Manufacturer).FirstOrDefault() ?? "";
                dbSchedule.Route = _db.Brands
                    .Where(b => b.Id == (brandId ?? 0)).Select(b => b.Route).FirstOrDefault() ?? "";
                dbSchedule.StockClinicId = clinicId;
                if (consumed != null)
                {
                    dbSchedule.Lot = consumed.BatchLot ?? "";
                    dbSchedule.Expiry = consumed.Expiry;
                    dbSchedule.VaccineCost = consumed.StockAmount;
                }
                return;
            }
            if (blankIfNoBatch)
            {
                dbSchedule.Manufacturer = _db.Brands
                    .Where(b => b.Id == (brandId ?? 0)).Select(b => b.Manufacturer).FirstOrDefault() ?? "";
                dbSchedule.Route = _db.Brands
                    .Where(b => b.Id == (brandId ?? 0)).Select(b => b.Route).FirstOrDefault() ?? "";
                dbSchedule.Lot = "";
                dbSchedule.Expiry = null;
                dbSchedule.StockClinicId = clinicId;
                return;
            }
            ApplyStockSourceFields(dbSchedule, brandId, lot, expiry, clinicId);
        }

        private void ApplyStockSourceFields(Schedule dbSchedule, long? brandId, string? lot, DateTime? expiry, long clinicId)
        {
            dbSchedule.Manufacturer = _db.Brands
                .Where(b => b.Id == (brandId ?? 0))
                .Select(b => b.Manufacturer)
                .FirstOrDefault() ?? "";
            // Route: server-authoritative, always derived from the Brand (client value never trusted).
            dbSchedule.Route = _db.Brands
                .Where(b => b.Id == (brandId ?? 0))
                .Select(b => b.Route)
                .FirstOrDefault() ?? "";
            dbSchedule.Lot = "";
            dbSchedule.Expiry = null;

            var selectedLot = string.IsNullOrWhiteSpace(lot) ? null : lot.Trim();
            var selectedExpiry = expiry.HasValue ? expiry.Value.Date : (DateTime?)null;

            Stock? stock = null;

            if (!string.IsNullOrWhiteSpace(selectedLot))
            {
                var selectedStockQuery = _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == (brandId ?? 0)
                        && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
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
                dbSchedule.VaccineCost = stock.StockAmount;
            }
            dbSchedule.StockClinicId = clinicId;
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
                    .Where(s => s.BrandId == schedule.BrandId.Value
                        && (s.ClinicId != null || s.BillId != null));

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

                // v2: clinic is Stock.ClinicId (opening/transfer rows) else Bill.ClinicId (purchase rows).
                var candidateClinicIds = candidateStocks
                    .Select(s => s.ClinicId != null ? s.ClinicId.Value : (s.Bill != null ? s.Bill.ClinicId : 0))
                    .Where(cid => cid != 0)
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

            var row = _db.Clinics
                .Where(c => c.Id == clinicId)
                .Select(c => new { c.Doctor.AllowInventory, c.MaintainInventory })
                .FirstOrDefault();

            if (row == null)
            {
                return true;
            }

            // A clinic maintains stock only when the owning doctor allows inventory AND
            // this clinic has opted in via its per-clinic MaintainInventory switch.
            return row.AllowInventory && row.MaintainInventory;
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
                    // Doctor must allow inventory AND the clinic in play must have opted in.
                    // The clinic-level check ANDs the doctor flag with MaintainInventory, so
                    // defer to it for the resolved clinic rather than short-circuiting here.
                    return doctorAllowInventory.Value && IsInventoryEnabledForClinic(clinicId);
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
                if (!d.MinGap.HasValue)
                {
                    var skipSchedule = _db.Schedules
                        .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                        .FirstOrDefault();
                    if (skipSchedule != null)
                        previousdosedate = skipSchedule.Date.Date;
                    continue;
                }

                var minimumGap = d.MinGap.Value;

                var TargetSchedule = _db.Schedules
                    .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                    .FirstOrDefault();
                if (TargetSchedule != null)
                {
                    // BUG-6: never rewrite the date of a dose that was already given. An
                    // administered dose's date is history, not a plan. Skip it, and anchor the
                    // next dose's gap off its real GivenDate (consistent with the give-time
                    // MinGap check, which measures from the previous dose's GivenDate).
                    if (TargetSchedule.IsDone)
                    {
                        previousdosedate = (TargetSchedule.GivenDate ?? TargetSchedule.Date).Date;
                        continue;
                    }

                    // BUG-2: compare DATES, not a raw day-count against a coded MinGap
                    // (406 = 6 months, not 406 days). Decode the floor first, then move the
                    // dose forward ONLY if it is genuinely earlier than that floor, and only
                    // as far as the floor. An already-valid (wider) gap is left untouched.
                    var minGapFloor = calculateDate(previousdosedate, minimumGap).Date;
                    if (TargetSchedule.Date.Date < minGapFloor)
                    {
                        TargetSchedule.Date = minGapFloor;
                    }
                    previousdosedate = TargetSchedule.Date.Date;
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
            var dtoList = dsDTOS.ToList();

            // PA permission check — read PaId from first item (all items share the same caller)
            var firstPaId = dtoList.FirstOrDefault(x => x.PaId.HasValue)?.PaId;
            if (firstPaId.HasValue)
            {
                var paPerm = _db.PaPermissions.FirstOrDefault(p => p.PaId == firstPaId.Value);
                if (paPerm == null || !paPerm.AddSpecialDoses)
                    return new Response<IEnumerable<ScheduleDTO>>(false, "You do not have permission to add vaccines to the schedule.", null);
            }

            foreach (var scheduleDTO in dtoList)
            {
                if (String.IsNullOrEmpty(scheduleDTO.DiseaseYear))
                    scheduleDTO.DiseaseYear = "";

                var dbChild = _db.Childs.FirstOrDefault(x => x.Id == scheduleDTO.ChildId);
                var dbDose = _db.Doses.Include(x => x.Vaccine).FirstOrDefault(x => x.Id == scheduleDTO.DoseId);
                scheduleDTO.Date = calculateDate(dbChild.DOB, dbDose.MinAge);
                if (string.IsNullOrEmpty(scheduleDTO.Expiry?.ToString()))
                {
                    scheduleDTO.Expiry = null;
                }
                Schedule scheduleDB = _mapper.Map<Schedule>(scheduleDTO);
                _db.Schedules.Add(scheduleDB);

                if (scheduleDTO.PaId.HasValue)
                {
                    var doseName = dbDose?.Name ?? dbDose?.Vaccine?.Name ?? $"Dose {scheduleDTO.DoseId}";
                    _db.PaActivityLogs.Add(new PaActivityLog
                    {
                        PaId = scheduleDTO.PaId.Value,
                        DoctorId = scheduleDTO.DoctorId,
                        ClinicId = null,
                        PatientId = scheduleDTO.ChildId,
                        ActionCode = "SCHEDULE_ADD_VACCINE",
                        Description = $"Added {doseName} to schedule for patient {scheduleDTO.ChildId}",
                        Notes = "",
                        IsReversal = false,
                        ActionDate = DateTime.UtcNow
                    });
                }

                _db.SaveChanges();
                scheduleDTO.Id = scheduleDB.Id;
            }
            return new Response<IEnumerable<ScheduleDTO>>(true, null, dtoList);
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
                    .ThenInclude(x => x.Vaccine)
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
                    scheduleDTO.GivenDate = schedule.GivenDate ?? default;
                    scheduleDTO.InvoiceDate = schedule.GivenDate;
                    scheduleDTO.IsDone = schedule.IsDone;
                    scheduleDTO.Validity = schedule.Validity ?? schedule.Dose.Vaccine.Validity;
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
            [FromQuery] bool ignoreMinGapFromPreviousDose = false,
            [FromQuery] bool isParent = false
        )
        {
            if (isParent)
            {
                ignoreMaxAgeRule = false;
                ignoreMinAgeFromDOB = false;
                ignoreMinGapFromPreviousDose = false;
            }
            // Step 5 — PA cannot bypass ignore flags on reschedule
            if (scheduleDTO.PaId.HasValue)
            {
                ignoreMaxAgeRule = false;
                ignoreMinAgeFromDOB = false;
                ignoreMinGapFromPreviousDose = false;
            }
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
                    return new Response<ScheduleDTO>(false, message, null)
                    { RuleCode = RuleCodeForRescheduleMessage(message) };
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

            // PA bulk counter + ownership pre-check: verify before mutating any row
            if (scheduleDTO.PaId.HasValue)
            {
                var today = DateTime.UtcNow.AddHours(5).Date;
                foreach (var checkSchedule in dbChildSchedules)
                {
                    if (scheduleDTO.IsDone == false && checkSchedule.IsDone == true)
                    {
                        if (checkSchedule.UngiveCount >= 2)
                            return new Response<ScheduleDTO>(false, "One or more vaccines have already been ungiven twice. Contact the doctor.", null);
                        if (checkSchedule.GivenByPaId != scheduleDTO.PaId)
                            return new Response<ScheduleDTO>(false, "You can only undo your own actions.", null);
                        if (!checkSchedule.DoneAt.HasValue || checkSchedule.DoneAt.Value.AddHours(5).Date != today)
                            return new Response<ScheduleDTO>(false, "You can only undo actions performed today.", null);
                    }
                    if (scheduleDTO.IsDone == true && checkSchedule.IsDone == false && checkSchedule.GiveCount >= 2)
                        return new Response<ScheduleDTO>(false, "One or more vaccines have already been given twice. Contact the doctor.", null);
                }
            }

            // Step 4 — MinAge, MaxAge, Brand.MinAge and MinGap checks for bulk give (accumulate all errors)
            bool bulkGraceApplied = false;
            var bulkGraceMessages = new System.Collections.Generic.List<string>();
            if (scheduleDTO.IsDone == true)
            {
                var bulkErrors = new System.Collections.Generic.List<string>();
                foreach (var chk in dbChildSchedules)
                {
                    var chkDose = _db.Doses.FirstOrDefault(x => x.Id == chk.DoseId);
                    if (chkDose == null) continue;

                    // Dose.MinAge
                    if (!scheduleDTO.IgnoreMinAgeAtGiveTime && chkDose.MinAge > 0)
                    {
                        var child = _db.Childs.FirstOrDefault(x => x.Id == chk.ChildId);
                        if (child != null)
                        {
                            var minAgeDate = calculateDate(child.DOB, chkDose.MinAge).Date;
                            if (scheduleDTO.GivenDate.Date < minAgeDate)
                            {
                                var msg = (chkDose.Name ?? "A dose") + " cannot be given before " + minAgeDate.ToString("dd-MM-yyyy") + " (minimum age not reached).";
                                if (scheduleDTO.PaId.HasValue)
                                    bulkErrors.Add(msg);
                                else if (!bulkErrors.Contains(msg))
                                    bulkErrors.Add("[Warning] " + msg);
                            }
                        }
                    }

                    // Dose.MaxAge — mirror single-give (PA = hard block, doctor = warning)
                    if (!scheduleDTO.IgnoreMaxAgeAtGiveTime && chkDose.MaxAge.HasValue)
                    {
                        var child = _db.Childs.FirstOrDefault(x => x.Id == chk.ChildId);
                        if (child != null)
                        {
                            var maxAgeDate = calculateDate(child.DOB, chkDose.MaxAge.Value).Date;
                            if (scheduleDTO.GivenDate.Date > maxAgeDate)
                            {
                                var msg = (chkDose.Name ?? "A dose") + " cannot be given after " + maxAgeDate.ToString("dd-MM-yyyy") + " (maximum age exceeded).";
                                if (scheduleDTO.PaId.HasValue)
                                    bulkErrors.Add(msg);
                                else if (!bulkErrors.Contains("[Warning] " + msg))
                                    bulkErrors.Add("[Warning] " + msg);
                            }
                        }
                    }

                    // Brand.MinAge — mirror single-give (unconditional hard block for all actors,
                    // no override flag). Resolve this dose's chosen brand from ScheduleBrands
                    // (per-dose in a bulk give), falling back to the top-level BrandId.
                    var chkBrandId = scheduleDTO.ScheduleBrands
                        .Where(x => x.ScheduleId == chk.Id)
                        .Select(x => x.BrandId)
                        .FirstOrDefault() ?? scheduleDTO.BrandId;
                    if (chkBrandId.HasValue && chkBrandId.Value > 0)
                    {
                        var chkBrand = _db.Brands.FirstOrDefault(b => b.Id == chkBrandId.Value);
                        if (chkBrand != null && chkBrand.MinAge.HasValue)
                        {
                            var child = _db.Childs.FirstOrDefault(x => x.Id == chk.ChildId);
                            if (child != null)
                            {
                                var brandMinAgeDate = calculateDate(child.DOB, chkBrand.MinAge.Value).Date;
                                if (scheduleDTO.GivenDate.Date < brandMinAgeDate)
                                {
                                    var msg = chkBrand.Name + " cannot be given before " + brandMinAgeDate.ToString("dd-MM-yyyy") + ".";
                                    if (!bulkErrors.Contains(msg))
                                        bulkErrors.Add(msg);   // no [Warning] prefix → hard block for everyone
                                }
                            }
                        }
                    }

                    // MinGap
                    if (!scheduleDTO.IgnoreMinGapAtGiveTime && (chkDose.DoseOrder ?? 0) > 1 && chkDose.MinGap.HasValue)
                    {
                        var prevDoseChk = _db.Doses
                            .FirstOrDefault(x => x.VaccineId == chkDose.VaccineId && x.DoseOrder == (chkDose.DoseOrder - 1));
                        if (prevDoseChk != null)
                        {
                            var prevSchedChk = _db.Schedules
                                .FirstOrDefault(x => x.ChildId == chk.ChildId && x.DoseId == prevDoseChk.Id);
                            // BUG-9 — previous dose must be recorded as given first (hard block,
                            // no override). Added plain (no [Warning] prefix) so it hard-blocks.
                            if (prevSchedChk == null || !prevSchedChk.IsDone || !prevSchedChk.GivenDate.HasValue)
                            {
                                var blockMsg = (chkDose.Name ?? "A dose") + " cannot be given until the previous dose of this vaccine is recorded as given.";
                                if (!bulkErrors.Contains(blockMsg))
                                    bulkErrors.Add(blockMsg);
                            }
                            else
                            {
                                var minGapDate = calculateDate(prevSchedChk.GivenDate.Value.Date, chkDose.MinGap.Value).Date;
                                // CDC 4-day grace (MinGap only), unless the vaccine requires exact
                                // intervals (cholera/rabies). chkDose is loaded without the Vaccine
                                // nav, so read the flag directly.
                                var exactInterval = _db.Vaccines
                                    .Where(v => v.Id == chkDose.VaccineId)
                                    .Select(v => v.ExactIntervalRequired)
                                    .FirstOrDefault();
                                var enforcedGapDate = exactInterval ? minGapDate : minGapDate.AddDays(-CdcGraceDays);
                                if (scheduleDTO.GivenDate.Date < enforcedGapDate)
                                {
                                    var msg = (chkDose.Name ?? "A dose") + " cannot be given before " + minGapDate.ToString("dd-MM-yyyy") + " (minimum gap not met).";
                                    if (scheduleDTO.PaId.HasValue)
                                        bulkErrors.Add(msg);
                                    else if (!bulkErrors.Contains("[Warning] " + msg))
                                        bulkErrors.Add("[Warning] " + msg);
                                }
                                else if (!exactInterval && scheduleDTO.GivenDate.Date < minGapDate)
                                {
                                    bulkGraceApplied = true;
                                    var gmsg = (chkDose.Name ?? "A dose") + " given " + (minGapDate - scheduleDTO.GivenDate.Date).Days
                                        + " day(s) early — valid under the CDC 4-day grace period.";
                                    if (!bulkGraceMessages.Contains(gmsg))
                                        bulkGraceMessages.Add(gmsg);
                                }
                            }
                        }
                    }
                }
                if (bulkErrors.Count > 0)
                {
                    // Any error NOT tagged [Warning] is a hard block: every PA error (added
                    // plain above) plus Brand.MinAge for any actor. If only [Warning] errors
                    // remain, the doctor gets a soft, overridable warning.
                    bool anyHardBlock = bulkErrors.Any(e => !e.StartsWith("[Warning]"));
                    // Strip the internal [Warning] marker from the user-facing text.
                    var combined = string.Join(" | ",
                        bulkErrors.Select(e => e.StartsWith("[Warning] ") ? e.Substring("[Warning] ".Length) : e));
                    if (anyHardBlock)
                        return new Response<ScheduleDTO>(false, combined, null);
                    else
                        return Response<ScheduleDTO>.Warning(combined);
                }
            }

            // v2 date policy (§8): no give with a FUTURE date, on any path — reject the whole
            // bulk request before any dose is marked done. Only applies when actually giving.
            if (scheduleDTO.IsDone && scheduleDTO.GivenDate.Date > ClinicClock.TodayPkt())
            {
                return new Response<ScheduleDTO>(false, "The given date cannot be in the future.", null);
            }

            // v2 §6.5a: on a bulk UNGIVE, pre-reset historical doses are left untouched and
            // reported — never silently un-recorded in a batch. (A PA can't ungive them at all;
            // even the doctor must undo them one at a time via single ungive with its warning.)
            int preResetSkipped = 0;

            foreach (var schedule in dbChildSchedules)
            {
                // Bulk ungive of a pre-reset given dose → skip this schedule entirely and count it.
                if (scheduleDTO.IsDone == false && schedule.IsDone == true && IsPreResetDose(schedule))
                {
                    preResetSkipped++;
                    continue;
                }

                var wasIsDone = schedule.IsDone;
                schedule.Weight =(scheduleDTO.Weight > 0) ? scheduleDTO.Weight : schedule.Weight;
                schedule.Height =(scheduleDTO.Height > 0) ? scheduleDTO.Height : schedule.Height;
                schedule.Circle =(scheduleDTO.Circle > 0) ? scheduleDTO.Circle : schedule.Circle;
                schedule.IsDone = scheduleDTO.IsDone;
                schedule.GivenDate = scheduleDTO.GivenDate.Date;
                schedule.DoneAt = scheduleDTO.IsDone ? DateTime.UtcNow : (DateTime?)null;
                if (scheduleDTO.PaymentMode != null) schedule.PaymentMode = scheduleDTO.PaymentMode;
                schedule.OnlineService = scheduleDTO.OnlineService;
                schedule.IsPaymentApproved = false;
                schedule.IsPAApprove= scheduleDTO.IsPAApprove;
                // Track which PA gave/ungave this dose; same PA is the payment collector
                if (scheduleDTO.IsDone)
                {
                    schedule.GivenByPaId = scheduleDTO.PaId;
                    schedule.PaymentCollectorPaId = scheduleDTO.PaId.HasValue
                        ? scheduleDTO.PaId
                        : GetActivePaIdForChild(schedule.ChildId);
                }
                else
                {
                    schedule.GivenByPaId = null;
                    schedule.PaymentCollectorPaId = null;
                    schedule.Amount = null;
                }

                // PA audit counters
                if (scheduleDTO.PaId.HasValue)
                {
                    if (scheduleDTO.IsDone == true && wasIsDone == false)
                        schedule.GiveCount++;
                    else if (scheduleDTO.IsDone == false && wasIsDone == true)
                        schedule.UngiveCount++;
                }

                // Ungive-after-download: create an InvoiceAmendment so it appears on the
                // doctor's Payment Reconciliation page. PA payable stays unchanged until doctor acts.
                // Doctor APPROVE → payable drops to 0. Doctor REJECT → PA still owes full amount.
                if (scheduleDTO.IsDone == false && wasIsDone == true && scheduleDTO.PaId.HasValue)
                {
                    var invoiceDateMin3 = DateTime.UtcNow.Date.AddDays(-1);
                    var invoiceDateMax3 = DateTime.UtcNow.Date.AddDays(1);
                    var invSub2 = _db.InvoiceSubmissions.FirstOrDefault(x =>
                        x.ChildId == schedule.ChildId &&
                        x.DoctorId == scheduleDTO.DoctorId &&
                        x.InvoiceDate.Date >= invoiceDateMin3 &&
                        x.InvoiceDate.Date <= invoiceDateMax3 &&
                        x.TotalAmount > 0);

                    if (invSub2 != null && !invSub2.HasPendingAmendment)
                    {
                        _db.InvoiceAmendments.Add(new InvoiceAmendment
                        {
                            InvoiceSubmissionId = invSub2.Id,
                            AmendmentType = "Ungive",
                            OldAmount = invSub2.TotalAmount,
                            NewAmount = 0,
                            PaId = scheduleDTO.PaId.Value,
                            DoctorId = scheduleDTO.DoctorId,
                            Notes = $"PA ungave vaccine after invoice was downloaded. ScheduleId: {schedule.Id}. Payment collected: {schedule.IsPaymentCollected}",
                            CreatedAt = DateTime.UtcNow
                        });
                        invSub2.InvoiceStatus = "UngiveReversal";
                        invSub2.HasPendingAmendment = true;
                        _db.Entry(invSub2).State = EntityState.Modified;
                    }
                }

                // Void the Invoice row immediately on ungive so the QR code on any downloaded PDF
                // shows "INVOICE CANCELLED" rather than the original valid invoice.
                if (scheduleDTO.IsDone == false && wasIsDone == true)
                {
                    var invoiceToVoid = _db.Invoices
                        .FirstOrDefault(i => i.DoseId == schedule.DoseId
                                          && i.ChildId == schedule.ChildId
                                          && i.DoctorId == scheduleDTO.DoctorId
                                          && i.IsVoided == false);
                    if (invoiceToVoid != null)
                    {
                        invoiceToVoid.IsVoided = true;
                        invoiceToVoid.SupersededBy = "UNGIVEN";
                        _db.Entry(invoiceToVoid).State = EntityState.Modified;
                    }
                }

                if (scheduleDTO.ScheduleBrands.Count > 0)
                {
                    var scheduleBrand = scheduleDTO.ScheduleBrands.Find(
                        x => x.ScheduleId == schedule.Id
                    );
                    if (scheduleBrand != null)
                    {
                        if (scheduleBrand.Validity.HasValue)
                            schedule.Validity = scheduleBrand.Validity;

                        var previousBrandId = schedule.BrandId;
                        schedule.BrandId = scheduleBrand.BrandId;
                        // Site: nurse-chosen per dose (not derived from Brand like Route). Validated
                        // against the brand's route — forced-single routes coerce, invalid ones drop.
                        schedule.Site = NormalizeSiteForRoute(BrandRoute(scheduleBrand.BrandId), scheduleBrand.Site);

                        var bulkStockClinicId = ResolveClinicIdForStock(
                            scheduleDTO.DoctorId,
                            schedule.Child?.ClinicId ?? 0
                        );

                        // v2: the batch this dose consumed (for Schedule.StockId + certificate lot).
                        int? bulkConsumedStockId = null;
                        bool bulkBlankIfNoBatch = false;

                        // v2: the old `GivenDate.Date == today` gate is REMOVED. The deduct/don't
                        // decision is now the §6.2a model, uniform with single give. Backdated,
                        // in-period, brand gives deduct unless the operator chose "just recording".

                        // Ungive transition: dose was given before, now being ungiven. Restore
                        // inventory for whatever brand was actually deducted (previousBrandId).
                        if (wasIsDone == true && scheduleDTO.IsDone == false && previousBrandId.HasValue)
                        {
                            var ungiveClinicId = ResolveClinicIdForStock(
                                scheduleDTO.DoctorId,
                                dbSchedule.Child != null ? dbSchedule.Child.ClinicId : 0
                            );

                            if (ungiveClinicId > 0)
                            {
                                var ungiveInventoryEnabled = IsInventoryEnabledForActor(scheduleDTO.DoctorId, ungiveClinicId);
                                if (ungiveInventoryEnabled)
                                {
                                    var ungiveDoctorId = _db.Clinics
                                        .Where(c => c.Id == ungiveClinicId)
                                        .Select(c => c.DoctorId)
                                        .FirstOrDefault();

                                    if (ungiveDoctorId > 0)
                                    {
                                        var ungiveInventory = _db.BrandAmounts
                                            .Where(b => b.BrandId == previousBrandId
                                                     && b.DoctorId == ungiveDoctorId
                                                     && b.ClinicId == ungiveClinicId)
                                            .FirstOrDefault();

                                        // UnadministerBulkSync mirrors the original give (restores
                                        // only if that give actually consumed); safe to call even
                                        // for OHF/historical/pre-reset gives (it no-ops the stock).
                                        if (ungiveInventory != null)
                                        {
                                            _inventory.UnadministerBulkSync(ungiveInventory, ungiveClinicId, previousBrandId.Value, schedule.Id, scheduleDTO.GivenDate, scheduleDTO.PaId);
                                        }
                                    }
                                }
                            }
                        }

                        // Give transition: dose was not given before, now being given.
                        if (wasIsDone == false && scheduleDTO.IsDone == true
                            && scheduleBrand.BrandId.HasValue && scheduleBrand.BrandId.Value > 0)
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

                            // v2 deduction-decision (§6.2a), uniform with single give.
                            var bulkPeriodStart = _db.Clinics
                                .Where(c => c.Id == onlineClinicId).Select(c => c.StockPeriodStart).FirstOrDefault();
                            var bulkDecision = InventoryTransactionService.ResolveGiveDecision(
                                scheduleBrand.BrandId, scheduleDTO.GivenDate, bulkPeriodStart, scheduleDTO.ReRecordHistorical);
                            if (bulkDecision.NeedsPrompt)
                            {
                                return new Response<ScheduleDTO>(false,
                                    "This dose is backdated. Choose whether it came from your stock before saving.", null);
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

                                // v2: never hard-block on zero stock (§2.8 exception) — the give-at-zero
                                // path records, floors Count at 0, and flags NeedsReconcile.
                                _inventory.AdministerSync(brandInventory, onlineClinicId, schedule.Id,
                                    scheduleDTO.GivenDate, scheduleDTO.PaId,
                                    bulkDecision.ConsumesStock, bulkDecision.Reason, out bulkConsumedStockId);

                                schedule.StockId = bulkConsumedStockId;
                                bulkBlankIfNoBatch = bulkDecision.ConsumesStock;
                            }
                        }

                        // Stamp certificate lot/expiry: from the consumed batch, or blank at
                        // give-at-zero (no fabricated fallback, §6.3). Non-consuming gives keep
                        // the operator-typed lot from scheduleBrand.
                        ApplyStockSourceFields(schedule, scheduleBrand.BrandId, scheduleBrand.Lot,
                            scheduleBrand.Expiry, bulkStockClinicId, bulkConsumedStockId, bulkBlankIfNoBatch);
                    }
                }

                // Only reschedule future doses for non-infinite vaccines
                if (!IsInfiniteDose(schedule.Dose))
                {
                    ChangeDueDatesOfInjectedSchedule(scheduleDTO, schedule);
                }
            }
            // Auto-create assignment when PA bulk-gives vaccines with no prior assignment today,
            // and pin every dose actually given in this batch to it.
            if (scheduleDTO.IsDone && scheduleDTO.PaId.HasValue && scheduleDTO.DoctorId > 0)
            {
                var bulkAssignmentId = EnsurePAAssignment(dbSchedule.ChildId, scheduleDTO.PaId.Value, scheduleDTO.DoctorId, dbSchedule.Child != null ? (long?)dbSchedule.Child.ClinicId : null);
                foreach (var schedule in dbChildSchedules)
                    if (schedule.IsDone)
                        LinkScheduleToAssignment(bulkAssignmentId, schedule.Id);
            }

            // Single transaction for the whole bulk batch (matches the existing single
            // SaveChanges()-for-everything semantics above) — a concurrent give/ungive on any
            // brand touched by this batch now fails the whole batch with a clear retry message
            // instead of silently racing past a stale Count/Quantity read.
            using (var tx = _db.Database.BeginTransaction())
            {
                try
                {
                    _db.SaveChanges();
                    tx.Commit();
                }
                catch (DbUpdateConcurrencyException)
                {
                    tx.Rollback();
                    return new Response<ScheduleDTO>(false, "Inventory was updated by another action just now. Please retry.", null);
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            var bulkMsg = preResetSkipped > 0
                ? $"schedule updated successfully. {preResetSkipped} historical dose(s) from before the current stock period were left untouched — the doctor can undo them one at a time."
                : "schedule updated successfully.";
            return new Response<ScheduleDTO>(true, bulkMsg, null)
            {
                GraceApplied = bulkGraceApplied,
                GraceMessage = bulkGraceApplied ? string.Join(" ", bulkGraceMessages) : null
            };
        }

        [HttpPut("update-bulk-invoice")]
        public Response<object> updateInvoice([FromBody] BulkInvoiceSubmitDTO dto)
        {
            // Use a ±1 day window to guard against UTC/PKT offset causing date mismatch on second call
            var invoiceDateMin = dto.InvoiceDate.Date.AddDays(-1);
            var invoiceDateMax = dto.InvoiceDate.Date.AddDays(1);
            var existing = _db.InvoiceSubmissions.FirstOrDefault(x =>
                x.ChildId == dto.ChildId &&
                x.DoctorId == dto.DoctorId &&
                x.InvoiceDate.Date >= invoiceDateMin &&
                x.InvoiceDate.Date <= invoiceDateMax);

            // If the existing row was ungiven (pending reversal approval), treat this download
            // as a fresh invoice for the re-given vaccines.
            if (existing != null && existing.InvoiceStatus == "UngiveReversal")
            {
                // Doctor re-downloading: auto-cancel the pending amendment so reconciliation queue stays clean.
                // PA re-downloading: leave amendment pending — doctor reviews PA actions later.
                if (!dto.PaId.HasValue)
                {
                    var pendingAmendment = _db.InvoiceAmendments
                        .FirstOrDefault(a => a.InvoiceSubmissionId == existing.Id
                                          && a.AmendmentType == "Ungive"
                                          && a.ApprovedAt == null
                                          && a.RejectedAt == null);
                    if (pendingAmendment != null)
                    {
                        pendingAmendment.ApprovedAt = DateTime.UtcNow;
                        pendingAmendment.Notes = (pendingAmendment.Notes ?? "") + " [Auto-cancelled: doctor re-downloaded]";
                    }
                    existing.InvoiceStatus = "Cancelled";
                    _db.Entry(existing).State = EntityState.Modified;
                }
                existing = null;
            }

            if (existing != null)
            {
                // PA-specific edit restrictions
                if (dto.PaId.HasValue)
                {
                    if (existing.EditCount >= 1)
                        return new Response<object>(false, "Invoice has already been edited once. Further changes are not allowed.", null);

                    var pktToday = DateTime.UtcNow.AddHours(5).Date;
                    if (existing.SubmittedAt.AddHours(5).Date != pktToday)
                        return new Response<object>(false, "Invoice can only be edited on the same day it was first submitted.", null);

                    if (existing.PaId != dto.PaId)
                        return new Response<object>(false, "Only the PA who submitted this invoice can edit it.", null);

                    var oldAmount = existing.TotalAmount;
                    var newAmount = dto.Schedules.Sum(s => s.Amount) + dto.ConsultationFee;

                    // Create an InvoiceAmendment to freeze Amount1 pending doctor approval.
                    // TotalAmount stays at Amount1 until the doctor acts — PA payable is unchanged.
                    _db.InvoiceAmendments.Add(new InvoiceAmendment
                    {
                        InvoiceSubmissionId = existing.Id,
                        AmendmentType = "Edit",
                        OldAmount = oldAmount,
                        NewAmount = newAmount,
                        PaId = dto.PaId.Value,
                        DoctorId = dto.DoctorId,
                        Notes = $"PA edited invoice. Consultation fee: {dto.ConsultationFee}",
                        CreatedAt = DateTime.UtcNow
                    });

                    existing.EditCount++;
                    existing.HasPendingAmendment = true;
                    if (dto.ClinicId.HasValue && existing.ClinicId == null)
                        existing.ClinicId = dto.ClinicId;
                    _db.Entry(existing).State = EntityState.Modified;
                }
                else
                {
                    // Doctor editing — direct update, no amendment gate needed
                    var newAmount = dto.Schedules.Sum(s => s.Amount) + dto.ConsultationFee;
                    existing.ConsultationFee = dto.ConsultationFee;
                    existing.TotalAmount = newAmount;
                    if (dto.ClinicId.HasValue && existing.ClinicId == null)
                        existing.ClinicId = dto.ClinicId;

                    // Self-heal PaId: if the doctor is editing and this invoice isn't linked
                    // to the currently active PA assignment, sync it. Covers cases where the
                    // one-shot stamp in PAAssignmentController.Create missed this invoice
                    // (wrong ordering, no matching PaId==null row at assignment time, etc.).
                    SyncInvoicePaToActiveAssignment(existing, dto.ChildId);

                    _db.Entry(existing).State = EntityState.Modified;
                }
            }
            else
            {
                // First download — only carry over PaymentMode if the PA has actually recorded
                // payment for one of these schedules. Schedule.PaymentMode defaults to "Cash"
                // even when no payment has been collected, so gate on IsPaymentCollected to
                // avoid the reconciliation table showing "Cash" before the PA picks a mode.
                var scheduleIds = dto.Schedules.Select(s => s.Id).ToList();
                var paymentMode = _db.Schedules
                    .Where(s => scheduleIds.Contains(s.Id) && s.IsPaymentCollected && s.PaymentMode != null)
                    .Select(s => s.PaymentMode)
                    .FirstOrDefault();

                var newTotal = dto.Schedules.Sum(s => s.Amount) + dto.ConsultationFee;
                var newInvoice = new InvoiceSubmission
                {
                    ChildId = dto.ChildId,
                    DoctorId = dto.DoctorId,
                    PaId = dto.PaId,
                    ClinicId = dto.ClinicId,
                    InvoiceDate = dto.InvoiceDate.Date,
                    SubmittedAt = DateTime.UtcNow,
                    ConsultationFee = dto.ConsultationFee,
                    TotalAmount = newTotal,
                    EditCount = 0,
                    InvoiceStatus = "Active",
                    PaymentMode = paymentMode
                };

                _db.InvoiceSubmissions.Add(newInvoice);
                _db.SaveChanges(); // need newInvoice.Id before linking it to the assignment below

                // Link this invoice to the child's active assignment's InvoiceSubmissionId FK —
                // whether the doctor downloaded it (PaId stamped here too) or the PA submitted
                // it themselves (PaId already set via dto.PaId — never overwritten, just linked).
                SyncInvoicePaToActiveAssignment(newInvoice, dto.ChildId, allowPaIdOverwrite: !dto.PaId.HasValue);
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

        // Links an InvoiceSubmission to whatever PA is currently actively assigned to this
        // child (there is at most one — PAAssignmentController.Create blocks a second active
        // assignment per child), and stamps the assignment's InvoiceSubmissionId FK directly.
        // No date/PA guessing: "the active assignment for this child" is already a unique,
        // enforced fact, so this is a direct lookup-and-link, not an inference.
        // Used by update-bulk-invoice's doctor-driven branches to self-heal invoices that
        // PAAssignmentController.Create's one-shot stamp missed (e.g. assign-then-download
        // ordering). Caller must SaveChanges() after calling this if invoice.Id was just assigned.
        // allowPaIdOverwrite should be false when a specific PA explicitly submitted this
        // invoice themselves (dto.PaId.HasValue) — their own stamp must never be overwritten,
        // even if a different PA is now the active assignment (e.g. reassigned afterward).
        private long? GetActivePaIdForChild(long childId)
        {
            return _db.PAAssignments
                .Where(a => a.ChildId == childId && !a.IsCancelled && !a.IsCompleted)
                .OrderByDescending(a => a.AssignedAt)
                .Select(a => (long?)a.PersonalAssistantId)
                .FirstOrDefault();
        }

        private void SyncInvoicePaToActiveAssignment(InvoiceSubmission invoice, long childId, bool allowPaIdOverwrite = true)
        {
            var activeAssignment = _db.PAAssignments
                .Where(a => a.ChildId == childId && !a.IsCancelled && !a.IsCompleted)
                .OrderByDescending(a => a.AssignedAt)
                .FirstOrDefault();

            if (activeAssignment == null)
                return;

            if (allowPaIdOverwrite && invoice.PaId != activeAssignment.PersonalAssistantId)
            {
                var pa = _db.PersonalAssistant.Find(activeAssignment.PersonalAssistantId);
                var paName = pa?.Name ?? "PA";
                invoice.PaId = activeAssignment.PersonalAssistantId;
                invoice.SubmittedByLabel = "Doctor/(" + paName + ")";
            }

            if (activeAssignment.InvoiceSubmissionId != invoice.Id && invoice.Id > 0)
            {
                activeAssignment.InvoiceSubmissionId = invoice.Id;
                _db.Entry(activeAssignment).State = EntityState.Modified;
            }

            // Pin this invoice's own schedules to the assignment too — covers a dose the
            // doctor already gave (so it was excluded from any earlier undone-doses pinning)
            // before its invoice was downloaded. Matched by GivenDate falling on the invoice's
            // own InvoiceDate, same convention used everywhere else this invoice is matched.
            var schedulesOnInvoice = _db.Schedules
                .Where(s => s.ChildId == childId
                         && s.IsDone == true
                         && s.GivenDate.HasValue
                         && s.GivenDate.Value.Date == invoice.InvoiceDate.Date)
                .ToList();
            foreach (var s in schedulesOnInvoice)
            {
                LinkScheduleToAssignment(activeAssignment.Id, s.Id);
                // The money-icon visibility check (hasUnpaidDoneVaccine in vaccine.page.ts)
                // reads Schedule.PaymentCollectorPaId directly, not PAAssignmentSchedule — a
                // dose given before any PA was assigned has this stuck at NULL forever (set
                // once at give-time, never revisited). Backfill it here, the same moment the
                // schedule is confirmed to belong to this assignment, so the PA can actually
                // see and record the payment from their own screen.
                if (s.PaymentCollectorPaId != activeAssignment.PersonalAssistantId)
                {
                    s.PaymentCollectorPaId = activeAssignment.PersonalAssistantId;
                    _db.Entry(s).State = EntityState.Modified;
                }
            }
        }

        [HttpGet("invoice-status")]
        public ActionResult GetInvoiceStatus([FromQuery] long childId, [FromQuery] long doctorId, [FromQuery] DateTime invoiceDate)
        {
            // Same ±1 day window as update-bulk-invoice's existing-row lookup, to guard
            // against the same UTC/PKT offset that can shift InvoiceDate by a day between calls.
            var invoiceDateMin = invoiceDate.Date.AddDays(-1);
            var invoiceDateMax = invoiceDate.Date.AddDays(1);
            var submission = _db.InvoiceSubmissions.FirstOrDefault(x =>
                x.ChildId == childId &&
                x.DoctorId == doctorId &&
                x.InvoiceDate.Date >= invoiceDateMin &&
                x.InvoiceDate.Date <= invoiceDateMax);

            if (submission == null)
                return Ok(new { isSubmitted = false, editCount = 0, canEdit = true, submittedByPaId = (long?)null });

            var pktNow = DateTime.UtcNow.AddHours(5);
            bool canEdit = submission.EditCount < 1 && submission.SubmittedAt.AddHours(5).Date == pktNow.Date;
            return Ok(new { isSubmitted = true, editCount = submission.EditCount, canEdit, submittedByPaId = submission.PaId });
        }

        // Converts a GapInDays code into a human-readable duration for messages
        public static string DescribeGapInDays(int gapInDays)
        {
            if (gapInDays >= 401 && gapInDays <= 460)
                return Pluralize(gapInDays - 400, "month");
            else if (gapInDays == 4109)
                return Pluralize(109, "month");
            else if (gapInDays == 4110)
                return Pluralize(110, "month");
            else if (gapInDays == 4113)
                return Pluralize(113, "month");
            else if (gapInDays == 4114)
                return Pluralize(114, "month");
            else if (gapInDays == 4164)
                return Pluralize(164, "month");
            else if (gapInDays == 462)
                return Pluralize(62, "month");
            else if (gapInDays >= 3001 && gapInDays <= 3020)
                return Pluralize(gapInDays - 3000, "year");
            else if (gapInDays >= 7 && gapInDays % 7 == 0)
                return Pluralize(gapInDays / 7, "week");
            else
                return Pluralize(gapInDays, "day");
        }

        private static string Pluralize(int value, string unit)
        {
            return value + " " + unit + (value == 1 ? "" : "s");
        }

        //date Function
        public static DateTime calculateDate(DateTime date, int GapInDays)
        {
            // Months: codes 401-460 = 1-60 months
            if (GapInDays >= 401 && GapInDays <= 460)
                return date.AddMonths(GapInDays - 400);
            // Extended months: 4109, 4110, 4113, 4114
            else if (GapInDays == 4109)
                return date.AddMonths(109);
            else if (GapInDays == 4110)
                return date.AddMonths(110);
            else if (GapInDays == 4113)
                return date.AddMonths(113);
            else if (GapInDays == 4114)
                return date.AddMonths(114);
            else if (GapInDays == 4164)
                return date.AddMonths(164);
            else if (GapInDays == 462)
                return date.AddMonths(62);
            // Years: codes 3001-3020 = 1-20 years
            else if (GapInDays >= 3001 && GapInDays <= 3020)
                return date.AddYears(GapInDays - 3000);
            // Days and weeks: raw AddDays
            else
                return date.AddDays(GapInDays);
        }

        // CDC 4-day grace period: a dose given up to 4 days before its minimum INTERVAL
        // (MinGap) still counts as valid. Applies to MinGap only (not MinAge/MaxAge), and
        // never to vaccines flagged ExactIntervalRequired (cholera/rabies). Same day-count
        // convention as calculateDate (day-of-dose = day 0), so we just subtract days.
        private const int CdcGraceDays = 4;
        private const string CdcGraceUrl = "https://www.cdc.gov/vaccines/hcp/imz-best-practices/timing-spacing-immunobiologics.html";

        // Maps a reschedule rejection message (from ChangeDueDatesOfSchedule, which returns a
        // human-worded string) to a stable code the client branches on. This keeps the last bit
        // of prose-parsing on the server, where the wording is defined — the client no longer
        // has to string-match the message (which silently broke when the casing/wording drifted).
        // Codes: MAX_AGE, MIN_AGE_FROM_DOB, MIN_GAP_FROM_PREV, BEFORE_PREV_DOSE (no override).
        private static string? RuleCodeForRescheduleMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            if (message.Contains("greater than the Max Age of dose"))
                return "MAX_AGE";
            if (message.Contains("minimum age of this vaccine from date of birth should be"))
                return "MIN_AGE_FROM_DOB";
            if (message.Contains("minimum gap from the previous dose of this vaccine should be"))
                return "MIN_GAP_FROM_PREV";
            if (message.Contains("before or on the same date as the previous dose"))
                return "BEFORE_PREV_DOSE";
            return null;
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
                    // BUG-11 — an infinite dose (Flu/Typhoid/Vitamin A) was rescheduled with no
                    // guards at all. It still cannot be moved before the child's DOB, or past its
                    // MaxAge (MinAge/MinGap don't apply — these are single, repeatable doses).
                    Dose infDose = dbSchedule.Dose;
                    if (scheduleDTO.Date.Date < dbSchedule.Child.DOB.Date)
                    {
                        message =
                            "Cannot reschedule to your selected date: "
                            + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                            + " because it is less than date of birth of child.";
                        return message;
                    }
                    if (infDose.MinAge > 0 && scheduleDTO.Date.Date < calculateDate(dbSchedule.Child.DOB, infDose.MinAge).Date && !ignoreMinAgeFromDOB)
                    {
                        message =
                            "Cannot reschedule to your selected date: "
                            + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                            + " because the minimum age of this vaccine from date of birth should be "
                            + DescribeGapInDays(infDose.MinAge) + ".";
                        return message;
                    }
                    if (infDose.MaxAge.HasValue && scheduleDTO.Date.Date > calculateDate(dbSchedule.Child.DOB, infDose.MaxAge.Value).Date && !ignoreMaxAgeRule)
                    {
                        message =
                            "Cannot reschedule to your selected date: "
                            + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                            + " because it is greater than the Max Age of dose.";
                        return message;
                    }

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
                        .Include(x => x.Child)
                        .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                        .FirstOrDefault();
                    if (d.MaxAge.HasValue && scheduleDTO.Date.Date > calculateDate(TargetSchedule.Child.DOB, d.MaxAge.Value).Date && !ignoreMaxAgeRule)
                    {
                        message =
                            "Cannot reschedule to your selected date: "
                            + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                            + " because it is greater than the Max Age of dose.";
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
                            .Include(x => x.Child)
                            .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                            .FirstOrDefault();

                        // if MinGap is this dose < MinAge of Previouse Dose; then dont reschedule
                        // stop updating date of a dose if minimum gap is valid
                        if (TargetSchedule != null)
                        {
                            if (i != 0)
                            {
                                // BUG-3: a null MinGap has no floor — advance the anchor and
                                // leave this dose in place (mirrors the give-cascade guard).
                                if (!MinGap.HasValue)
                                {
                                    previousDate = TargetSchedule.Date;
                                    continue;
                                }
                                // BUG-2 + BUG-10: decode the coded MinGap to a real floor date,
                                // then move the dose forward ONLY to that floor if it is genuinely
                                // too close — not by a blanket AddDays(daysDifference) over-shift.
                                var minGapFloor = calculateDate(previousDate.Date, MinGap.Value).Date;
                                if (TargetSchedule.Date.Date < minGapFloor)
                                    TargetSchedule.Date = minGapFloor;
                            }
                            else
                            {
                                // check for MaxAge of any Dose
                                if (d.MaxAge.HasValue && scheduleDTO.Date.Date > calculateDate(TargetSchedule.Child.DOB, d.MaxAge.Value).Date && !ignoreMaxAgeRule)
                                {
                                    message =
                                        "Cannot reschedule to your selected date: "
                                        + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                        + " because it is greater than the Max Age of dose.";
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
                            .Include(x => x.Child)
                            .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == d.Id)
                            .FirstOrDefault();

                        if (scheduleDTO.Date.Date < FirstDoseSchedule.Child.DOB.Date)
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because it is less than date of birth of child.";
                            return message;
                        }
                        else if (scheduleDTO.Date.Date < calculateDate(FirstDoseSchedule.Child.DOB, d.MinAge).Date && !ignoreMinAgeFromDOB)
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because the minimum age of this vaccine from date of birth should be "
                                + DescribeGapInDays(d.MinAge) + ".";
                            return message;
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
                            .Include(x => x.Child)
                            .Where(x => x.ChildId == dbSchedule.ChildId && x.DoseId == lastDose.Id)
                            .FirstOrDefault();
                        var TargetSchedulePrevious = db.Schedules
                            .Where(
                                x =>
                                    x.ChildId == dbSchedule.ChildId && x.DoseId == secondLastDose.Id
                            )
                            .FirstOrDefault();

                        // check for MaxAge of any Dose
                        if (TargetSchedule != null && lastDose.MaxAge.HasValue && scheduleDTO.Date.Date > calculateDate(TargetSchedule.Child.DOB, lastDose.MaxAge.Value).Date && !ignoreMaxAgeRule)
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because it is greater than the Max Age of dose.";
                            return message;
                        }

                        // check for MinAge from DOB of the target dose — mirror the first-dose
                        // branch above; a later dose must not be pulled below its own age floor.
                        if (TargetSchedule != null && scheduleDTO.Date.Date < calculateDate(TargetSchedule.Child.DOB, lastDose.MinAge).Date && !ignoreMinAgeFromDOB)
                        {
                            message =
                                "Cannot reschedule to your selected date: "
                                + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                + " because the minimum age of this vaccine from date of birth should be "
                                + DescribeGapInDays(lastDose.MinAge) + ".";
                            return message;
                        }

                        if (TargetSchedulePrevious != null)
                        {
                            DateTime previousDoseDate = TargetSchedulePrevious.IsDone && TargetSchedulePrevious.GivenDate.HasValue
                                ? TargetSchedulePrevious.GivenDate.Value.Date
                                : TargetSchedulePrevious.Date.Date;

                            if (scheduleDTO.Date.Date <= previousDoseDate)
                            {
                                message =
                                    "Cannot reschedule to your selected date: "
                                    + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                    + " because it is before or on the same date as the previous dose.";
                                return message;
                            }
                        }

                        if (TargetSchedulePrevious != null && lastDose.MinGap.HasValue)
                        {
                            DateTime previousDoseAnchor = TargetSchedulePrevious.IsDone && TargetSchedulePrevious.GivenDate.HasValue
                                ? TargetSchedulePrevious.GivenDate.Value.Date
                                : TargetSchedulePrevious.Date.Date;

                            DateTime minimumAllowedDate = calculateDate(previousDoseAnchor, lastDose.MinGap.Value);

                            if (scheduleDTO.Date.Date < minimumAllowedDate.Date && !ignoreMinGapFromPreviousDose)
                            {
                                message =
                                    "Cannot reschedule to your selected date: "
                                    + Convert.ToDateTime(scheduleDTO.Date.Date).ToString("dd-MM-yyyy")
                                    + " because the minimum gap from the previous dose of this vaccine should be "
                                    + DescribeGapInDays(lastDose.MinGap.Value) + ".";
                                return message;
                            }
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
            [FromQuery] bool ignoreMinGapFromPreviousDose = false,
            [FromQuery] bool isParent = false
        )
        {
            {
                if (isParent)
                {
                    ignoreMaxAgeRule = false;
                    ignoreMinAgeFromDOB = false;
                    ignoreMinGapFromPreviousDose = false;
                }
                // Step 5 — PA cannot bypass ignore flags on reschedule
                if (scheduleDTO.PaId.HasValue)
                {
                    ignoreMaxAgeRule = false;
                    ignoreMinAgeFromDOB = false;
                    ignoreMinGapFromPreviousDose = false;
                }
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
                    return new Response<ScheduleDTO>(false, message, null)
                    { RuleCode = RuleCodeForRescheduleMessage(message) };
            }
        }

        [HttpDelete("{ChildId}/{DoseId}/{Date}")]
        public async Task<Response<List<Schedule>>> Delete(long ChildId, long DoseId, string date, [FromQuery] long? paId = null, [FromQuery] long? doctorId = null)
        {
            // PA permission check
            if (paId.HasValue)
            {
                var paPerm = _db.PaPermissions.FirstOrDefault(p => p.PaId == paId.Value);
                if (paPerm == null || !paPerm.EditVaccineSchedule)
                    return new Response<List<Schedule>>(false, "You do not have permission to remove vaccines from the schedule.", null);
            }

            DateTime dateOfInjection = DateTime.ParseExact(date, "dd-MM-yyyy", null);
            var dose = await _db.Doses.Include(x => x.Vaccine).FirstOrDefaultAsync(d => d.Id == DoseId);
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

                if (paId.HasValue && doctorId.HasValue)
                {
                    var doseName = dose.Name ?? dose.Vaccine?.Name ?? $"Dose {DoseId}";
                    _db.PaActivityLogs.Add(new PaActivityLog
                    {
                        PaId = paId.Value,
                        DoctorId = doctorId.Value,
                        ClinicId = null,
                        PatientId = ChildId,
                        ActionCode = "SCHEDULE_REMOVE_VACCINE",
                        Description = $"Removed {doseName} from schedule for patient {ChildId}",
                        Notes = "",
                        IsReversal = false,
                        ActionDate = DateTime.UtcNow
                    });
                }

                await _db.SaveChangesAsync();
                return new Response<List<Schedule>>(true, "Only one infinite dose left as undone.", null);
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

                if (paId.HasValue && doctorId.HasValue)
                {
                    var doseName = dose.Name ?? dose.Vaccine?.Name ?? $"Dose {DoseId}";
                    _db.PaActivityLogs.Add(new PaActivityLog
                    {
                        PaId = paId.Value,
                        DoctorId = doctorId.Value,
                        ClinicId = null,
                        PatientId = ChildId,
                        ActionCode = "SCHEDULE_REMOVE_VACCINE",
                        Description = $"Removed {doseName} from schedule for patient {ChildId}",
                        Notes = "",
                        IsReversal = false,
                        ActionDate = DateTime.UtcNow
                    });
                }

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

                // Query direct sales early so we can check before returning NotFound
                var directSales = _db.DirectSales
                    .Where(ds =>
                        ds.ClinicId == clinicId
                        && ds.SaleDate >= parsedFromDate.Date
                        && ds.SaleDate < nextDay
                    )
                    .Select(ds => new
                    {
                        ds.SaleDate,
                        ClientName = ds.ClientName ?? "Direct Sale",
                        BrandName = ds.Brand.Name ?? "Unknown",
                        ds.Quantity,
                        ds.TotalSaleValue,
                    })
                    .OrderBy(ds => ds.SaleDate)
                    .ToList();

                if (!rawSchedules.Any() && !directSales.Any())
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


                    // Direct sale rows
                    decimal totalDirectSalesValue = 0m;
                    if (directSales.Any())
                    {
                        var directSalesByDate = directSales.GroupBy(ds => ds.SaleDate.Date);
                        foreach (var dateGroup in directSalesByDate)
                        {
                            bool isFirstInDate = true;
                            foreach (var ds in dateGroup)
                            {
                                if (isFirstInDate)
                                {
                                    table.AddCell(new PdfPCell(new Phrase(ds.SaleDate.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_LEFT });
                                    table.AddCell(new PdfPCell(new Phrase("Direct Sale", headerFont)) { HorizontalAlignment = Element.ALIGN_LEFT });
                                    table.AddCell(new PdfPCell(new Phrase("", normalFont)) { HorizontalAlignment = Element.ALIGN_RIGHT });
                                    isFirstInDate = false;
                                }
                                else
                                {
                                    table.AddCell(new PdfPCell(new Phrase("", normalFont)) { HorizontalAlignment = Element.ALIGN_LEFT });
                                    table.AddCell(new PdfPCell(new Phrase("", normalFont)) { HorizontalAlignment = Element.ALIGN_LEFT });
                                    table.AddCell(new PdfPCell(new Phrase("", normalFont)) { HorizontalAlignment = Element.ALIGN_RIGHT });
                                }
                                table.AddCell(new PdfPCell(new Phrase(ds.BrandName, normalFont)) { HorizontalAlignment = Element.ALIGN_LEFT });
                                table.AddCell(new PdfPCell(new Phrase(ds.Quantity.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_RIGHT });
                                table.AddCell(new PdfPCell(new Phrase($"₹{ds.TotalSaleValue:N2}", normalFont)) { HorizontalAlignment = Element.ALIGN_RIGHT });
                            }

                            decimal dateTotalSales = dateGroup.Sum(ds => ds.TotalSaleValue);
                            totalDirectSalesValue += dateTotalSales;
                            table.AddCell(new PdfPCell(new Phrase($"Total for Direct Sales ({dateGroup.Key:dd-MM-yyyy}): ₹{dateTotalSales:N2}", headerFont))
                            {
                                Colspan = 6,
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                                Padding = 6,
                            });
                        }
                    }

                    document.Add(table);

                    decimal totalItemsPrice = schedules.Sum(s => s.InvoicePrice) + totalDirectSalesValue;
                    Paragraph summary = new Paragraph(
                        $"\nTotal Patients: {groupedSchedules.Count()}"
                            + $"\nTotal Vaccination Fee: ₹{grandTotalConsultationFee:N2}"
                            + $"\nTotal Items Price: ₹{totalItemsPrice:N2}"
                            + $"\nTotal Direct Sales: ₹{totalDirectSalesValue:N2}"
                            + $"\nGrand Total Cash: ₹{totalItemsPrice + grandTotalConsultationFee:N2}",
                        headerFont
                    );
                    summary.SpacingBefore = 20f;
                    document.Add(summary);

                    document.Close();
                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        ReportFileName.Build("SalesReport", clinicName)
                    );
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }

        [HttpGet("pa-collection-tasks/{paId}")]
        public IActionResult GetPaCollectionTasks(long paId)
        {
            var tasks = _db.Schedules
                .Include(s => s.Child)
                .Include(s => s.Dose).ThenInclude(d => d.Vaccine)
                .Include(s => s.Brand)
                .Where(s => s.PaymentCollectorPaId == paId
                         && s.IsDone == true
                         && s.IsPaymentCollected == false)
                .OrderBy(s => s.GivenDate)
                .ToList();
            var dtos = _mapper.Map<List<ScheduleDTO>>(tasks);
            return Ok(new Response<List<ScheduleDTO>>(true, "OK", dtos));
        }

        [HttpPatch("{id}/mark-payment-collected")]
        public IActionResult MarkPaymentCollected(long id, [FromBody] ScheduleDTO dto)
        {
            var schedule = _db.Schedules.FirstOrDefault(s => s.Id == id);
            if (schedule == null) return Ok(new Response<ScheduleDTO>(false, "Not found", null));
            if (schedule.PaymentCollectorPaId == null)
                return Ok(new Response<ScheduleDTO>(false, "No PA assigned for this payment.", null));
            var allowed = new[] { "Cash", "Online" };
            if (dto.PaymentMode != null && !allowed.Contains(dto.PaymentMode))
                return Ok(new Response<ScheduleDTO>(false, "Invalid payment mode.", null));

            schedule.IsPaymentCollected = true;
            schedule.PaymentMode = dto.PaymentMode ?? schedule.PaymentMode;
            if (dto.Weight > 0) schedule.Weight = dto.Weight;
            if (dto.Height > 0) schedule.Height = dto.Height;
            if (dto.Circle > 0) schedule.Circle = dto.Circle;
            SyncInvoicePaymentMode(schedule);
            _db.SaveChanges();
            return Ok(new Response<ScheduleDTO>(true, "Payment marked as collected.", null));
        }

        [HttpPatch("{id}/record-payment-mode")]
        public IActionResult RecordPaymentMode(long id, [FromBody] ScheduleDTO dto)
        {
            var schedule = _db.Schedules.FirstOrDefault(s => s.Id == id);
            if (schedule == null) return Ok(new Response<ScheduleDTO>(false, "Not found", null));
            var allowed = new[] { "Cash", "Online" };
            if (dto.PaymentMode != null && !allowed.Contains(dto.PaymentMode))
                return Ok(new Response<ScheduleDTO>(false, "Invalid payment mode.", null));
            schedule.PaymentMode = dto.PaymentMode ?? schedule.PaymentMode;
            schedule.OnlineService = dto.OnlineService ?? schedule.OnlineService;
            schedule.IsPaymentCollected = true;
            SyncInvoicePaymentMode(schedule);
            _db.SaveChanges();
            return Ok(new Response<ScheduleDTO>(true, "Payment recorded.", null));
        }

        // PATCH /api/Schedule/{id}/correct-batch
        // §6.3a — label-only correction of a GIVEN dose's batch/expiry/manufacturer. Used to fill
        // in a give-at-zero dose's blank batch later, or fix a typo'd lot. Moves NO stock and
        // writes NO stock-moving ledger row — only an audit BatchCorrection row (who/when/new).
        // The certificate snapshot (Schedule.Lot/Expiry/Manufacturer) is what's edited; StockId
        // is untouched. Permitted for doctor + PA.
        [HttpPatch("{id}/correct-batch")]
        public IActionResult CorrectBatch(long id, [FromBody] ScheduleDTO dto)
        {
            var schedule = _db.Schedules
                .Include(s => s.Child)
                .FirstOrDefault(s => s.Id == id);
            if (schedule == null)
                return Ok(new Response<ScheduleDTO>(false, "Schedule not found.", null));
            if (!schedule.IsDone)
                return Ok(new Response<ScheduleDTO>(false, "Batch details can only be corrected on a given dose.", null));
            if (!schedule.BrandId.HasValue)
                return Ok(new Response<ScheduleDTO>(false, "This dose was recorded as OHF (no brand); it has no batch to correct.", null));

            var newLot = (dto.Lot ?? "").Trim();
            var newManufacturer = (dto.Manufacturer ?? "").Trim();
            var newExpiry = dto.Expiry;

            // Label-only edit of the certificate snapshot — StockId and all stock counters untouched.
            schedule.Lot = newLot;
            schedule.Manufacturer = newManufacturer;
            schedule.Expiry = newExpiry;

            // Audit row (no stock movement). Best-effort clinic resolution; a give always has a child.
            var childClinicId = schedule.Child != null ? schedule.Child.ClinicId : 0;
            var clinicId = ResolveClinicIdForStock(dto.DoctorId, childClinicId);
            var eventDate = schedule.GivenDate ?? ClinicClock.TodayPkt();
            _inventory.LogBatchCorrection(dto.DoctorId, clinicId, schedule.BrandId.Value,
                schedule.StockId, newLot, newExpiry, schedule.Id, eventDate, dto.CorrectByPaId);

            _db.SaveChanges();
            return Ok(new Response<ScheduleDTO>(true, "Batch details corrected.", null));
        }

        // Keeps InvoiceSubmission.PaymentMode in sync with the schedule's actual
        // recorded mode, so the Doctor's reconciliation page reflects what was
        // really collected (invoice creation leaves PaymentMode null until then).
        private void SyncInvoicePaymentMode(Schedule schedule)
        {
            if (!schedule.GivenDate.HasValue) return;
            var relatedInvoice = _db.InvoiceSubmissions
                .Where(x => x.ChildId == schedule.ChildId
                         && x.InvoiceDate.Date == schedule.GivenDate.Value.Date
                         && x.InvoiceStatus != "Cancelled"
                         && x.InvoiceStatus != "UngiveReversal")
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
            if (relatedInvoice != null)
                relatedInvoice.PaymentMode = schedule.PaymentMode;
        }

        // PATCH /api/Schedule/{id}/verify-payment?doctorId=X
        // Doctor verifies a payment (cash or online). Sets IsPaymentApproved + audit trail.
        [HttpPatch("{id}/verify-payment")]
        public IActionResult VerifyPayment(long id, [FromQuery] long doctorId)
        {
            var schedule = _db.Schedules.FirstOrDefault(s => s.Id == id);
            if (schedule == null)
                return Ok(new { IsSuccess = false, Message = "Schedule not found." });
            if (schedule.IsPaymentApproved)
                return Ok(new { IsSuccess = false, Message = "Payment already verified." });

            schedule.IsPaymentApproved = true;
            schedule.IsPAApprove = true;
            schedule.PaymentApprovedAt = DateTime.UtcNow;
            schedule.PaymentApprovedByDoctorId = doctorId;
            _db.SaveChanges();
            return Ok(new { IsSuccess = true, Message = "Payment verified." });
        }

        // PATCH /api/Schedule/confirm-invoice/{id}?doctorId=X
        // Doctor confirms receipt of a full invoice (InvoiceSubmission row).
        [HttpPatch("confirm-invoice/{id}")]
        public IActionResult ConfirmInvoice(long id, [FromQuery] long doctorId)
        {
            var inv = _db.InvoiceSubmissions.FirstOrDefault(i => i.Id == id);
            if (inv == null)
                return Ok(new { IsSuccess = false, Message = "Invoice not found." });
            if (inv.IsConfirmedByDoctor)
                return Ok(new { IsSuccess = false, Message = "Invoice already confirmed." });

            inv.IsConfirmedByDoctor = true;
            inv.ConfirmedAt = DateTime.UtcNow;
            _db.SaveChanges();
            return Ok(new { IsSuccess = true, Message = "Invoice confirmed." });
        }
    }
}
