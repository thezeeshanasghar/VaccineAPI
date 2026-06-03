using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InvoiceAmendmentController : ControllerBase
    {
        private readonly Context _db;
        public InvoiceAmendmentController(Context db) { _db = db; }

        // GET /api/InvoiceAmendment/pending/{doctorId}
        // Returns all pending (unresolved) amendments for the doctor's reconciliation page.
        [HttpGet("pending/{doctorId}")]
        public IActionResult GetPending(long doctorId)
        {
            var clinicIds = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToList();

            var amendments = _db.InvoiceAmendments
                .Include(a => a.InvoiceSubmission)
                .Where(a =>
                    a.DoctorId == doctorId &&
                    !a.IsApprovedByDoctor &&
                    !a.IsRejectedByDoctor)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            var childIds = amendments.Select(a => a.InvoiceSubmission != null ? a.InvoiceSubmission.ChildId : 0).Distinct().ToList();
            var childNames = _db.Childs
                .Where(c => childIds.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.Name ?? "");

            var paIds = amendments.Select(a => a.PaId).Distinct().ToList();
            var paNames = _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.Name ?? "");

            var result = amendments.Select(a => new
            {
                AmendmentId        = a.Id,
                InvoiceSubmissionId = a.InvoiceSubmissionId,
                AmendmentType      = a.AmendmentType,
                OldAmount          = a.OldAmount,
                NewAmount          = a.NewAmount,
                PaId               = a.PaId,
                PaName             = paNames.ContainsKey(a.PaId) ? paNames[a.PaId] : "",
                Date               = a.CreatedAt.ToString("yyyy-MM-dd"),
                PatientName        = a.InvoiceSubmission != null && childNames.ContainsKey(a.InvoiceSubmission.ChildId)
                                        ? childNames[a.InvoiceSubmission.ChildId] : "",
                Notes              = a.Notes
            });

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // PATCH /api/InvoiceAmendment/{id}/approve
        [HttpPatch("{id}/approve")]
        public IActionResult Approve(long id, [FromQuery] long doctorId)
        {
            var amendment = _db.InvoiceAmendments
                .Include(a => a.InvoiceSubmission)
                .FirstOrDefault(a => a.Id == id);

            if (amendment == null)
                return Ok(new { IsSuccess = false, Message = "Amendment not found." });
            if (amendment.IsApprovedByDoctor || amendment.IsRejectedByDoctor)
                return Ok(new { IsSuccess = false, Message = "Amendment already resolved." });

            amendment.IsApprovedByDoctor = true;
            amendment.ApprovedAt = DateTime.UtcNow;
            amendment.ApprovedByDoctorId = doctorId;

            var inv = amendment.InvoiceSubmission;
            if (inv != null)
            {
                if (amendment.AmendmentType == "Ungive")
                {
                    // Doctor accepts the ungive — invoice cancelled, PA owes nothing for this invoice
                    inv.TotalAmount = 0;
                    inv.InvoiceStatus = "Cancelled";
                }
                else if (amendment.AmendmentType == "Edit")
                {
                    // Doctor accepts the edit — PA's payable becomes the new (edited) amount
                    inv.TotalAmount = amendment.NewAmount;
                    inv.ConsultationFee = inv.ConsultationFee; // unchanged
                    inv.InvoiceStatus = "Active";
                }
                inv.HasPendingAmendment = false;
                _db.Entry(inv).State = EntityState.Modified;
            }

            _db.Entry(amendment).State = EntityState.Modified;
            _db.SaveChanges();

            return Ok(new { IsSuccess = true, Message = "Amendment approved." });
        }

        // PATCH /api/InvoiceAmendment/{id}/reject
        [HttpPatch("{id}/reject")]
        public IActionResult Reject(long id, [FromQuery] long doctorId, [FromBody] RejectAmendmentDTO dto)
        {
            var amendment = _db.InvoiceAmendments
                .Include(a => a.InvoiceSubmission)
                .FirstOrDefault(a => a.Id == id);

            if (amendment == null)
                return Ok(new { IsSuccess = false, Message = "Amendment not found." });
            if (amendment.IsApprovedByDoctor || amendment.IsRejectedByDoctor)
                return Ok(new { IsSuccess = false, Message = "Amendment already resolved." });

            amendment.IsRejectedByDoctor = true;
            amendment.RejectedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dto?.Notes))
                amendment.Notes = (amendment.Notes ?? "") + " | Rejection reason: " + dto.Notes;

            var inv = amendment.InvoiceSubmission;
            if (inv != null)
            {
                // For both Ungive and Edit rejections:
                // PA payable stays at OldAmount — PA still owes the full original amount.
                inv.TotalAmount = amendment.OldAmount;
                inv.InvoiceStatus = "Active";
                inv.HasPendingAmendment = false;
                _db.Entry(inv).State = EntityState.Modified;
            }

            _db.Entry(amendment).State = EntityState.Modified;
            _db.SaveChanges();

            return Ok(new { IsSuccess = true, Message = "Amendment rejected. PA payable remains unchanged." });
        }
    }

    public class RejectAmendmentDTO
    {
        public string? Notes { get; set; }
    }
}
