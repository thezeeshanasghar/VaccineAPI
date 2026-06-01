using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        // Batch version: returns a dictionary keyed by (PaId, ClinicId)
        // One query for collections, one for handovers — no N+1.
        private Dictionary<(long PaId, long ClinicId), decimal> BatchCashInHand(
            IEnumerable<(long PaId, long ClinicId)> pairs,
            List<long> clinicIds)
        {
            var paIds = pairs.Select(p => p.PaId).Distinct().ToList();

            var collected = _db.Schedules
                .Include(s => s.Child)
                .Where(s => (paIds.Contains(s.GivenByPaId ?? 0) || paIds.Contains(s.PaymentCollectorPaId ?? 0))
                         && s.IsDone == true
                         && s.PaymentMode == "Cash"
                         && clinicIds.Contains(s.Child.ClinicId)
                         && s.Amount != null)
                .GroupBy(s => new
                {
                    PaId     = s.PaymentCollectorPaId.HasValue ? s.PaymentCollectorPaId.Value : s.GivenByPaId.Value,
                    ClinicId = s.Child.ClinicId
                })
                .Select(g => new { g.Key.PaId, g.Key.ClinicId, Total = g.Sum(s => (decimal?)s.Amount) ?? 0m })
                .ToList();

            var handedOver = _db.PaCashHandovers
                .Where(h => paIds.Contains(h.PaId) && clinicIds.Contains(h.ClinicId) && h.Status == "Confirmed")
                .GroupBy(h => new { h.PaId, h.ClinicId })
                .Select(g => new { g.Key.PaId, g.Key.ClinicId, Total = g.Sum(h => (decimal?)h.Amount) ?? 0m })
                .ToList();

            var result = new Dictionary<(long, long), decimal>();
            foreach (var pair in pairs)
            {
                var c = collected.FirstOrDefault(x => x.PaId == pair.PaId && x.ClinicId == pair.ClinicId);
                var h = handedOver.FirstOrDefault(x => x.PaId == pair.PaId && x.ClinicId == pair.ClinicId);
                result[(pair.PaId, pair.ClinicId)] = (c?.Total ?? 0m) - (h?.Total ?? 0m);
            }
            return result;
        }

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

            // Batch cash-in-hand computation
            var pairs = handovers.Select(h => (h.PaId, h.ClinicId)).Distinct().ToList();
            var cashInHandMap = BatchCashInHand(pairs, clinicIds);

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
                CashInHand = cashInHandMap.ContainsKey((h.PaId, h.ClinicId)) ? cashInHandMap[(h.PaId, h.ClinicId)] : 0m
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

            // Notify PA by email (fire-and-forget)
            var pa = _db.PersonalAssistant.FirstOrDefault(p => p.Id == handover.PaId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                var reason = !string.IsNullOrEmpty(dto.RejectionNote) ? dto.RejectionNote : "No reason given";
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    $"Hi {pa.Name},<br><br>Your cash handover of <b>Rs. {handover.Amount:N0}</b> has been <b>rejected</b>.<br>Reason: {reason}<br><br>Please re-submit the handover after resolving the issue.",
                    "Cash Handover Rejected"
                ));
            }

            return Ok(new { IsSuccess = true, Message = "Handover rejected." });
        }

        // GET /api/PaCashHandover/daily-summary/{doctorId}?date=YYYY-MM-DD
        // CashTotal / OnlineTotal reflect that date only.
        // LifetimeHandedOver and PendingCash reflect cumulative lifetime balance (correct for running ledger).
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

            var paRows = rows.Where(s => s.PaymentCollectorPaId.HasValue).ToList();
            var paIds = paRows.Select(s => s.PaymentCollectorPaId.Value).Distinct().ToList();
            var paNames = _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.Name);

            // Batch lifetime handover totals for all PA-clinic pairs
            var paClinics = paRows
                .Select(s => (PaId: s.PaymentCollectorPaId.Value, ClinicId: s.Child.ClinicId))
                .Distinct().ToList();
            var cashInHandMap = BatchCashInHand(paClinics, clinicIds);

            // Batch lifetime confirmed handovers per PA across all their clinics
            var lifetimeHandedOver = _db.PaCashHandovers
                .Where(h => paIds.Contains(h.PaId) && clinicIds.Contains(h.ClinicId) && h.Status == "Confirmed")
                .GroupBy(h => h.PaId)
                .Select(g => new { PaId = g.Key, Total = g.Sum(h => (decimal?)h.Amount) ?? 0m })
                .ToDictionary(x => x.PaId, x => x.Total);

            var summary = paIds.Select(paId =>
            {
                var paSchedules = paRows.Where(s => s.PaymentCollectorPaId == paId).ToList();
                var firstClinicId = paSchedules.FirstOrDefault()?.Child?.ClinicId ?? 0;
                var cashTotal = paSchedules
                    .Where(s => s.PaymentMode == "Cash")
                    .Sum(s => (decimal?)s.Amount) ?? 0m;
                var onlineTotal = paSchedules
                    .Where(s => s.PaymentMode != "Cash")
                    .Sum(s => (decimal?)s.Amount) ?? 0m;
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
                    // Labelled "Lifetime" so callers know this is not today-only
                    LifetimeHandedOver = lifetimeHandedOver.ContainsKey(paId) ? lifetimeHandedOver[paId] : 0m,
                    PendingCash = cashInHandMap.ContainsKey((paId, firstClinicId)) ? cashInHandMap[(paId, firstClinicId)] : 0m,
                    Schedules = breakdown
                };
            }).ToList();

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
                    LifetimeHandedOver = 0m,
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
        [HttpGet("outstanding/{doctorId}")]
        public IActionResult GetOutstanding(long doctorId)
        {
            var clinics = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .ToList();
            var clinicIds = clinics.Select(c => c.Id).ToList();

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

            // Batch cash-in-hand for all pairs
            var pairs = paClinics.Select(x => (x.PaId, x.ClinicId)).Distinct().ToList();
            var cashInHandMap = BatchCashInHand(pairs, clinicIds);

            var result = paClinics
                .Select(pc => new {
                    PaId = pc.PaId,
                    PaName = paNames.ContainsKey(pc.PaId) ? paNames[pc.PaId] : "",
                    ClinicId = pc.ClinicId,
                    ClinicName = clinicMap.ContainsKey(pc.ClinicId) ? clinicMap[pc.ClinicId] : "",
                    PendingCash = cashInHandMap.ContainsKey((pc.PaId, pc.ClinicId)) ? cashInHandMap[(pc.PaId, pc.ClinicId)] : 0m
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

        // GET /api/PaCashHandover/reconciliation/{doctorId}
        // One row per downloaded invoice (InvoiceSubmission) where a PA submitted it.
        [HttpGet("reconciliation/{doctorId}")]
        public IActionResult GetReconciliation(
            long doctorId,
            [FromQuery] long? clinicId = null,
            [FromQuery] long? paId = null,
            [FromQuery] string fromDate = null,
            [FromQuery] string toDate = null)
        {
            DateTime? from = null;
            DateTime? to   = null;
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fd)) from = fd.Date;
            if (!string.IsNullOrEmpty(toDate)   && DateTime.TryParse(toDate,   out var td)) to   = td.Date.AddDays(1);

            var clinicIds = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToList();

            var paNames = _db.PersonalAssistant
                .Select(p => new { p.Id, p.Name })
                .ToList()
                .ToDictionary(p => p.Id, p => p.Name);

            var clinicNames = _db.Clinics
                .Where(c => clinicIds.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.Name ?? "");

            // Source of truth: InvoiceSubmission — created when PA downloads invoice
            var invQuery = _db.InvoiceSubmissions
                .Where(i =>
                    i.PaId.HasValue &&
                    i.TotalAmount > 0 &&
                    i.ClinicId.HasValue &&
                    clinicIds.Contains(i.ClinicId.Value));

            if (clinicId.HasValue)
                invQuery = invQuery.Where(i => i.ClinicId == clinicId.Value);
            if (paId.HasValue)
                invQuery = invQuery.Where(i => i.PaId == paId.Value);
            if (from.HasValue)
                invQuery = invQuery.Where(i => i.InvoiceDate.Date >= from.Value);
            if (to.HasValue)
                invQuery = invQuery.Where(i => i.InvoiceDate.Date < to.Value);

            var invoices = invQuery.OrderByDescending(i => i.InvoiceDate).ToList();

            var childIds = invoices.Select(i => i.ChildId).Distinct().ToList();
            var childNames = _db.Childs
                .Where(c => childIds.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.Name ?? "");

            var result = invoices.Select(i => new {
                InvoiceSubmissionId = i.Id,
                ScheduleId          = i.Id,          // keep alias so existing frontend key works
                Date                = i.InvoiceDate.ToString("yyyy-MM-dd"),
                PatientName         = childNames.ContainsKey(i.ChildId) ? childNames[i.ChildId] : "",
                Vaccines            = "Invoice",
                Amount              = i.TotalAmount,
                PaymentMode         = "Cash",         // invoice-level; mode detail is on schedule rows
                IsConfirmed         = i.IsConfirmedByDoctor,
                ConfirmedAt         = i.ConfirmedAt.HasValue ? i.ConfirmedAt.Value.ToString("yyyy-MM-ddTHH:mm:ss") : (string)null,
                PaId                = i.PaId.Value,
                PaName              = paNames.ContainsKey(i.PaId.Value) ? paNames[i.PaId.Value] : "",
                ClinicId            = i.ClinicId.Value,
                ClinicName          = clinicNames.ContainsKey(i.ClinicId.Value) ? clinicNames[i.ClinicId.Value] : ""
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // GET /api/PaCashHandover/my-reconciliation/{paId}/{clinicId}
        // PA's own view: total invoiced, confirmed by doctor, still pending.
        [HttpGet("my-reconciliation/{paId}/{clinicId}")]
        public IActionResult GetMyReconciliation(long paId, long clinicId)
        {
            var invoices = _db.InvoiceSubmissions
                .Where(i =>
                    i.PaId == paId &&
                    i.ClinicId == clinicId &&
                    i.TotalAmount > 0)
                .OrderByDescending(i => i.InvoiceDate)
                .ToList();

            var childIds = invoices.Select(i => i.ChildId).Distinct().ToList();
            var childNames = _db.Childs
                .Where(c => childIds.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.Name ?? "");

            var rows = invoices.Select(i => new {
                InvoiceSubmissionId = i.Id,
                Date                = i.InvoiceDate.ToString("yyyy-MM-dd"),
                PatientName         = childNames.ContainsKey(i.ChildId) ? childNames[i.ChildId] : "",
                Amount              = i.TotalAmount,
                IsConfirmed         = i.IsConfirmedByDoctor
            }).ToList();

            var totalCollected = invoices.Sum(i => i.TotalAmount);
            var totalConfirmed = invoices.Where(i => i.IsConfirmedByDoctor).Sum(i => i.TotalAmount);

            return Ok(new {
                IsSuccess    = true,
                ResponseData = new {
                    TotalCollected = totalCollected,
                    TotalConfirmed = totalConfirmed,
                    TotalPending   = totalCollected - totalConfirmed,
                    Rows           = rows
                }
            });
        }
    }
}
