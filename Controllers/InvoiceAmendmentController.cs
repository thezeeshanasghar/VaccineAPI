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

            var paIds = amendments.Where(a => a.PaId.HasValue).Select(a => a.PaId!.Value).Distinct().ToList();
            var paNames = _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.Name ?? "");

            var managerIds = amendments.Where(a => a.ManagerId.HasValue).Select(a => a.ManagerId!.Value).Distinct().ToList();
            var managerNames = _db.Manager
                .Where(m => managerIds.Contains(m.Id))
                .ToDictionary(m => m.Id, m => m.Name ?? "");

            var result = amendments.Select(a => new
            {
                AmendmentId        = a.Id,
                InvoiceSubmissionId = a.InvoiceSubmissionId,
                AmendmentType      = a.AmendmentType,
                OldAmount          = a.OldAmount,
                NewAmount          = a.NewAmount,
                PaId               = a.PaId,
                PaName             = a.ManagerId.HasValue
                                        ? "Manager/(" + (managerNames.ContainsKey(a.ManagerId.Value) ? managerNames[a.ManagerId.Value] : "") + ")"
                                        : (a.PaId.HasValue && paNames.ContainsKey(a.PaId.Value) ? paNames[a.PaId.Value] : ""),
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
                    // Doctor accepts the ungive — invoice cancelled, PA owes nothing for this invoice.
                    // A cancelled invoice can never reach ScheduleController.ConfirmInvoice again (that
                    // endpoint only ever acts on an invoice a doctor is confirming receipt of cash on —
                    // there's no cash left to confirm here), so without closing the assignment here too,
                    // it would sit open forever with nothing to reach it: the same stuck-assignment class
                    // as the 2026-09-18 ConfirmInvoice sync-gap incident, just via a voided-invoice door
                    // instead of a stale-FK door.
                    inv.TotalAmount = 0;
                    inv.InvoiceStatus = "Cancelled";

                    var linkedAssignment = _db.PAAssignments
                        .Where(a => a.InvoiceSubmissionId == inv.Id && !a.IsCancelled)
                        .OrderByDescending(a => a.AssignedAt)
                        .FirstOrDefault();
                    if (linkedAssignment != null && !linkedAssignment.IsCashConfirmedByDoctor)
                    {
                        linkedAssignment.IsCashConfirmedByDoctor = true;
                        linkedAssignment.CashConfirmedAt = DateTime.UtcNow;
                        if (!linkedAssignment.IsCompleted)
                        {
                            linkedAssignment.IsCompleted = true;
                            linkedAssignment.CompletedAt = DateTime.UtcNow;
                        }
                        _db.Entry(linkedAssignment).State = EntityState.Modified;
                    }
                }
                else if (amendment.AmendmentType == "Edit")
                {
                    // Doctor accepts the edit — PA's payable becomes the new (edited) amount.
                    // Invoice stays Active and unconfirmed: the doctor still owes a real, separate
                    // ConfirmInvoice action to receive this (revised) amount — the assignment must
                    // stay open, not close here.
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
