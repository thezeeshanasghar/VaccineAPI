using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PAAssignmentController : ControllerBase
    {
        private readonly Context _db;
        private readonly VaccineAPI.Services.InventoryTransactionService _inventory;

        public PAAssignmentController(Context db, VaccineAPI.Services.InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        // GET /api/PAAssignment/pa/{paId}
        [HttpGet("pa/{paId}")]
        public async Task<IActionResult> GetByPA(long paId)
        {
            // Fetch raw assignments first — avoid EF Join+Where MySQL column misattribution bug
            var rawAssignments = await _db.PAAssignments
                .Where(a => a.PersonalAssistantId == paId && !a.IsCompleted && !a.IsCancelled)
                .ToListAsync();

            var childIds = rawAssignments.Select(a => a.ChildId).Distinct().ToList();
            var children = childIds.Any()
                ? await _db.Childs.Include(c => c.User).Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id)
                : new Dictionary<long, VaccineAPI.Models.Child>();

            // Look up each assignment's linked invoice directly via the InvoiceSubmissionId FK —
            // no date/PaId guessing. The FK is written once, at the moment the link is actually
            // known (PAAssignmentController.Create or ScheduleController's
            // SyncInvoicePaToActiveAssignment), so reading it here is exact: it can never surface
            // — or steal — a different PA's unrelated invoice for the same child.
            var invoiceIds = rawAssignments
                .Select(a => a.InvoiceSubmissionId)
                .Where(id => id.HasValue)
                .Select(id => id.GetValueOrDefault())
                .Distinct().ToList();
            var invoices = invoiceIds.Any()
                ? await _db.InvoiceSubmissions.Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id)
                : new Dictionary<long, InvoiceSubmission>();

            // Booking.Comments (parent) / DoctorComment for assignments that originated from
            // a booking — read-only reference for the PA, same fields already shown together
            // on the doctor's own Bookings page.
            var bookingIds = rawAssignments
                .Select(a => a.BookingId)
                .Where(id => id.HasValue)
                .Select(id => id.GetValueOrDefault())
                .Distinct().ToList();
            var bookings = bookingIds.Any()
                ? await _db.Bookings.Where(b => bookingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id)
                : new Dictionary<long, Booking>();

            // Enrich each assignment with child info, schedules, and invoice
            var result = rawAssignments.Select(a =>
            {
                var child = children.ContainsKey(a.ChildId) ? children[a.ChildId] : null;

                // PAAssignmentSchedule is the single source of truth for "which Schedule rows
                // does this assignment cover" — no ChildId/date/PaymentCollectorPaId inference.
                // Not filtered to IsDone == true: a schedule can be pinned here before the PA
                // has actually given it yet (assign-time auto-include of undone doses).
                var scheduleIds = _db.PAAssignmentSchedules
                    .Where(l => l.AssignmentId == a.Id)
                    .Select(l => l.ScheduleId);

                var schedules = _db.Schedules
                    .Where(s => scheduleIds.Contains(s.Id))
                    .Join(_db.Doses,
                        s => s.DoseId,
                        d => d.Id,
                        (s, d) => new
                        {
                            s.Id,
                            DoseName           = d.Name,
                            s.Date,
                            s.IsDone,
                            s.IsPaymentCollected,
                            s.Amount,
                            s.Weight,
                            s.Height,
                            s.Circle,
                            s.PaymentCollectorPaId,
                            s.GivenByPaId
                        })
                    .ToList();

                var invoice = a.InvoiceSubmissionId.HasValue && invoices.ContainsKey(a.InvoiceSubmissionId.Value)
                    ? invoices[a.InvoiceSubmissionId.Value]
                    : null;

                var booking = a.BookingId.HasValue && bookings.ContainsKey(a.BookingId.Value)
                    ? bookings[a.BookingId.Value]
                    : null;

                return new
                {
                    AssignmentId  = a.Id,
                    a.AssignedAt,
                    a.Notes,
                    a.TargetDate,
                    ChildId       = a.ChildId,
                    Name          = child != null ? child.Name      : "",
                    Gender        = child != null ? child.Gender    : "",
                    DOB           = child != null ? child.DOB       : (DateTime?)null,
                    FatherName    = child != null ? child.FatherName: "",
                    ParentMobile  = child?.User != null ? child.User.MobileNumber : "",
                    // Same normalizer NotifyParentOfAssignment already uses for the parent-facing
                    // notification, reused here so the PA can tap straight into WhatsApp with the
                    // parent instead of a 4th hand-rolled formatter. Empty string if unusable —
                    // frontend hides the chip in that case.
                    ParentWhatsApp = child?.User != null ? ToWhatsAppNumber(child.User.MobileNumber, child.User.CountryCode) : "",
                    a.IsAutoCreated,
                    a.AssignmentStatus,
                    InvoiceAmount = invoice != null ? invoice.TotalAmount : 0m,
                    HasInvoice    = invoice != null,
                    BookingId     = a.BookingId,
                    ParentComment = booking?.Comments ?? "",
                    DoctorComment = booking?.DoctorComment ?? "",
                    Schedules     = schedules
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // DELETE /api/PAAssignment/{id}?doctorId={doctorId}&mode={UnassignOnly|FullReset}
        // Doctor-facing removal of an assignment row, with two modes:
        //   UnassignOnly (default) — removes only the PAAssignment row. The patient's
        //     vaccine/payment records and the invoice are left completely untouched.
        //   FullReset — also deletes the invoice (and any amendments) and resets the
        //     schedules this PA gave/collected payment for on this child back to
        //     "not given", crediting any consumed stock back via the inventory ledger.
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAssignment(long id, [FromQuery] long doctorId, [FromQuery] string mode = "UnassignOnly")
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });
            if (assignment.DoctorId != doctorId)
                return Ok(new { IsSuccess = false, Message = "Not authorised" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (mode == "FullReset")
                {
                    var paId = assignment.PersonalAssistantId;
                    var childId = assignment.ChildId;

                    var invoiceIds = await _db.InvoiceSubmissions
                        .Where(i => i.ChildId == childId && i.PaId == paId)
                        .Select(i => i.Id)
                        .ToListAsync();

                    if (invoiceIds.Count > 0)
                    {
                        var amendments = _db.InvoiceAmendments.Where(am => invoiceIds.Contains(am.InvoiceSubmissionId));
                        _db.InvoiceAmendments.RemoveRange(amendments);

                        var invoices = _db.InvoiceSubmissions.Where(i => invoiceIds.Contains(i.Id));
                        _db.InvoiceSubmissions.RemoveRange(invoices);
                    }

                    var schedules = await _db.Schedules
                        .Where(s => s.ChildId == childId && s.PaymentCollectorPaId == paId)
                        .ToListAsync();

                    var inventoryEnabled = IsInventoryEnabledForDoctor(doctorId);

                    // The child's own clinic is the reliable source of truth for where a dose was
                    // actually given — assignment.ClinicId can be null or stale (e.g. the PA was
                    // reassigned, or the field was never backfilled), which silently pointed the
                    // BrandAmount lookup below at the wrong clinic and swallowed the restore.
                    var childClinicId = await _db.Childs
                        .Where(c => c.Id == childId)
                        .Select(c => c.ClinicId)
                        .FirstOrDefaultAsync();

                    foreach (var s in schedules)
                    {
                        if (inventoryEnabled && s.IsDone == true && s.BrandId.HasValue)
                        {
                            var brandInventory = _db.BrandAmounts
                                .Where(b => b.BrandId == s.BrandId && b.DoctorId == doctorId && b.ClinicId == childClinicId)
                                .FirstOrDefault();

                            if (brandInventory == null)
                            {
                                // Never silently drop a stock restore — that leaves the dose
                                // reversed with nothing credited back, a permanent invisible gap.
                                await tx.RollbackAsync();
                                return Ok(new
                                {
                                    IsSuccess = false,
                                    Message = $"Inventory row not found for brand {s.BrandId} at clinic {childClinicId} — cannot safely reset schedule {s.Id}."
                                });
                            }

                            _inventory.UnadministerSync(doctorId, childClinicId, s.BrandId.Value, s.Id, s.GivenDate ?? DateTime.Today, paId);
                        }

                        s.IsDone = false;
                        s.GivenDate = null;
                        s.DoneAt = null;
                        s.GivenByPaId = null;
                        s.PaymentMode = "Cash";
                        s.OnlineService = null;
                        s.IsPaymentApproved = false;
                        s.BrandId = null;
                        s.Amount = null;
                        s.PaymentCollectorPaId = null;
                        s.IsPaymentCollected = false;
                        s.IsSkip = false;
                        s.SkippedByPaId = null;
                        s.SkippedAt = null;
                    }
                }

                _db.PAAssignments.Remove(assignment);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return Ok(new { IsSuccess = true });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        private bool IsInventoryEnabledForDoctor(long doctorId)
        {
            var doctorAllowInventory = _db.Doctors
                .Where(d => d.Id == doctorId)
                .Select(d => (bool?)d.AllowInventory)
                .FirstOrDefault();

            return doctorAllowInventory ?? false;
        }

        // POST /api/PAAssignment/{id}/complete
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(long id, [FromQuery] long? paId = null)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is already cancelled" });

            // Ownership check: PA can only complete their own assignment
            if (paId.HasValue && assignment.PersonalAssistantId != paId.Value)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to complete this assignment" });

            // Payment gate: all schedules pinned to this assignment with a non-zero amount
            // must have payment recorded — exact via PAAssignmentSchedule, no date window.
            var unpaid = _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == assignment.Id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .Where(s => !s.IsPaymentCollected && s.Amount > 0)
                .Join(_db.Doses, s => s.DoseId, d => d.Id, (s, d) => d.Name)
                .ToList();

            if (unpaid.Any())
                return Ok(new { IsSuccess = false, Message = "Please record payment mode before completing. Unpaid vaccines: " + string.Join(", ", unpaid) });

            assignment.IsCompleted  = true;
            assignment.CompletedAt  = DateTime.UtcNow;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true });
        }

        // PATCH /api/PAAssignment/{id}/cancel
        [HttpPatch("{id}/cancel")]
        public async Task<IActionResult> Cancel(long id, [FromBody] CancelAssignmentDto dto)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Assignment is already completed" });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is already cancelled" });

            // PA can only cancel their own; doctor can cancel any under their DoctorId
            if (dto.CallerType == "PA" && assignment.PersonalAssistantId != dto.CallerId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to cancel this assignment" });

            if (dto.CallerType == "DOCTOR" && assignment.DoctorId != dto.CallerId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to cancel this assignment" });

            // PA cannot self-cancel once vaccines were given or payment was recorded — must ask the doctor
            if (dto.CallerType == "PA")
            {
                var hasGivenOrPaid = await _db.PAAssignmentSchedules
                    .Where(l => l.AssignmentId == assignment.Id)
                    .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                    .AnyAsync(s => s.IsDone == true || s.IsPaymentCollected == true);

                if (hasGivenOrPaid)
                    return BadRequest(new { IsSuccess = false, Message = "This assignment has vaccines given or payment recorded and can no longer be self-cancelled. Please contact the doctor." });
            }

            assignment.IsCancelled  = true;
            assignment.CancelledAt  = DateTime.UtcNow;
            assignment.CancelReason = dto.Reason;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Notify doctor by email (fire-and-forget)
            if (dto.CallerType == "PA")
            {
                var doctor = await _db.Doctors.FindAsync(assignment.DoctorId);
                if (doctor != null && !string.IsNullOrEmpty(doctor.Email))
                {
                    var paUser = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                    var paNameStr    = paUser?.Name ?? "Your PA";
                    var child        = await _db.Childs.FindAsync(assignment.ChildId);
                    var childNameStr = child?.Name ?? "a patient";
                    var reasonStr    = dto.Reason ?? "No reason given";
                    _ = Task.Run(() => UserEmail.SendEmail(
                        doctor.Email,
                        $"{paNameStr} has cancelled the assignment for patient {childNameStr}. Reason: {reasonStr}. Please reassign or reschedule.",
                        "PA Assignment Cancelled"
                    ));
                }
            }

            return Ok(new { IsSuccess = true });
        }

        // PATCH /api/PAAssignment/{id}/request-cancel
        // PA-only: clean assignment cancel becomes a pending request requiring doctor approval (no instant cancel)
        [HttpPatch("{id}/request-cancel")]
        public async Task<IActionResult> RequestCancel(long id, [FromBody] CancelAssignmentDto dto)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Assignment is already completed" });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is already cancelled" });

            if (assignment.PersonalAssistantId != dto.CallerId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to cancel this assignment" });

            if (assignment.AssignmentStatus == "PendingCancellation")
                return Ok(new { IsSuccess = false, Message = "Cancellation already requested — awaiting doctor approval" });

            // Belt-and-suspenders — frontend already blocks this, but guard here too (same rule as instant Cancel)
            var hasGivenOrPaid = await _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == assignment.Id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .AnyAsync(s => s.IsDone == true || s.IsPaymentCollected == true);

            if (hasGivenOrPaid)
                return BadRequest(new { IsSuccess = false, Message = "This assignment has vaccines given or payment recorded and can no longer be self-cancelled. Please contact the doctor." });

            assignment.AssignmentStatus   = "PendingCancellation";
            assignment.CancelRequestedAt  = DateTime.UtcNow;
            assignment.CancelRequestReason = dto.Reason;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Notify doctor by email (fire-and-forget)
            var doctor = await _db.Doctors.FindAsync(assignment.DoctorId);
            if (doctor != null && !string.IsNullOrEmpty(doctor.Email))
            {
                var paUser = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                var paNameStr    = paUser?.Name ?? "Your PA";
                var child        = await _db.Childs.FindAsync(assignment.ChildId);
                var childNameStr = child?.Name ?? "a patient";
                var reasonStr    = dto.Reason ?? "No reason given";
                _ = Task.Run(() => UserEmail.SendEmail(
                    doctor.Email,
                    $"{paNameStr} has requested to cancel the assignment for patient {childNameStr}. Reason: {reasonStr}. Please review and approve or reject this request in the VacDoc app.",
                    "PA Cancellation Request — Approval Needed"
                ));
            }

            return Ok(new { IsSuccess = true });
        }

        // GET /api/PAAssignment/pending-cancellations/{doctorId}
        [HttpGet("pending-cancellations/{doctorId}")]
        public async Task<IActionResult> GetPendingCancellations(long doctorId)
        {
            var pending = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId && a.AssignmentStatus == "PendingCancellation" && !a.IsCancelled)
                .OrderByDescending(a => a.CancelRequestedAt)
                .ToListAsync();

            var childIds = pending.Select(a => a.ChildId).Distinct().ToList();
            var childNames = await _db.Childs
                .Where(c => childIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name ?? "");

            var paIds = pending.Select(a => a.PersonalAssistantId).Distinct().ToList();
            var paNames = await _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? "");

            var result = pending.Select(a => new
            {
                AssignmentId       = a.Id,
                ChildId            = a.ChildId,
                ChildName          = childNames.ContainsKey(a.ChildId) ? childNames[a.ChildId] : "",
                PaId               = a.PersonalAssistantId,
                PaName             = paNames.ContainsKey(a.PersonalAssistantId) ? paNames[a.PersonalAssistantId] : "",
                CancelRequestedAt  = a.CancelRequestedAt,
                CancelRequestReason = a.CancelRequestReason
            });

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // PATCH /api/PAAssignment/{id}/approve-cancel
        [HttpPatch("{id}/approve-cancel")]
        public async Task<IActionResult> ApproveCancelRequest(long id, [FromQuery] long doctorId)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.DoctorId != doctorId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to act on this assignment" });

            if (assignment.AssignmentStatus != "PendingCancellation" || assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "This cancellation request has already been resolved" });

            assignment.IsCancelled  = true;
            assignment.CancelledAt  = DateTime.UtcNow;
            assignment.CancelReason = assignment.CancelRequestReason;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Cancellation approved" });
        }

        // PATCH /api/PAAssignment/{id}/reject-cancel
        [HttpPatch("{id}/reject-cancel")]
        public async Task<IActionResult> RejectCancelRequest(long id, [FromBody] RejectCancelDto dto)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.DoctorId != dto.DoctorId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to act on this assignment" });

            if (assignment.AssignmentStatus != "PendingCancellation" || assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "This cancellation request has already been resolved" });

            assignment.AssignmentStatus = "Active";
            assignment.RejectionNote    = dto.Notes;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Notify PA by email (fire-and-forget)
            var pa = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                var child = await _db.Childs.FindAsync(assignment.ChildId);
                var childNameStr = child?.Name ?? "the patient";
                var reason = !string.IsNullOrEmpty(dto.Notes) ? dto.Notes : "No reason given";
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    $"Hi {pa.Name},<br><br>Your request to cancel the assignment for patient <b>{childNameStr}</b> has been <b>rejected</b>.<br>Reason: {reason}<br><br>The assignment remains active.",
                    "Cancellation Request Rejected"
                ));
            }

            return Ok(new { IsSuccess = true, Message = "Cancellation request rejected" });
        }

        // PATCH /api/PAAssignment/{id}/reassign
        // dto.RequestingPaId: set when a manager-tier PA (not the doctor) is initiating the
        // reassign from the shared PA Assignment Tracking view. Requires ReassignPaTask, and
        // the assignment's own clinic must be one the manager PA has PaAccess to — same fence
        // as GetForDoctor's requestingPaId, so a manager PA can't reassign an assignment they
        // weren't even allowed to see.
        [HttpPatch("{id}/reassign")]
        public async Task<IActionResult> Reassign(long id, [FromBody] ReassignDto dto)
        {
            var old = await _db.PAAssignments.FindAsync(id);
            if (old == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (dto.RequestingPaId.HasValue)
            {
                var managerPerm = await _db.PaPermissions.FirstOrDefaultAsync(p => p.PaId == dto.RequestingPaId.Value);
                if (managerPerm == null || !managerPerm.ReassignPaTask)
                    return Forbid();

                if (old.ClinicId.HasValue)
                {
                    var hasAccess = await _db.PaAccess.AnyAsync(a =>
                        a.PersonalAssistantId == dto.RequestingPaId.Value && a.ClinicId == old.ClinicId.Value);
                    if (!hasAccess)
                        return Forbid();
                }
            }

            if (old.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Cannot reassign a completed assignment" });

            if (old.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Cannot reassign a cancelled assignment" });

            if (old.AssignmentStatus == "PendingCancellation")
                return Ok(new { IsSuccess = false, Message = "This assignment has a pending cancellation request — resolve it before reassigning." });

            // Cancel the old one
            old.IsCancelled  = true;
            old.CancelledAt  = DateTime.UtcNow;
            old.CancelReason = "Reassigned to another PA";

            // Create new assignment for the new PA — carries the old assignment's
            // InvoiceSubmissionId FK forward directly (this assignment's invoice, no guessing)
            var newAssignment = new PAAssignment
            {
                DoctorId                    = old.DoctorId,
                ClinicId                    = old.ClinicId,
                PersonalAssistantId         = dto.NewPaId,
                ChildId                     = old.ChildId,
                Notes                       = !string.IsNullOrEmpty(dto.Notes) ? dto.Notes : old.Notes,
                AssignedAt                  = DateTime.UtcNow,
                IsCompleted                 = false,
                ReassignedFromAssignmentId  = old.Id,
                InvoiceSubmissionId         = old.InvoiceSubmissionId,
                TargetDate                  = dto.TargetDate ?? old.TargetDate,
                BookingId                   = old.BookingId
            };

            _db.PAAssignments.Add(newAssignment);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Move every PAAssignmentSchedule row from the old assignment to the new one —
            // exact FK move, no date-window guessing. This is what makes "which schedules
            // does this assignment cover" survive a reassignment unchanged.
            var linksToMove = await _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == old.Id)
                .ToListAsync();
            foreach (var link in linksToMove)
                link.AssignmentId = newAssignment.Id;
            if (linksToMove.Count > 0)
                await _db.SaveChangesAsync();

            // Move the linked invoice's PaId to the new PA — exact, via the FK just carried
            // forward above, not a date-window guess.
            if (old.InvoiceSubmissionId.HasValue)
            {
                var invoiceToMove = await _db.InvoiceSubmissions.FindAsync(old.InvoiceSubmissionId.Value);
                if (invoiceToMove != null)
                {
                    invoiceToMove.PaId = dto.NewPaId;
                    if (invoiceToMove.ClinicId == null && old.ClinicId.HasValue)
                        invoiceToMove.ClinicId = old.ClinicId;
                    _db.Entry(invoiceToMove).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }

            // Notify new PA by email (fire-and-forget)
            var pa = await _db.PersonalAssistant.FindAsync(dto.NewPaId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    "A patient has been assigned to you. Please log in to your VacDoc app to view your assignments.",
                    "New Patient Assignment"
                ));
            }

            // Notify the PARENT that their vaccine coordinator has changed. Fires on every
            // reassignment (not just first assignment) — the parent needs to know who is
            // coordinating now. Same helper as Create; passes isReassignment for the wording.
            await NotifyParentOfAssignment(old.ChildId, dto.NewPaId, old.ClinicId, isReassignment: true);

            return Ok(new { IsSuccess = true, ResponseData = new { NewAssignmentId = newAssignment.Id } });
        }

        // Inserts an in-app PARENT notification announcing who is coordinating the child's
        // vaccines. Reused by Create (first assignment) and Reassign (PA swap). Phone is read
        // live from the PA's linked User (PersonalAssistant has no phone field of its own);
        // the PA photo is delivered via ProfileImage on the PA record and rendered by VacParent.
        private async Task NotifyParentOfAssignment(long childId, long paId, long? clinicId, bool isReassignment = false)
        {
            var child = await _db.Childs.Include(c => c.User)
                                        .FirstOrDefaultAsync(c => c.Id == childId);
            var assignedPa = await _db.PersonalAssistant.Include(p => p.User)
                                        .FirstOrDefaultAsync(p => p.Id == paId);
            if (child?.User == null || assignedPa == null)
                return;

            var paPhone = assignedPa.User?.MobileNumber ?? "";
            var paCountryCode = assignedPa.User?.CountryCode ?? "";
            var lead = isReassignment
                ? $"{assignedPa.Name} is now your vaccine companion for {child.Name}'s upcoming vaccines."
                : $"{assignedPa.Name} will be your vaccine companion for {child.Name}'s upcoming vaccines.";

            _db.Notifications.Add(new Notification
            {
                Type            = "PaAssigned",
                RecipientType   = "PARENT",
                RecipientId     = child.UserId,          // the parent's User.Id
                ChildId         = child.Id,
                ClinicId        = clinicId,
                Title           = isReassignment ? "Your vaccine companion has changed" : "Your vaccine companion",
                Message         = lead + (string.IsNullOrEmpty(paPhone) ? "" : $" You can reach them at {paPhone}."),
                PaName          = assignedPa.Name,
                PaPhone         = paPhone,
                PaPhoneWhatsApp = ToWhatsAppNumber(paPhone, paCountryCode),
                PaProfileImage  = assignedPa.ProfileImage,   // filename; VacParent prefixes RESOURCE_URL
                IsRead          = false,
                CreatedAt       = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }

        // Normalise a locally-stored mobile to wa.me international format: digits only, no
        // leading 0, country code prefixed once. Stored numbers here are typically local
        // ("03001234567") with CountryCode held separately ("92"). Returns "" if there's no
        // usable number so the frontend can hide the WhatsApp button.
        private static string ToWhatsAppNumber(string phone, string countryCode)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return "";

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            var cc = new string((countryCode ?? "").Where(char.IsDigit).ToArray());

            digits = digits.TrimStart('0');
            if (digits.Length == 0)
                return "";

            // Already prefixed with the country code (number stored in international form)?
            if (!string.IsNullOrEmpty(cc) && digits.StartsWith(cc))
                return digits;

            return (string.IsNullOrEmpty(cc) ? "" : cc) + digits;
        }

        // GET /api/PAAssignment/clinic/{clinicId}
        [HttpGet("clinic/{clinicId}")]
        public async Task<IActionResult> GetPAsForClinic(long clinicId)
        {
            // Fetch PaAccess rows first, then look up PAs in memory — avoids EF Join+Where MySQL alias bug
            var paIds = await _db.PaAccess
                .Where(a => a.ClinicId == clinicId)
                .Select(a => a.PersonalAssistantId)
                .Distinct()
                .ToListAsync();

            var pas = await _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id) && p.IsActive)
                .Select(p => new { p.Id, p.Name, p.Email, p.IsActive })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = pas });
        }

        // PATCH /api/PAAssignment/{id}/target-date
        // Doctor-only: set/change/clear the target date on an existing assignment — the
        // in-place edit from PA Assignment Tracking's "No date set" pill, for assignments
        // that were created without one (or need a correction) after the fact.
        [HttpPatch("{id}/target-date")]
        public async Task<IActionResult> SetTargetDate(long id, [FromBody] SetTargetDateDto dto)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (assignment.DoctorId != dto.DoctorId)
                return Ok(new { IsSuccess = false, Message = "Not authorised" });

            assignment.TargetDate = dto.TargetDate;

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true });
        }

        // POST /api/PAAssignment
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAssignmentDto dto)
        {
            try
            {
                var today = DateTime.UtcNow.AddHours(5).Date;

                var exists = await _db.PAAssignments.AnyAsync(a =>
                    a.ChildId == dto.ChildId &&
                    !a.IsCompleted &&
                    !a.IsCancelled &&
                    a.AssignedAt >= today && a.AssignedAt < today.AddDays(1));

                if (exists)
                {
                    var existingRow = await _db.PAAssignments
                        .Where(a => a.ChildId == dto.ChildId && !a.IsCompleted && !a.IsCancelled
                                    && a.AssignedAt >= today && a.AssignedAt < today.AddDays(1))
                        .FirstOrDefaultAsync();

                    var paName = "another PA";
                    if (existingRow != null)
                    {
                        var existingPa = await _db.PersonalAssistant.FindAsync(existingRow.PersonalAssistantId);
                        paName = existingPa?.Name ?? "another PA";
                    }
                    return Ok(new { IsSuccess = false, Message = $"This patient is already assigned to {paName} today. Cancel that assignment first or use Reassign." });
                }

                var assignment = new PAAssignment
                {
                    DoctorId            = dto.DoctorId,
                    ClinicId            = dto.ClinicId,
                    PersonalAssistantId = dto.PersonalAssistantId,
                    ChildId             = dto.ChildId,
                    Notes               = dto.Notes ?? "",
                    AssignedAt          = DateTime.UtcNow,
                    IsCompleted         = false,
                    IsCancelled         = false,
                    IsAutoCreated       = false,
                    TargetDate          = dto.TargetDate,
                    BookingId           = dto.BookingId
                };

                _db.PAAssignments.Add(assignment);
                await _db.SaveChangesAsync();

                // Pin the date-group's undone schedules to this assignment now, at creation
                // time — single source of truth for "which doses does this assignment cover."
                // No later re-inference; an extra dose given mid-visit gets appended by
                // ScheduleController's LinkScheduleToAssignment, not guessed here. Re-validated
                // server-side (ChildId + still-undone) rather than trusting the client's list
                // blindly, in case the popup state went stale between open and confirm.
                if (dto.ScheduleIds != null && dto.ScheduleIds.Count > 0)
                {
                    var validScheduleIds = await _db.Schedules
                        .Where(s => dto.ScheduleIds.Contains(s.Id) && s.ChildId == dto.ChildId && s.IsDone == false)
                        .Select(s => s.Id)
                        .ToListAsync();

                    foreach (var sid in validScheduleIds)
                    {
                        _db.PAAssignmentSchedules.Add(new PAAssignmentSchedule
                        {
                            AssignmentId = assignment.Id,
                            ScheduleId = sid
                        });
                    }
                    if (validScheduleIds.Count > 0)
                        await _db.SaveChangesAsync();
                }

                // Link this child's orphaned invoice (downloaded before any PA was assigned —
                // PaId == null) to the new assignment directly via the InvoiceSubmissionId FK.
                // No date guessing needed: a child can have at most one active assignment at a
                // time (enforced by the `exists` check above), so "the orphaned invoice for this
                // child" is already an unambiguous fact, not an inference. An invoice already
                // owned by a different PA (a separate, already-settled visit) is never touched.
                var orphanInvoice = _db.InvoiceSubmissions
                    .Where(i => i.ChildId == dto.ChildId && i.PaId == null)
                    .OrderByDescending(i => i.SubmittedAt)
                    .FirstOrDefault();
                if (orphanInvoice != null)
                {
                    var pa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
                    var paName = pa?.Name ?? "PA";
                    orphanInvoice.PaId = dto.PersonalAssistantId;
                    orphanInvoice.SubmittedByLabel = "Doctor/(" + paName + ")";
                    if (orphanInvoice.ClinicId == null && dto.ClinicId.HasValue)
                        orphanInvoice.ClinicId = dto.ClinicId;
                    assignment.InvoiceSubmissionId = orphanInvoice.Id;
                    _db.Entry(orphanInvoice).State = EntityState.Modified;
                    _db.Entry(assignment).State = EntityState.Modified;
                    await _db.SaveChangesAsync();

                    // Pin this invoice's own schedules too — covers the doctor-gave-it-then-
                    // assigned-a-PA ordering, where the dose was already done before the
                    // assignment existed and so was excluded from the undone-doses pinning
                    // above. Matched via Schedule.InvoiceSubmissionId, the FK stamped on exactly
                    // the doses submitted on this invoice — not by GivenDate falling on the
                    // invoice's InvoiceDate, which would also sweep in an unrelated same-day
                    // dose (e.g. one the parent declined and was never actually billed).
                    var schedulesOnInvoice = await _db.Schedules
                        .Where(s => s.ChildId == dto.ChildId
                                 && s.InvoiceSubmissionId == orphanInvoice.Id)
                        .ToListAsync();
                    foreach (var s in schedulesOnInvoice)
                    {
                        if (!_db.PAAssignmentSchedules.Any(l => l.AssignmentId == assignment.Id && l.ScheduleId == s.Id))
                            _db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = assignment.Id, ScheduleId = s.Id });
                        // Same backfill as SyncInvoicePaToActiveAssignment (ScheduleController.cs)
                        // — the money-icon check reads Schedule.PaymentCollectorPaId directly,
                        // which stays NULL forever from give-time unless backfilled here.
                        if (s.PaymentCollectorPaId != dto.PersonalAssistantId)
                        {
                            s.PaymentCollectorPaId = dto.PersonalAssistantId;
                            _db.Entry(s).State = EntityState.Modified;
                        }
                    }
                    if (schedulesOnInvoice.Count > 0)
                        await _db.SaveChangesAsync();
                }

                // Fire-and-forget email
                var newPa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
                if (newPa != null && !string.IsNullOrEmpty(newPa.Email))
                {
                    _ = Task.Run(() => UserEmail.SendEmail(
                        newPa.Email,
                        "A patient has been assigned to you. Please log in to your VacDoc app to view your assignments.",
                        "New Patient Assignment"
                    ));
                }

                // Notify the PARENT that a PA is now coordinating their child's vaccines.
                // In-app notification only (VacParent already polls parent notifications on
                // login). Kept synchronous — cheap DB row on the request's own _db context;
                // backgrounding it would race a disposed context.
                await NotifyParentOfAssignment(dto.ChildId, dto.PersonalAssistantId, dto.ClinicId);

                return Ok(new { IsSuccess = true, ResponseData = new { assignment.Id } });
            }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // PATCH /api/PAAssignment/{id}/mark-done
        // PA marks assignment as done (payment collected) — transitions to PendingHandover state.
        // Doctor sees this row on Payment Reconciliation with "Pending Handover" flag.
        [HttpPatch("{id}/mark-done")]
        public async Task<IActionResult> MarkDone(long id, [FromQuery] long? paId = null)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found." });

            if (assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is cancelled." });

            if (assignment.AssignmentStatus == "PendingHandover" || assignment.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Assignment is already marked done." });

            if (paId.HasValue && assignment.PersonalAssistantId != paId.Value)
                return Ok(new { IsSuccess = false, Message = "You are not authorised to update this assignment." });

            // Payment gate: PA must have collected payment on at least one schedule pinned to this assignment
            var hasPayment = _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == assignment.Id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .Any(s => s.IsPaymentCollected == true);

            if (!hasPayment)
                return Ok(new { IsSuccess = false, Message = "Please record payment mode before marking done." });

            assignment.AssignmentStatus = "PendingHandover";
            assignment.HandoverDoneAt   = DateTime.UtcNow;

            // Flag the linked InvoiceSubmission so it shows as PendingHandover on reconciliation
            var inv = assignment.InvoiceSubmissionId.HasValue
                ? await _db.InvoiceSubmissions.FindAsync(assignment.InvoiceSubmissionId.Value)
                : null;
            if (inv != null)
            {
                inv.PendingHandover = true;
                inv.HandoverDoneAt  = DateTime.UtcNow;
                _db.Entry(inv).State = EntityState.Modified;
            }

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok(new { IsSuccess = true, Message = "Assignment marked as done. Pending handover to doctor." });
        }

        // GET /api/PAAssignment/active/{doctorId}
        // Returns all active (non-completed, non-cancelled) assignments for a doctor today
        [HttpGet("active/{doctorId}")]
        public async Task<IActionResult> GetActiveForDoctor(long doctorId)
        {
            var today = DateTime.UtcNow.AddHours(5).Date;

            // Fetch raw assignment rows first, then enrich in memory to avoid EF join translation issues
            var raw = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId
                         && !a.IsCompleted
                         && !a.IsCancelled
                         && a.AssignedAt >= today && a.AssignedAt < today.AddDays(1))
                .ToListAsync();

            var childIds = raw.Select(a => a.ChildId).Distinct().ToList();
            var paIds    = raw.Select(a => a.PersonalAssistantId).Distinct().ToList();

            var children = childIds.Any()
                ? await _db.Childs.Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id)
                : new Dictionary<long, VaccineAPI.Models.Child>();

            var pas = paIds.Any()
                ? await _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id)
                : new Dictionary<long, VaccineAPI.Models.PersonalAssistant>();

            var assignments = raw.Select(a => new {
                AssignmentId = a.Id,
                a.AssignedAt,
                a.Notes,
                ChildId      = a.ChildId,
                ChildName    = children.ContainsKey(a.ChildId)             ? children[a.ChildId].Name       : "",
                Gender       = children.ContainsKey(a.ChildId)             ? children[a.ChildId].Gender     : "",
                DOB          = children.ContainsKey(a.ChildId)             ? children[a.ChildId].DOB        : (DateTime?)null,
                FatherName   = children.ContainsKey(a.ChildId)             ? children[a.ChildId].FatherName : "",
                PaId         = a.PersonalAssistantId,
                PaName       = pas.ContainsKey(a.PersonalAssistantId)      ? pas[a.PersonalAssistantId].Name : ""
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = assignments });
        }

        // GET /api/PAAssignment/doctor/{doctorId}?clinicId=&paId=&status=&fromDate=&toDate=&requestingPaId=
        // Doctor-facing tracking list — the gap GetByPA (PA-only, excludes completed) and
        // GetActiveForDoctor (today-only, unfiltered) both leave open. Scoped through the
        // doctor's own clinics (multi-clinic safe, same pattern as
        // PaCashHandoverController.GetReconciliation), with clinic/PA/status/date-range
        // filters all optional. status defaults to "Active" to match the doctor's usual
        // "what's outstanding" view; pass "All" for full history.
        //
        // requestingPaId: set when a manager-tier PA (not the doctor) is calling this same
        // endpoint from the shared PA Assignment Tracking view. Requires ViewPaAssignmentStatus
        // and — unlike the doctor's own unrestricted view — further fences results down to only
        // clinics in that PA's own PaAccess rows, so a manager PA can never see assignments at a
        // clinic they don't personally have access to, even though the doctor who owns all of
        // them can.
        [HttpGet("doctor/{doctorId}")]
        public async Task<IActionResult> GetForDoctor(
            long doctorId,
            [FromQuery] long? clinicId = null,
            [FromQuery] long? paId = null,
            [FromQuery] string status = "Active",
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] long? requestingPaId = null)
        {
            var clinicIds = await _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToListAsync();

            if (requestingPaId.HasValue)
            {
                var managerPerm = await _db.PaPermissions.FirstOrDefaultAsync(p => p.PaId == requestingPaId.Value);
                if (managerPerm == null || !managerPerm.ViewPaAssignmentStatus)
                    return Forbid();

                var managerClinicIds = await _db.PaAccess
                    .Where(a => a.PersonalAssistantId == requestingPaId.Value)
                    .Select(a => a.ClinicId)
                    .ToListAsync();
                clinicIds = clinicIds.Where(id => managerClinicIds.Contains(id)).ToList();
            }

            var query = _db.PAAssignments.Where(a =>
                a.DoctorId == doctorId &&
                (a.ClinicId == null ? !requestingPaId.HasValue : clinicIds.Contains(a.ClinicId.Value)));

            if (clinicId.HasValue)
                query = query.Where(a => a.ClinicId == clinicId.Value);

            if (paId.HasValue)
                query = query.Where(a => a.PersonalAssistantId == paId.Value);

            if (fromDate.HasValue)
                query = query.Where(a => a.AssignedAt >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(a => a.AssignedAt < toDate.Value.Date.AddDays(1));

            switch (status)
            {
                case "Active":
                    query = query.Where(a => !a.IsCancelled && !a.IsCashConfirmedByDoctor
                                           && !a.IsCompleted && a.AssignmentStatus != "PendingHandover");
                    break;
                case "PendingHandover":
                    query = query.Where(a => !a.IsCancelled && !a.IsCashConfirmedByDoctor
                                           && !a.IsCompleted && a.AssignmentStatus == "PendingHandover");
                    break;
                case "CashConfirmed":
                    query = query.Where(a => a.IsCashConfirmedByDoctor);
                    break;
                case "Completed":
                    query = query.Where(a => a.IsCompleted);
                    break;
                case "Cancelled":
                    query = query.Where(a => a.IsCancelled);
                    break;
                case "All":
                default:
                    break;
            }

            var raw = await query.OrderByDescending(a => a.AssignedAt).ToListAsync();

            var childIds = raw.Select(a => a.ChildId).Distinct().ToList();
            var paIds    = raw.Select(a => a.PersonalAssistantId).Distinct().ToList();
            var clinicNameIds = raw.Where(a => a.ClinicId.HasValue).Select(a => a.ClinicId!.Value).Distinct().ToList();

            var children = childIds.Any()
                ? await _db.Childs.Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id)
                : new Dictionary<long, VaccineAPI.Models.Child>();

            var pas = paIds.Any()
                ? await _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id)
                : new Dictionary<long, VaccineAPI.Models.PersonalAssistant>();

            var clinicNames = clinicNameIds.Any()
                ? await _db.Clinics.Where(c => clinicNameIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name)
                : new Dictionary<long, string>();

            var assignmentIds = raw.Select(a => a.Id).ToList();
            var scheduleLinks = assignmentIds.Any()
                ? await _db.PAAssignmentSchedules.Where(l => assignmentIds.Contains(l.AssignmentId)).ToListAsync()
                : new List<PAAssignmentSchedule>();
            var scheduleIds = scheduleLinks.Select(l => l.ScheduleId).Distinct().ToList();
            var scheduleRows = scheduleIds.Any()
                ? await _db.Schedules.Where(s => scheduleIds.Contains(s.Id))
                    .Join(_db.Doses, s => s.DoseId, d => d.Id,
                        (s, d) => new DoseRow { ScheduleId = s.Id, DoseName = d.Name, Date = s.Date, IsDone = s.IsDone, GivenByPaId = s.GivenByPaId })
                    .ToListAsync()
                : new List<DoseRow>();
            var schedulesById = scheduleRows.ToDictionary(s => s.ScheduleId);

            var invoiceIds = raw.Where(a => a.InvoiceSubmissionId.HasValue).Select(a => a.InvoiceSubmissionId!.Value).Distinct().ToList();
            var invoiceAmounts = invoiceIds.Any()
                ? await _db.InvoiceSubmissions.Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.TotalAmount)
                : new Dictionary<long, decimal>();

            var result = raw.Select(a =>
            {
                var child = children.ContainsKey(a.ChildId) ? children[a.ChildId] : null;
                var pa    = pas.ContainsKey(a.PersonalAssistantId) ? pas[a.PersonalAssistantId] : null;

                var doses = scheduleLinks
                    .Where(l => l.AssignmentId == a.Id)
                    .Where(l => schedulesById.ContainsKey(l.ScheduleId))
                    .Select(l => schedulesById[l.ScheduleId])
                    .ToList();

                return new
                {
                    AssignmentId  = a.Id,
                    a.AssignedAt,
                    a.TargetDate,
                    a.Notes,
                    a.AssignmentStatus,
                    a.IsCompleted,
                    a.IsCancelled,
                    a.IsAutoCreated,
                    a.CompletedAt,
                    a.CancelledAt,
                    a.CancelReason,
                    a.IsCashConfirmedByDoctor,
                    a.CashConfirmedAt,
                    ChildId    = a.ChildId,
                    ChildName  = child?.Name ?? "",
                    DOB        = child?.DOB ?? (DateTime?)null,
                    PaId       = a.PersonalAssistantId,
                    PaName     = pa?.Name ?? "",
                    ClinicId   = a.ClinicId,
                    ClinicName = a.ClinicId.HasValue && clinicNames.ContainsKey(a.ClinicId.Value) ? clinicNames[a.ClinicId.Value] : "",
                    DosesTotal = doses.Count,
                    DosesGiven = doses.Count(d => d.IsDone),
                    Doses      = doses,
                    CashAmount = a.InvoiceSubmissionId.HasValue && invoiceAmounts.ContainsKey(a.InvoiceSubmissionId.Value)
                        ? invoiceAmounts[a.InvoiceSubmissionId.Value] : (decimal?)null
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }
    }

    public class CancelAssignmentDto
    {
        public string CallerType { get; set; } // "DOCTOR" or "PA"
        public long CallerId { get; set; }
        public string? Reason { get; set; }
    }

    public class ReassignDto
    {
        public long NewPaId { get; set; }
        public string? Notes { get; set; }
        public DateTime? TargetDate { get; set; }
        public long? RequestingPaId { get; set; }
    }

    public class SetTargetDateDto
    {
        public long DoctorId { get; set; }
        public DateTime? TargetDate { get; set; }
    }

    public class RejectCancelDto
    {
        public long DoctorId { get; set; }
        public string? Notes { get; set; }
    }

    public class CreateAssignmentDto
    {
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long PersonalAssistantId { get; set; }
        public long ChildId { get; set; }
        public string? Notes { get; set; }
        public List<long> ScheduleIds { get; set; } = new List<long>();
        public DateTime? TargetDate { get; set; }
        public long? BookingId { get; set; }
    }

    public class DoseRow
    {
        public long ScheduleId { get; set; }
        public string DoseName { get; set; } = "";
        public DateTime Date { get; set; }
        public bool IsDone { get; set; }
        public long? GivenByPaId { get; set; }
    }
}
