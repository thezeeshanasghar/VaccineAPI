using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ColdChainController : ControllerBase
    {
        private readonly Context _db;

        public ColdChainController(Context db)
        {
            _db = db;
        }

        // Minimum readings required per fridge per day (soft compliance target, not a hard block).
        private const int RequiredReadingsPerDay = 2;

        // Sanity bounds for a logged temperature — anything outside this range is almost
        // certainly a data-entry error, not a real cold-chain excursion.
        private const decimal MinSaneTemp = -40m;
        private const decimal MaxSaneTemp = 60m;

        // ---------------- Refrigerators ----------------

        // GET /api/coldchain/refrigerators?clinicId=X
        [HttpGet("refrigerators")]
        public async Task<IActionResult> GetRefrigerators([FromQuery] long clinicId)
        {
            var fridges = await _db.Refrigerators
                .Where(f => f.ClinicId == clinicId && f.IsActive)
                .OrderBy(f => f.Name)
                .Select(f => ToDto(f))
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = fridges });
        }

        // POST /api/coldchain/refrigerators
        [HttpPost("refrigerators")]
        public async Task<IActionResult> CreateRefrigerator([FromBody] RefrigeratorCreateDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Ok(new { IsSuccess = false, Message = "Name is required" });
            if (string.IsNullOrWhiteSpace(dto.SerialNumber))
                return Ok(new { IsSuccess = false, Message = "Serial number is required" });
            if (dto.MinTemp > dto.MaxTemp)
                return Ok(new { IsSuccess = false, Message = "Min temp cannot be greater than max temp" });

            var fridge = new Refrigerator
            {
                DoctorId     = dto.DoctorId,
                ClinicId     = dto.ClinicId,
                Name         = dto.Name.Trim(),
                SerialNumber = dto.SerialNumber.Trim(),
                Type         = string.IsNullOrWhiteSpace(dto.Type) ? "Refrigerator" : dto.Type,
                MinTemp      = dto.MinTemp,
                MaxTemp      = dto.MaxTemp,
                Location     = dto.Location?.Trim(),
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            };

            _db.Refrigerators.Add(fridge);
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Refrigerator added", ResponseData = ToDto(fridge) });
        }

        // PUT /api/coldchain/refrigerators/{id}
        [HttpPut("refrigerators/{id}")]
        public async Task<IActionResult> UpdateRefrigerator(long id, [FromBody] RefrigeratorCreateDTO dto)
        {
            var fridge = await _db.Refrigerators.FindAsync(id);
            if (fridge == null)
                return Ok(new { IsSuccess = false, Message = "Refrigerator not found" });

            if (dto.MinTemp > dto.MaxTemp)
                return Ok(new { IsSuccess = false, Message = "Min temp cannot be greater than max temp" });

            fridge.Name         = string.IsNullOrWhiteSpace(dto.Name) ? fridge.Name : dto.Name.Trim();
            fridge.SerialNumber = string.IsNullOrWhiteSpace(dto.SerialNumber) ? fridge.SerialNumber : dto.SerialNumber.Trim();
            fridge.Type         = string.IsNullOrWhiteSpace(dto.Type) ? fridge.Type : dto.Type;
            fridge.MinTemp      = dto.MinTemp;
            fridge.MaxTemp      = dto.MaxTemp;
            fridge.Location     = dto.Location?.Trim();

            _db.Entry(fridge).State = EntityState.Modified;
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Refrigerator updated", ResponseData = ToDto(fridge) });
        }

        // DELETE /api/coldchain/refrigerators/{id}
        [HttpDelete("refrigerators/{id}")]
        public async Task<IActionResult> DeleteRefrigerator(long id)
        {
            var fridge = await _db.Refrigerators.FindAsync(id);
            if (fridge == null)
                return Ok(new { IsSuccess = false, Message = "Refrigerator not found" });

            // Soft delete only — historical readings point at this fridge.
            fridge.IsActive = false;
            _db.Entry(fridge).State = EntityState.Modified;
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Refrigerator removed" });
        }

        // ---------------- Temperature Readings ----------------

        // POST /api/coldchain/readings
        [HttpPost("readings")]
        public async Task<IActionResult> CreateReading([FromBody] TemperatureReadingCreateDTO dto)
        {
            if (dto.Temperature < MinSaneTemp || dto.Temperature > MaxSaneTemp)
                return Ok(new { IsSuccess = false, Message = $"Temperature must be between {MinSaneTemp} and {MaxSaneTemp}" });

            var fridge = await _db.Refrigerators.FindAsync(dto.RefrigeratorId);
            if (fridge == null)
                return Ok(new { IsSuccess = false, Message = "Refrigerator not found" });

            // Never trust the client's DoctorId claim — verify the fridge actually belongs to them.
            if (fridge.DoctorId != dto.DoctorId)
                return Ok(new { IsSuccess = false, Message = "This refrigerator does not belong to the specified doctor" });

            var isInRange = dto.Temperature >= fridge.MinTemp && dto.Temperature <= fridge.MaxTemp;

            var reading = new TemperatureReading
            {
                RefrigeratorId  = dto.RefrigeratorId,
                DoctorId        = dto.DoctorId,
                ClinicId        = fridge.ClinicId,
                Temperature     = dto.Temperature,
                RecordedDate    = dto.RecordedDate == default ? DateTime.Today : dto.RecordedDate.Date,
                RecordedTime    = string.IsNullOrWhiteSpace(dto.RecordedTime) ? DateTime.Now.ToString("HH:mm") : dto.RecordedTime,
                RecordedByPaId  = dto.RecordedByPaId,
                RecordedByName  = dto.RecordedByName ?? "",
                Notes           = dto.Notes?.Trim(),
                IsInRange       = isInRange, // always server-calculated, never trust client input
                CreatedAt       = DateTime.UtcNow
            };

            _db.TemperatureReadings.Add(reading);
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Reading logged", ResponseData = ToDto(reading, fridge.Name) });
        }

        // GET /api/coldchain/readings?clinicId=X&fromDate=Y&toDate=Z&refrigeratorId=W
        [HttpGet("readings")]
        public async Task<IActionResult> GetReadings(
            [FromQuery] long clinicId,
            [FromQuery] DateTime fromDate,
            [FromQuery] DateTime toDate,
            [FromQuery] long? refrigeratorId = null)
        {
            var from = fromDate.Date;
            var to = toDate.Date;

            var query = _db.TemperatureReadings
                .Where(r => r.ClinicId == clinicId && r.RecordedDate >= from && r.RecordedDate <= to);

            if (refrigeratorId.HasValue)
                query = query.Where(r => r.RefrigeratorId == refrigeratorId.Value);

            var readings = await query
                .OrderByDescending(r => r.RecordedDate)
                .ThenByDescending(r => r.RecordedTime)
                .ToListAsync();

            var fridgeNames = await _db.Refrigerators
                .Where(f => f.ClinicId == clinicId)
                .ToDictionaryAsync(f => f.Id, f => f.Name);

            var result = readings.Select(r => ToDto(r, fridgeNames.TryGetValue(r.RefrigeratorId, out var n) ? n : null)).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // GET /api/coldchain/requirement-status?clinicId=X&date=Y
        [HttpGet("requirement-status")]
        public async Task<IActionResult> GetRequirementStatus([FromQuery] long clinicId, [FromQuery] DateTime date)
        {
            var day = date == default ? DateTime.Today : date.Date;

            var fridges = await _db.Refrigerators
                .Where(f => f.ClinicId == clinicId && f.IsActive)
                .ToListAsync();

            var todaysReadings = await _db.TemperatureReadings
                .Where(r => r.ClinicId == clinicId && r.RecordedDate == day)
                .ToListAsync();

            var result = fridges.Select(f =>
            {
                var readingsForFridge = todaysReadings.Where(r => r.RefrigeratorId == f.Id).ToList();
                var last = readingsForFridge
                    .OrderByDescending(r => r.RecordedTime)
                    .FirstOrDefault();

                return new ColdChainRequirementStatusDTO
                {
                    RefrigeratorId  = f.Id,
                    RefrigeratorName = f.Name,
                    ReadingsToday   = readingsForFridge.Count,
                    RequirementMet  = readingsForFridge.Count >= RequiredReadingsPerDay,
                    LastReadingTime = last?.RecordedTime
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // ---------------- Approvals ----------------

        // GET /api/coldchain/approvals/rollup?doctorId=X&weekStart=Y
        [HttpGet("approvals/rollup")]
        public async Task<IActionResult> GetRollup([FromQuery] long doctorId, [FromQuery] DateTime weekStart)
        {
            var (start, end) = WeekBounds(weekStart);

            var clinics = await _db.Clinics.Where(c => c.DoctorId == doctorId).ToListAsync();
            var result = new List<ColdChainClinicRollupDTO>();

            foreach (var clinic in clinics)
            {
                var fridges = await _db.Refrigerators
                    .Where(f => f.ClinicId == clinic.Id && f.IsActive)
                    .ToListAsync();

                if (fridges.Count == 0)
                {
                    // No fridges registered yet for this clinic — nothing to roll up.
                    result.Add(new ColdChainClinicRollupDTO
                    {
                        ClinicId          = clinic.Id,
                        ClinicName        = clinic.Name,
                        CompliancePercent = 0,
                        MissedChecks      = 0,
                        ExcursionCount    = 0,
                        Status            = "pending",
                        FridgeCount       = 0
                    });
                    continue;
                }

                var fridgeIds = fridges.Select(f => f.Id).ToHashSet();

                var readings = await _db.TemperatureReadings
                    .Where(r => r.ClinicId == clinic.Id && r.RecordedDate >= start && r.RecordedDate <= end)
                    .ToListAsync();

                var stats = ComputeWeekStats(fridges, readings, start, end);

                var log = await _db.ColdChainApprovalLogs
                    .Where(l => l.ClinicId == clinic.Id && l.WeekStartDate == start)
                    .FirstOrDefaultAsync();

                result.Add(new ColdChainClinicRollupDTO
                {
                    ClinicId          = clinic.Id,
                    ClinicName        = clinic.Name,
                    CompliancePercent = stats.CompliancePercent,
                    MissedChecks      = stats.MissedChecks,
                    ExcursionCount    = stats.OutOfRangeCount,
                    Status            = log?.Status ?? "pending",
                    FridgeCount       = fridges.Count
                });
            }

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // GET /api/coldchain/approvals/clinic?clinicId=X&weekStart=Y
        [HttpGet("approvals/clinic")]
        public async Task<IActionResult> GetClinicDrillDown([FromQuery] long clinicId, [FromQuery] DateTime weekStart)
        {
            var (start, end) = WeekBounds(weekStart);

            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null)
                return Ok(new { IsSuccess = false, Message = "Clinic not found" });

            var fridges = await _db.Refrigerators
                .Where(f => f.ClinicId == clinicId && f.IsActive)
                .ToListAsync();

            var readings = await _db.TemperatureReadings
                .Where(r => r.ClinicId == clinicId && r.RecordedDate >= start && r.RecordedDate <= end)
                .ToListAsync();

            var stats = ComputeWeekStats(fridges, readings, start, end);

            var log = await _db.ColdChainApprovalLogs
                .Where(l => l.ClinicId == clinicId && l.WeekStartDate == start)
                .FirstOrDefaultAsync();

            if (log == null)
            {
                log = new ColdChainApprovalLog
                {
                    DoctorId        = clinic.DoctorId,
                    ClinicId        = clinicId,
                    WeekStartDate   = start,
                    WeekEndDate     = end,
                    TotalReadings   = readings.Count,
                    InRangeCount    = stats.InRangeCount,
                    OutOfRangeCount = stats.OutOfRangeCount,
                    RequiredChecks  = stats.RequiredChecks,
                    MissedChecks    = stats.MissedChecks,
                    Status          = "pending",
                    CreatedAt       = DateTime.UtcNow
                };
                _db.ColdChainApprovalLogs.Add(log);
                try { await _db.SaveChangesAsync(); }
                catch (Exception ex)
                {
                    return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
                }
            }
            else
            {
                // Keep the stats snapshot current in case new readings came in since the row was created.
                log.TotalReadings   = readings.Count;
                log.InRangeCount    = stats.InRangeCount;
                log.OutOfRangeCount = stats.OutOfRangeCount;
                log.RequiredChecks  = stats.RequiredChecks;
                log.MissedChecks    = stats.MissedChecks;
                _db.Entry(log).State = EntityState.Modified;
                try { await _db.SaveChangesAsync(); } catch { }
            }

            var fridgeBreakdown = fridges.Select(f =>
            {
                var fridgeReadings = readings.Where(r => r.RefrigeratorId == f.Id).ToList();
                var inRange = fridgeReadings.Count(r => r.IsInRange);
                var required = RequiredReadingsPerDay * DaysInRange(start, end);
                var missed = ComputeMissedChecks(f.Id, fridgeReadings, start, end);

                return new ColdChainFridgeBreakdownDTO
                {
                    RefrigeratorId    = f.Id,
                    RefrigeratorName  = f.Name,
                    InRangeCount      = inRange,
                    TotalReadings     = fridgeReadings.Count,
                    RequiredChecks    = required,
                    MissedChecks      = missed,
                    CompliancePercent = fridgeReadings.Count == 0 ? 0 : Math.Round((decimal)inRange / fridgeReadings.Count * 100, 1)
                };
            }).ToList();

            var excursionReadings = readings
                .Where(r => !r.IsInRange)
                .OrderByDescending(r => r.RecordedDate)
                .ThenByDescending(r => r.RecordedTime)
                .Select(r => ToDto(r, fridges.FirstOrDefault(f => f.Id == r.RefrigeratorId)?.Name))
                .ToList();

            var fridgesWithNoReadings = fridges
                .Where(f => !readings.Any(r => r.RefrigeratorId == f.Id))
                .Select(f => ToDto(f))
                .ToList();

            var response = ToDto(log);
            response.FridgeBreakdown = fridgeBreakdown;
            response.ExcursionReadings = excursionReadings;
            response.FridgesWithNoReadings = fridgesWithNoReadings;

            return Ok(new { IsSuccess = true, ResponseData = response });
        }

        // POST /api/coldchain/approvals/{id}/submit
        [HttpPost("approvals/{id}/submit")]
        public async Task<IActionResult> SubmitApproval(long id, [FromBody] ColdChainApprovalSubmitDTO dto)
        {
            var log = await _db.ColdChainApprovalLogs.FindAsync(id);
            if (log == null)
                return Ok(new { IsSuccess = false, Message = "Approval record not found" });

            var validStatuses = new[] { "approved", "flagged", "rejected", "pending" };
            if (string.IsNullOrWhiteSpace(dto.Status) || !validStatuses.Contains(dto.Status))
                return Ok(new { IsSuccess = false, Message = "Invalid status" });

            log.Status         = dto.Status;
            log.DoctorComments = dto.Comments?.Trim();
            log.ApprovalDate   = DateTime.UtcNow;
            log.UpdatedAt      = DateTime.UtcNow;

            _db.Entry(log).State = EntityState.Modified;
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Approval saved", ResponseData = ToDto(log) });
        }

        // GET /api/coldchain/approvals/history?clinicId=X&limit=10
        [HttpGet("approvals/history")]
        public async Task<IActionResult> GetApprovalHistory([FromQuery] long clinicId, [FromQuery] int limit = 10)
        {
            var logs = await _db.ColdChainApprovalLogs
                .Where(l => l.ClinicId == clinicId)
                .OrderByDescending(l => l.WeekStartDate)
                .Take(limit <= 0 ? 10 : limit)
                .Select(l => ToDto(l))
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = logs });
        }

        // ---------------- Helpers ----------------

        private static (DateTime start, DateTime end) WeekBounds(DateTime weekStart)
        {
            var start = (weekStart == default ? DateTime.Today : weekStart).Date;
            var end = start.AddDays(6);
            return (start, end);
        }

        private static int DaysInRange(DateTime start, DateTime end) => (end.Date - start.Date).Days + 1;

        private class WeekStats
        {
            public int InRangeCount;
            public int OutOfRangeCount;
            public int RequiredChecks;
            public int MissedChecks;
            public decimal CompliancePercent;
        }

        private static WeekStats ComputeWeekStats(List<Refrigerator> fridges, List<TemperatureReading> readings, DateTime start, DateTime end)
        {
            var inRange = readings.Count(r => r.IsInRange);
            var outOfRange = readings.Count(r => !r.IsInRange);
            var days = DaysInRange(start, end);
            var required = fridges.Count * RequiredReadingsPerDay * days;

            var missed = 0;
            foreach (var fridge in fridges)
            {
                var fridgeReadings = readings.Where(r => r.RefrigeratorId == fridge.Id).ToList();
                missed += ComputeMissedChecks(fridge.Id, fridgeReadings, start, end);
            }

            var compliance = readings.Count == 0 ? 0 : Math.Round((decimal)inRange / readings.Count * 100, 1);

            return new WeekStats
            {
                InRangeCount      = inRange,
                OutOfRangeCount   = outOfRange,
                RequiredChecks    = required,
                MissedChecks      = missed,
                CompliancePercent = compliance
            };
        }

        // A "missed check" day is one where fewer than RequiredReadingsPerDay readings were logged
        // for this fridge. NOTE: this is a first-pass placeholder — it only checks the *count* of
        // readings per day, not whether they were spread out over the day. Exact spacing/window
        // logic (e.g. requiring readings be some minimum hours apart) can be refined later.
        private static int ComputeMissedChecks(long refrigeratorId, List<TemperatureReading> fridgeReadings, DateTime start, DateTime end)
        {
            var missedDays = 0;
            for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
            {
                var countForDay = fridgeReadings.Count(r => r.RecordedDate == day);
                if (countForDay < RequiredReadingsPerDay)
                    missedDays++;
            }
            return missedDays;
        }

        private static RefrigeratorResponseDTO ToDto(Refrigerator f) => new RefrigeratorResponseDTO
        {
            Id           = f.Id,
            DoctorId     = f.DoctorId,
            ClinicId     = f.ClinicId,
            Name         = f.Name,
            SerialNumber = f.SerialNumber,
            Type         = f.Type,
            MinTemp      = f.MinTemp,
            MaxTemp      = f.MaxTemp,
            Location     = f.Location,
            IsActive     = f.IsActive,
            CreatedAt    = f.CreatedAt
        };

        private static TemperatureReadingResponseDTO ToDto(TemperatureReading r, string? refrigeratorName) => new TemperatureReadingResponseDTO
        {
            Id               = r.Id,
            RefrigeratorId   = r.RefrigeratorId,
            RefrigeratorName = refrigeratorName,
            DoctorId         = r.DoctorId,
            ClinicId         = r.ClinicId,
            Temperature      = r.Temperature,
            RecordedDate     = r.RecordedDate,
            RecordedTime     = r.RecordedTime,
            RecordedByPaId   = r.RecordedByPaId,
            RecordedByName   = r.RecordedByName,
            Notes            = r.Notes,
            IsInRange        = r.IsInRange,
            CreatedAt        = r.CreatedAt
        };

        private static ColdChainApprovalResponseDTO ToDto(ColdChainApprovalLog l) => new ColdChainApprovalResponseDTO
        {
            Id              = l.Id,
            DoctorId        = l.DoctorId,
            ClinicId        = l.ClinicId,
            WeekStartDate   = l.WeekStartDate,
            WeekEndDate     = l.WeekEndDate,
            TotalReadings   = l.TotalReadings,
            InRangeCount    = l.InRangeCount,
            OutOfRangeCount = l.OutOfRangeCount,
            RequiredChecks  = l.RequiredChecks,
            MissedChecks    = l.MissedChecks,
            Status          = l.Status,
            DoctorComments  = l.DoctorComments,
            ApprovalDate    = l.ApprovalDate,
            CreatedAt       = l.CreatedAt,
            UpdatedAt       = l.UpdatedAt
        };
    }
}
