using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaCashHandoverController : ControllerBase
    {
        private readonly Context _db;
        public PaCashHandoverController(Context db) { _db = db; }

        private decimal ComputeCashInHand(long paId, long clinicId)
        {
            var totalCollected = _db.Schedules
                .Include(s => s.Child)
                .Where(s => (s.GivenByPaId == paId || (s.PaymentCollectorPaId == paId && s.IsPaymentCollected == true))
                         && s.IsDone == true
                         && s.PaymentMode == "Cash"
                         && s.Child.ClinicId == clinicId
                         && s.Amount != null)
                .Sum(s => (decimal?)s.Amount) ?? 0m;

            var totalHandedOver = _db.PaCashHandovers
                .Where(h => h.PaId == paId && h.ClinicId == clinicId && h.Status == "Confirmed")
                .Sum(h => (decimal?)h.Amount) ?? 0m;

            return totalCollected - totalHandedOver;
        }

        // GET /api/PaCashHandover/cash-in-hand/{paId}/{clinicId}
        [HttpGet("cash-in-hand/{paId}/{clinicId}")]
        public IActionResult GetCashInHand(long paId, long clinicId)
        {
            var amount = ComputeCashInHand(paId, clinicId);
            return Ok(new { IsSuccess = true, Message = "OK", ResponseData = amount });
        }

        // POST /api/PaCashHandover
        [HttpPost]
        public IActionResult CreateHandover([FromBody] PaCashHandoverDTO dto)
        {
            var alreadyPending = _db.PaCashHandovers
                .Any(h => h.PaId == dto.PaId && h.ClinicId == dto.ClinicId && h.Status == "Pending");
            if (alreadyPending)
                return Ok(new { IsSuccess = false, Message = "A handover is already pending. Wait for the doctor to confirm." });

            var amount = ComputeCashInHand(dto.PaId, dto.ClinicId);
            if (amount <= 0)
                return Ok(new { IsSuccess = false, Message = "No cash to hand over." });

            var handover = new PaCashHandover
            {
                PaId = dto.PaId,
                ClinicId = dto.ClinicId,
                DoctorId = dto.DoctorId,
                Amount = amount,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _db.PaCashHandovers.Add(handover);
            _db.SaveChanges();

            var pa = _db.PersonalAssistant.FirstOrDefault(p => p.Id == dto.PaId);
            var clinic = _db.Clinics.FirstOrDefault(c => c.Id == dto.ClinicId);

            var result = new PaCashHandoverDTO
            {
                Id = handover.Id,
                PaId = handover.PaId,
                PaName = pa != null ? pa.Name : "",
                ClinicId = handover.ClinicId,
                ClinicName = clinic != null ? clinic.Name : "",
                DoctorId = handover.DoctorId,
                Amount = handover.Amount,
                Status = handover.Status,
                CreatedAt = handover.CreatedAt,
                CashInHand = 0
            };
            return Ok(new { IsSuccess = true, Message = "Handover created.", ResponseData = result });
        }

        // GET /api/PaCashHandover/pending/{doctorId}
        [HttpGet("pending/{doctorId}")]
        public IActionResult GetPending(long doctorId)
        {
            var clinicIds = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToList();

            var handovers = _db.PaCashHandovers
                .Where(h => clinicIds.Contains(h.ClinicId) && h.Status == "Pending")
                .OrderByDescending(h => h.CreatedAt)
                .ToList();

            var paIds = handovers.Select(h => h.PaId).Distinct().ToList();
            var pas = _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionary(p => p.Id, p => p.Name);
            var clinicMap = _db.Clinics.Where(c => clinicIds.Contains(c.Id)).ToDictionary(c => c.Id, c => c.Name);

            var result = handovers.Select(h => new PaCashHandoverDTO
            {
                Id = h.Id,
                PaId = h.PaId,
                PaName = pas.ContainsKey(h.PaId) ? pas[h.PaId] : "",
                ClinicId = h.ClinicId,
                ClinicName = clinicMap.ContainsKey(h.ClinicId) ? clinicMap[h.ClinicId] : "",
                DoctorId = h.DoctorId,
                Amount = h.Amount,
                Status = h.Status,
                CreatedAt = h.CreatedAt,
                ConfirmedAt = h.ConfirmedAt,
                CashInHand = ComputeCashInHand(h.PaId, h.ClinicId)
            }).ToList();

            return Ok(new { IsSuccess = true, Message = "OK", ResponseData = result });
        }

        // GET /api/PaCashHandover/history/{paId}/{clinicId}
        [HttpGet("history/{paId}/{clinicId}")]
        public IActionResult GetHistory(long paId, long clinicId)
        {
            var handovers = _db.PaCashHandovers
                .Where(h => h.PaId == paId && h.ClinicId == clinicId)
                .OrderByDescending(h => h.CreatedAt)
                .ToList();

            var clinic = _db.Clinics.FirstOrDefault(c => c.Id == clinicId);
            var pa = _db.PersonalAssistant.FirstOrDefault(p => p.Id == paId);

            var result = handovers.Select(h => new PaCashHandoverDTO
            {
                Id = h.Id,
                PaId = h.PaId,
                PaName = pa != null ? pa.Name : "",
                ClinicId = h.ClinicId,
                ClinicName = clinic != null ? clinic.Name : "",
                DoctorId = h.DoctorId,
                Amount = h.Amount,
                Status = h.Status,
                CreatedAt = h.CreatedAt,
                ConfirmedAt = h.ConfirmedAt,
                RejectionNote = h.RejectionNote
            }).ToList();

            var cashInHand = ComputeCashInHand(paId, clinicId);
            return Ok(new { IsSuccess = true, Message = "OK", ResponseData = result, CashInHand = cashInHand });
        }

        // PATCH /api/PaCashHandover/{id}/confirm
        [HttpPatch("{id}/confirm")]
        public IActionResult Confirm(long id)
        {
            var handover = _db.PaCashHandovers.FirstOrDefault(h => h.Id == id);
            if (handover == null)
                return Ok(new { IsSuccess = false, Message = "Handover not found." });
            if (handover.Status != "Pending")
                return Ok(new { IsSuccess = false, Message = "Handover is not pending." });

            handover.Status = "Confirmed";
            handover.ConfirmedAt = DateTime.UtcNow;
            _db.SaveChanges();
            return Ok(new { IsSuccess = true, Message = "Handover confirmed." });
        }

        // GET /api/PaCashHandover/daily-summary/{doctorId}?date=YYYY-MM-DD
        // Shows activity for a specific date with per-schedule breakdown per PA.
        // Totals (CashTotal, OnlineTotal) reflect that date only.
        // HandedOver and PendingCash reflect cumulative lifetime balance (correct for running ledger).
        [HttpGet("daily-summary/{doctorId}")]
        public IActionResult GetDailySummary(long doctorId, [FromQuery] string date = null)
        {
            DateTime targetDate;
            try { targetDate = date != null ? DateTime.Parse(date).Date : DateTime.UtcNow.Date; }
            catch { targetDate = DateTime.UtcNow.Date; }

            var clinicIds = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToList();

            var rows = _db.Schedules
                .Include(s => s.Child)
                .Include(s => s.Dose).ThenInclude(d => d.Vaccine)
                .Where(s => s.IsDone
                    && s.DoneAt.HasValue
                    && s.DoneAt.Value.Date == targetDate
                    && clinicIds.Contains(s.Child.ClinicId))
                .ToList();

            // PA-collected rows
            var paRows = rows.Where(s => s.PaymentCollectorPaId.HasValue).ToList();
            var paIds = paRows.Select(s => s.PaymentCollectorPaId.Value).Distinct().ToList();
            var paNames = _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.Name);

            var summary = paIds.Select(paId =>
            {
                var paSchedules = paRows.Where(s => s.PaymentCollectorPaId == paId).ToList();
                var cashTotal = paSchedules
                    .Where(s => s.PaymentMode == "Cash")
                    .Sum(s => (decimal?)s.Amount) ?? 0m;
                var onlineTotal = paSchedules
                    .Where(s => s.PaymentMode != "Cash")
                    .Sum(s => (decimal?)s.Amount) ?? 0m;
                // Lifetime cumulative handovers — intentionally not date-filtered
                var handedOver = _db.PaCashHandovers
                    .Where(h => h.PaId == paId && clinicIds.Contains(h.ClinicId) && h.Status == "Confirmed")
                    .Sum(h => (decimal?)h.Amount) ?? 0m;
                var breakdown = paSchedules.Select(s => new {
                    ScheduleId = s.Id,
                    ChildName = s.Child?.Name ?? "",
                    VaccineName = s.Dose?.Vaccine?.Name ?? s.Dose?.Name ?? "",
                    Amount = s.Amount ?? 0m,
                    PaymentMode = s.PaymentMode,
                    OnlineService = s.OnlineService,
                    IsPaymentApproved = s.IsPaymentApproved,
                    IsPaymentCollected = s.IsPaymentCollected,
                    PaymentApprovedAt = s.PaymentApprovedAt
                }).ToList();
                return new {
                    PaId = paId,
                    PaName = paNames.ContainsKey(paId) ? paNames[paId] : "",
                    CashTotal = cashTotal,
                    OnlineTotal = onlineTotal,
                    HandedOver = handedOver,
                    PendingCash = ComputeCashInHand(paId, paRows.FirstOrDefault(s => s.PaymentCollectorPaId == paId)?.Child?.ClinicId ?? 0),
                    Schedules = breakdown
                };
            }).ToList();

            // Doctor self-collected rows (PaymentCollectorPaId is null but payment collected)
            var doctorRows = rows.Where(s => !s.PaymentCollectorPaId.HasValue && s.IsPaymentCollected).ToList();
            object doctorEntry = null;
            if (doctorRows.Any())
            {
                var doctorBreakdown = doctorRows.Select(s => new {
                    ScheduleId = s.Id,
                    ChildName = s.Child?.Name ?? "",
                    VaccineName = s.Dose?.Vaccine?.Name ?? s.Dose?.Name ?? "",
                    Amount = s.Amount ?? 0m,
                    PaymentMode = s.PaymentMode,
                    OnlineService = s.OnlineService,
                    IsPaymentApproved = s.IsPaymentApproved,
                    IsPaymentCollected = s.IsPaymentCollected,
                    PaymentApprovedAt = s.PaymentApprovedAt
                }).ToList();
                doctorEntry = new {
                    PaId = (long?)null,
                    PaName = "Doctor",
                    CashTotal = doctorRows.Where(s => s.PaymentMode == "Cash").Sum(s => (decimal?)s.Amount) ?? 0m,
                    OnlineTotal = doctorRows.Where(s => s.PaymentMode != "Cash").Sum(s => (decimal?)s.Amount) ?? 0m,
                    HandedOver = 0m,
                    PendingCash = 0m,
                    Schedules = doctorBreakdown
                };
            }

            var pending = _db.PaCashHandovers
                .Where(h => clinicIds.Contains(h.ClinicId) && h.Status == "Pending")
                .Select(h => new {
                    h.Id, h.PaId, h.ClinicId, h.Amount, h.Status, h.CreatedAt
                })
                .ToList();

            return Ok(new { IsSuccess = true, ResponseData = new { Summary = summary, DoctorEntry = doctorEntry, PendingHandovers = pending } });
        }

        // GET /api/PaCashHandover/outstanding/{doctorId}
        // All PAs with uncleared cash balance (any date). The doctor's weekly cleanup view.
        [HttpGet("outstanding/{doctorId}")]
        public IActionResult GetOutstanding(long doctorId)
        {
            var clinics = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .ToList();
            var clinicIds = clinics.Select(c => c.Id).ToList();

            // Find all PA+clinic combos that have ever had a schedule payment under this doctor
            var paClinics = _db.Schedules
                .Include(s => s.Child)
                .Where(s => s.IsDone
                    && s.PaymentCollectorPaId.HasValue
                    && clinicIds.Contains(s.Child.ClinicId))
                .Select(s => new { PaId = s.PaymentCollectorPaId.Value, s.Child.ClinicId })
                .Distinct()
                .ToList();

            var paIds = paClinics.Select(x => x.PaId).Distinct().ToList();
            var paNames = _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.Name);
            var clinicMap = clinics.ToDictionary(c => c.Id, c => c.Name ?? "");

            var result = paClinics
                .Select(pc => new {
                    PaId = pc.PaId,
                    PaName = paNames.ContainsKey(pc.PaId) ? paNames[pc.PaId] : "",
                    ClinicId = pc.ClinicId,
                    ClinicName = clinicMap.ContainsKey(pc.ClinicId) ? clinicMap[pc.ClinicId] : "",
                    PendingCash = ComputeCashInHand(pc.PaId, pc.ClinicId)
                })
                .Where(x => x.PendingCash > 0)
                .OrderByDescending(x => x.PendingCash)
                .ToList();

            var pendingHandovers = _db.PaCashHandovers
                .Where(h => clinicIds.Contains(h.ClinicId) && h.Status == "Pending")
                .Select(h => new { h.Id, h.PaId, h.ClinicId, h.Amount, h.CreatedAt })
                .ToList();

            return Ok(new { IsSuccess = true, ResponseData = new { Outstanding = result, PendingHandovers = pendingHandovers } });
        }

        // PATCH /api/PaCashHandover/{id}/reject
        [HttpPatch("{id}/reject")]
        public IActionResult Reject(long id, [FromBody] PaCashHandoverDTO dto)
        {
            var handover = _db.PaCashHandovers.FirstOrDefault(h => h.Id == id);
            if (handover == null)
                return Ok(new { IsSuccess = false, Message = "Handover not found." });
            if (handover.Status != "Pending")
                return Ok(new { IsSuccess = false, Message = "Handover is not pending." });

            handover.Status = "Rejected";
            handover.RejectionNote = dto.RejectionNote;
            _db.SaveChanges();
            return Ok(new { IsSuccess = true, Message = "Handover rejected." });
        }
    }
}
