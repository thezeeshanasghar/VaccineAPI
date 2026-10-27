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
    [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
    [Owns(OwnerKind.Doctor, "doctorId")]
    [Owns(OwnerKind.Clinic, "clinicId", "OnlineClinicId")]
    [Owns(OwnerKind.Child, "childId")]
    [Owns(OwnerKind.Pa, "paId")]
    public class PAAssignmentController : ControllerBase
    {
        private readonly Context _db;
        private readonly VaccineAPI.Services.InventoryTransactionService _inventory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public PAAssignmentController(Context db, VaccineAPI.Services.InventoryTransactionService inventory, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _db = db;
            _inventory = inventory;
            _config = config;
        }

        // Same resolution order as UserController.LinkLoginSecret() — one shared config
        // key/env var for every magic-link token type in the app.
        private string LinkLoginSecret()
        {
            return _config["LinkLogin:Secret"]
                ?? Environment.GetEnvironmentVariable("LinkLoginSecret")
                ?? "";
        }

        // Verifies the caller is who they claim to be: userId/securityStamp must match a real
        // User row, and that row's SecurityStamp must equal the one issued at login (login and
        // validate-session already produce/check this same pair — this just applies it here too).
        // Every login page (loginpa, login) stores SecurityStamp locally right after auth, so any
        // caller with a valid session already has it on hand to send.
        private Task<bool> VerifyCaller(long? userId, string? securityStamp)
            => Task.FromResult(CallerGuard.VerifyCaller(_db, userId, securityStamp));

        // GET /api/PAAssignment/pa/{paId}
        [HttpGet("pa/{paId}")]
        public async Task<IActionResult> GetByPA(long paId, [FromQuery] long? userId, [FromQuery] string? securityStamp)
        {
            var pa = await _db.PersonalAssistant.FindAsync(paId);
            if (pa == null)
                return Ok(new { IsSuccess = false, Message = "Personal Assistant not found." });

            if (!await VerifyCaller(userId, securityStamp) || userId!.Value != pa.UserId)
                return Ok(new { IsSuccess = false, Message = "Not authorised to view this Personal Assistant's assignments." });

            // Fetch raw assignments first — avoid EF Join+Where MySQL column misattribution bug.
            // Was filtered on !IsCompleted, which dropped a row the instant the PA marked it done
            // — before the doctor ever confirmed cash handover, so a PA lost visibility on money
            // she was still accountable for. Now stays visible through IsCompleted and only drops
            // once IsCashConfirmedByDoctor, matching GetForDoctor's own "CashConfirmed" cutoff.
            var rawAssignments = await _db.PAAssignments
                .Where(a => a.PersonalAssistantId == paId && !a.IsCashConfirmedByDoctor && !a.IsCancelled)
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

            var rawAssignmentIds = rawAssignments.Select(a => a.Id).ToList();
            var pendingRefusalAssignmentIds = new HashSet<long>(
                await _db.VaccineRefusals
                    .Where(r => r.Status == "Pending" && rawAssignmentIds.Contains(r.AssignmentId))
                    .Select(r => r.AssignmentId)
                    .ToListAsync());

            // PA Daily Route: stop numbers for this PA's scheduled visits, ranked per date at read time.
            var stopNumbers = VaccineAPI.Services.VisitStatus.Compute(
                rawAssignments.Where(x => x.IsScheduledVisit).Select(x => new VaccineAPI.Services.VisitStatus.Stop
                {
                    Id = x.Id, PersonalAssistantId = x.PersonalAssistantId, TargetDate = x.TargetDate, WindowStart = x.WindowStart,
                    WindowMinutes = x.WindowMinutes, VisitOrder = x.VisitOrder, AssignedAt = x.AssignedAt
                }), Pkt.Today(), 0).ToDictionary(r => r.Id, r => r.StopNumber);

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
                            s.GrowthNotMeasured,
                            s.IsDisease,
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
                    a.IsScheduledVisit,
                    WindowStart   = VaccineAPI.Services.VisitStatus.FormatHm(a.WindowStart),
                    a.WindowMinutes,
                    a.Area,
                    a.VisitCompletedAt,
                    StopNumber    = stopNumbers.ContainsKey(a.Id) ? stopNumbers[a.Id] : (int?)null,
                    ChildId       = a.ChildId,
                    Name          = child != null ? child.Name      : "",
                    Gender        = child != null ? child.Gender    : "",
                    ChildType     = child != null ? child.Type      : "",
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
                    RefusalPending = pendingRefusalAssignmentIds.Contains(a.Id),
                    a.IsCompleted,
                    a.IsCashConfirmedByDoctor,
                    InvoiceAmount = invoice != null ? invoice.TotalAmount : 0m,
                    HasInvoice    = invoice != null,
                    a.InvoiceSubmissionId,
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
        public async Task<IActionResult> DeleteAssignment(long id, [FromQuery] long doctorId, [FromQuery] string mode = "UnassignOnly", [FromQuery] string? reason = null)
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

                    // FullReset is scoped to THIS assignment's own doses: the ones it pins (each
                    // assignment is a separate entity — one vaccine group). Resolving by the
                    // invoice link alone wiped a sibling assignment's doses whenever an invoice
                    // was shared. An assignment with no pins (legacy) falls back to the invoice
                    // stamped on it, then to the PA's not-yet-invoiced doses since it was created.
                    var visitInvoiceId = assignment.InvoiceSubmissionId;
                    var pinnedDoseIds = await _db.PAAssignmentSchedules
                        .Where(l => l.AssignmentId == assignment.Id)
                        .Select(l => l.ScheduleId)
                        .ToListAsync();

                    var schedulesQuery = _db.Schedules
                        .Include(s => s.Dose)
                        .Where(s => s.ChildId == childId);

                    List<Schedule> schedules;
                    if (pinnedDoseIds.Count > 0)
                    {
                        schedules = await schedulesQuery
                            .Where(s => pinnedDoseIds.Contains(s.Id) && (s.IsDone == true || s.InvoiceSubmissionId != null))
                            .ToListAsync();
                    }
                    else if (visitInvoiceId.HasValue)
                    {
                        schedules = await schedulesQuery
                            .Where(s => s.InvoiceSubmissionId == visitInvoiceId.Value)
                            .ToListAsync();
                    }
                    else
                    {
                        // Visit never got an invoice linked: only the PA's not-yet-invoiced doses
                        // done since this assignment was created. Anything invoiced, or done before
                        // the assignment existed, belongs to an earlier visit and is left alone.
                        var assignedAt = assignment.AssignedAt;
                        schedules = await schedulesQuery
                            .Where(s => (s.PaymentCollectorPaId == paId || s.GivenByPaId == paId)
                                        && s.InvoiceSubmissionId == null
                                        && s.DoneAt != null
                                        && s.DoneAt >= assignedAt)
                            .ToListAsync();
                    }

                    // Delete an invoice only when nothing outside this reset still references it.
                    var resetDoseIds = schedules.Select(x => x.Id).ToList();
                    var invoiceIdsToDelete = schedules
                        .Where(x => x.InvoiceSubmissionId.HasValue)
                        .Select(x => x.InvoiceSubmissionId.Value)
                        .ToList();
                    if (visitInvoiceId.HasValue) invoiceIdsToDelete.Add(visitInvoiceId.Value);
                    foreach (var invId in invoiceIdsToDelete.Distinct().ToList())
                    {
                        var stillUsed = await _db.Schedules.AnyAsync(x => x.InvoiceSubmissionId == invId && !resetDoseIds.Contains(x.Id));
                        if (stillUsed) continue;
                        _db.InvoiceAmendments.RemoveRange(_db.InvoiceAmendments.Where(am => am.InvoiceSubmissionId == invId));
                        _db.InvoiceSubmissions.RemoveRange(_db.InvoiceSubmissions.Where(i => i.Id == invId && i.ChildId == childId));
                    }

                    var inventoryEnabled = IsInventoryEnabledForDoctor(doctorId);

                    // The child's own clinic is the reliable source of truth for where a dose was
                    // actually given — assignment.ClinicId can be null or stale (e.g. the PA was
                    // reassigned, or the field was never backfilled), which silently pointed the
                    // BrandAmount lookup below at the wrong clinic and swallowed the restore.
                    var childClinicId = await _db.Childs
                        .Where(c => c.Id == childId)
                        .Select(c => c.ClinicId)
                        .FirstOrDefaultAsync();

                    var vaccineIdsToCleanUp = new HashSet<long>();

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

                            _inventory.UnadministerSync(doctorId, childClinicId, s.BrandId.Value, s.Id, s.GivenDate ?? Pkt.Today(), paId);
                        }
                        else if (!inventoryEnabled && s.IsDone == true && s.BrandId.HasValue && _inventory.HasLiveGive(s.Id))
                        {
                            // Inventory was switched off after this dose consumed stock: still reverse it.
                            _inventory.UnadministerSync(doctorId, childClinicId, s.BrandId.Value, s.Id, s.GivenDate ?? Pkt.Today(), paId);
                        }

                        // This dose was given, which (for an infinite/repeating vaccine like Flu)
                        // already inserted a brand-new future Schedule row dated GivenDate+MinGap.
                        // Reversing the give below without also removing that future row leaves it
                        // behind as a permanent orphan — same cleanup the normal UNGIVE button runs.
                        if (s.IsDone == true && InfiniteDoseCleanup.IsInfiniteDoseName(s.Dose?.Name))
                        {
                            vaccineIdsToCleanUp.Add(s.Dose.VaccineId);
                        }

                        // Same archive-before-reset as Reassign() — a FullReset wipes this PA's
                        // fingerprints from the schedule (give/skip history, payment state), and
                        // the give/ungive/skip/unskip caps need to go with it, or whichever PA
                        // picks this dose up next inherits a maxed-out counter that isn't theirs.
                        if (s.GiveCount != 0 || s.UngiveCount != 0 || s.SkipCount != 0 || s.UnskipCount != 0)
                        {
                            _db.PaActivityLogs.Add(new PaActivityLog
                            {
                                PaId = paId,
                                DoctorId = doctorId,
                                ClinicId = assignment.ClinicId,
                                PatientId = childId,
                                ActionCode = "ReassignCounterSnapshot",
                                Description = $"PA counters archived on FullReset of schedule {s.Id}",
                                Notes = $"Give={s.GiveCount}, Ungive={s.UngiveCount}, Skip={s.SkipCount}, Unskip={s.UnskipCount}",
                                ArchivedGiveCount = s.GiveCount,
                                ArchivedUngiveCount = s.UngiveCount,
                                ArchivedSkipCount = s.SkipCount,
                                ArchivedUnskipCount = s.UnskipCount,
                                ActionDate = DateTime.UtcNow
                            });

                            s.GiveCount = 0;
                            s.UngiveCount = 0;
                            s.SkipCount = 0;
                            s.UnskipCount = 0;
                        }

                        s.IsDone = false;
                        s.GivenDate = null;
                        s.DoneAt = null;
                        s.InvoiceSubmissionId = null;
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

                    // The cleanup queries the DB for undone rows: the doses reset above are still
                    // IsDone=true there until saved, so without this flush it never sees the reset
                    // row and leaves the dose's spawned next-year row behind (still inside the tx).
                    if (vaccineIdsToCleanUp.Count > 0)
                        await _db.SaveChangesAsync();

                    foreach (var vaccineId in vaccineIdsToCleanUp)
                    {
                        InfiniteDoseCleanup.RemoveExtraUndoneRows(_db, childId, vaccineId);
                    }
                }

                // A FullReset resolves any pending "patient refused at home" request for this
                // assignment, in the same transaction — so Approved always means really reversed.
                if (mode == "FullReset")
                {
                    var pendingRefusals = await _db.VaccineRefusals
                        .Where(r => r.AssignmentId == assignment.Id && r.Status == "Pending")
                        .ToListAsync();
                    foreach (var r in pendingRefusals)
                    {
                        r.Status = "Approved";
                        r.ResolvedAt = DateTime.UtcNow;
                    }
                }

                // The assignment's pin rows go with it (they used to be left dangling).
                _db.PAAssignmentSchedules.RemoveRange(_db.PAAssignmentSchedules.Where(l => l.AssignmentId == assignment.Id));
                // The assignment row is deleted, so this log row is the only lasting record of who
                // removed it, when, and in which mode. It commits in the same transaction.
                VaccineAPI.Services.AssignmentAudit.Add(_db, mode == "FullReset" ? "Reversed" : "Removed", assignment, "DOCTOR", doctorId,
                    mode == "FullReset" ? "Visit reversed: doses ungiven, stock returned, invoice deleted" : "Assignment removed (doses and invoice untouched)",
                    reason, new { mode, paId = assignment.PersonalAssistantId });
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
            VaccineAPI.Services.AssignmentAudit.Add(_db, "Completed", assignment, paId.HasValue ? "PA" : "DOCTOR", paId ?? assignment.DoctorId,
                "Marked complete");

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

            // Cancel only calls off the PA's task. Once a dose is given or payment is recorded it would
            // leave given doses and collected cash with no owner, so neither PA nor doctor may use it:
            // the PA must ask the doctor, the doctor must Reassign / Take back / Reverse instead
            // (PA_TRACKING_MERGE_SPEC.md section 2).
            var hasGivenOrPaid = await _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == assignment.Id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .AnyAsync(s => s.IsDone == true || s.IsPaymentCollected == true);

            if (hasGivenOrPaid && dto.CallerType == "PA")
                return BadRequest(new { IsSuccess = false, Message = "This assignment has vaccines given or payment recorded and can no longer be self-cancelled. Please contact the doctor." });

            if (hasGivenOrPaid && dto.CallerType == "DOCTOR")
                return Ok(new { IsSuccess = false, Message = "Vaccines are already given or payment is recorded, so this assignment cannot be cancelled. Reassign it to another PA, or reverse the visit." });

            assignment.IsCancelled  = true;
            assignment.CancelledAt  = DateTime.UtcNow;
            assignment.CancelReason = dto.Reason;
            VaccineAPI.Services.AssignmentAudit.Add(_db, "Cancelled", assignment, dto.CallerType == "PA" ? "PA" : "DOCTOR", dto.CallerId,
                "Assignment cancelled (task only)", dto.Reason);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Notify doctor by email (fire-and-forget)
            if (dto.CallerType == "PA")
            {
                var cKid = await _db.Childs.FindAsync(assignment.ChildId);
                var cPa = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                VaccineAPI.Services.NotifyHelper.Add(_db, "PaCancelled", "DOCTOR", assignment.DoctorId, "Assignment cancelled by PA",
                    $"{cPa?.Name ?? "Your PA"} cancelled the assignment for {cKid?.Name ?? "a patient"}. Please reassign or reschedule.", assignment.ChildId, assignment.ClinicId);
                await _db.SaveChangesAsync();
                var doctor = await _db.Doctors.FindAsync(assignment.DoctorId);
                if (doctor != null && !string.IsNullOrEmpty(doctor.Email))
                {
                    var paUser = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                    var paNameStr    = paUser?.Name ?? "Your PA";
                    var child        = await _db.Childs.FindAsync(assignment.ChildId);
                    var childNameStr = child?.Name ?? "a patient";
                    var reasonStr    = dto.Reason ?? "No reason given";
                    var cancelSender = EmailSenderResolver.Resolve(doctor, _db);
                    _ = Task.Run(() => UserEmail.SendEmail(
                        doctor.Email,
                        $"{paNameStr} has cancelled the assignment for patient {childNameStr}. Reason: {reasonStr}. Please reassign or reschedule.",
                        "PA Assignment Cancelled",
                        sender: cancelSender
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
            VaccineAPI.Services.AssignmentAudit.Add(_db, "CancelRequested", assignment, "PA", dto.CallerId,
                "Asked the doctor to cancel", dto.Reason);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            // Notify doctor by email (fire-and-forget)
            var doctor = await _db.Doctors.FindAsync(assignment.DoctorId);
            {
                var rqPa = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                var rqKid = await _db.Childs.FindAsync(assignment.ChildId);
                VaccineAPI.Services.NotifyHelper.Add(_db, "PaCancelRequest", "DOCTOR", assignment.DoctorId, "Cancellation request",
                    $"{rqPa?.Name ?? "Your PA"} asks to cancel the assignment for {rqKid?.Name ?? "a patient"}. Tap to approve or reject.", assignment.ChildId, assignment.ClinicId);
                await _db.SaveChangesAsync();
            }
            if (doctor != null && !string.IsNullOrEmpty(doctor.Email))
            {
                var paUser = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
                var paNameStr    = paUser?.Name ?? "Your PA";
                var child        = await _db.Childs.FindAsync(assignment.ChildId);
                var childNameStr = child?.Name ?? "a patient";
                var reasonStr    = dto.Reason ?? "No reason given";
                var cancelReqSender = EmailSenderResolver.Resolve(doctor, _db);
                _ = Task.Run(() => UserEmail.SendEmail(
                    doctor.Email,
                    $"{paNameStr} has requested to cancel the assignment for patient {childNameStr}. Reason: {reasonStr}. Please review and approve or reject this request in the VacDoc app.",
                    "PA Cancellation Request — Approval Needed",
                    sender: cancelReqSender
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
            VaccineAPI.Services.AssignmentAudit.Add(_db, "CancelApproved", assignment, "DOCTOR", assignment.DoctorId,
                "Approved the PA's cancel request", assignment.CancelRequestReason);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            var apKid = await _db.Childs.FindAsync(assignment.ChildId);
            VaccineAPI.Services.NotifyHelper.Add(_db, "PaCancelApproved", "PA", assignment.PersonalAssistantId, "Cancellation approved",
                $"Your request to cancel the assignment for {apKid?.Name ?? "the patient"} was approved.", assignment.ChildId, assignment.ClinicId);
            await _db.SaveChangesAsync();

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
            VaccineAPI.Services.AssignmentAudit.Add(_db, "CancelRejected", assignment, "DOCTOR", assignment.DoctorId,
                "Rejected the PA's cancel request. Assignment stays active", dto.Notes);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            var rjKid = await _db.Childs.FindAsync(assignment.ChildId);
            VaccineAPI.Services.NotifyHelper.Add(_db, "PaCancelRejected", "PA", assignment.PersonalAssistantId, "Cancellation rejected",
                $"Your request to cancel the assignment for {rjKid?.Name ?? "the patient"} was rejected. The assignment stays active.", assignment.ChildId, assignment.ClinicId);
            await _db.SaveChangesAsync();

            // Notify PA by email (fire-and-forget)
            var pa = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                var child = await _db.Childs.FindAsync(assignment.ChildId);
                var childNameStr = child?.Name ?? "the patient";
                var reason = !string.IsNullOrEmpty(dto.Notes) ? dto.Notes : "No reason given";
                var rejectDoctor = await _db.Doctors.FindAsync(assignment.DoctorId);
                var rejectSender = EmailSenderResolver.Resolve(rejectDoctor, _db);
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    $"Hi {pa.Name},<br><br>Your request to cancel the assignment for patient <b>{childNameStr}</b> has been <b>rejected</b>.<br>Reason: {reason}<br><br>The assignment remains active.",
                    "Cancellation Request Rejected",
                    sender: rejectSender
                ));
            }

            return Ok(new { IsSuccess = true, Message = "Cancellation request rejected" });
        }

        // POST /api/PAAssignment/{id}/request-refusal
        // PA-only: the patient refused at home. Allowed from assignment until the payment mode
        // is recorded. Nothing is reversed here — the doctor's approval does that.
        [HttpPost("{id}/request-refusal")]
        public async Task<IActionResult> RequestRefusal(long id, [FromBody] CancelAssignmentDto dto)
        {
            var assignment = await _db.PAAssignments.FindAsync(id);
            if (assignment == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });
            if (assignment.IsCompleted || assignment.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "This assignment is already closed" });
            if (assignment.PersonalAssistantId != dto.CallerId)
                return Ok(new { IsSuccess = false, Message = "You are not authorised for this assignment" });

            if (await _db.VaccineRefusals.AnyAsync(r => r.AssignmentId == id && r.Status == "Pending"))
                return Ok(new { IsSuccess = false, Message = "A refusal request is already awaiting the doctor" });

            var paymentRecorded = await _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .AnyAsync(s => s.IsPaymentCollected);
            if (paymentRecorded)
                return Ok(new { IsSuccess = false, Message = "Payment mode is already recorded, so this can no longer be reversed from here. Please contact the doctor." });

            _db.VaccineRefusals.Add(new VaccineRefusal
            {
                AssignmentId = assignment.Id,
                ChildId = assignment.ChildId,
                DoctorId = assignment.DoctorId,
                PaId = assignment.PersonalAssistantId,
                Reason = dto.Reason,
                Status = "Pending",
                RequestedAt = DateTime.UtcNow
            });

            // In-app bell notification for the doctor (shown in the top-bar bell + Notifications page)
            var notifPa = await _db.PersonalAssistant.FindAsync(assignment.PersonalAssistantId);
            var notifChild = await _db.Childs.FindAsync(assignment.ChildId);
            var priorRefusals = await _db.VaccineRefusals
                .CountAsync(r => r.ChildId == assignment.ChildId && r.Status == "Approved");
            _db.Notifications.Add(new Notification
            {
                Type          = "PaRefusal",
                RecipientType = "DOCTOR",
                RecipientId   = assignment.DoctorId,
                ChildId       = assignment.ChildId,
                ClinicId      = assignment.ClinicId,
                Title         = $"Refused at home: {notifChild?.Name ?? "a patient"}",
                Message       = $"{notifPa?.Name ?? "Your PA"} reports the family refused. Tap to approve or reject."
                                + (priorRefusals > 0 ? $" Refused before: {priorRefusals} time{(priorRefusals > 1 ? "s" : "")}." : "")
                                + (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $" Reason: {dto.Reason}"),
                IsRead        = false,
                CreatedAt     = DateTime.UtcNow
            });
            VaccineAPI.Services.AssignmentAudit.Add(_db, "RefusalRequested", assignment, "PA", dto.CallerId,
                "PA reported the client refused at home", dto.Reason);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            var doctor = await _db.Doctors.FindAsync(assignment.DoctorId);
            if (doctor != null && !string.IsNullOrEmpty(doctor.Email))
            {
                var paUser = notifPa;
                var paNameStr = paUser?.Name ?? "Your PA";
                var child = notifChild;
                var childNameStr = child?.Name ?? "a patient";
                var reasonStr = dto.Reason ?? "No reason given";
                var refusalSender = EmailSenderResolver.Resolve(doctor, _db);
                _ = Task.Run(() => UserEmail.SendEmail(
                    doctor.Email,
                    $"The family of {childNameStr} refused the vaccination at home (reported by {paNameStr}). Reason: {reasonStr}. Approve in the VacDoc app to reverse the visit, or reject to keep it.",
                    "Patient Refused at Home — Approval Needed",
                    sender: refusalSender
                ));
            }

            return Ok(new { IsSuccess = true });
        }

        // GET /api/PAAssignment/pending-refusals/{doctorId}
        [HttpGet("pending-refusals/{doctorId}")]
        public async Task<IActionResult> GetPendingRefusals(long doctorId)
        {
            var pending = await _db.VaccineRefusals
                .Where(r => r.DoctorId == doctorId && r.Status == "Pending")
                .OrderByDescending(r => r.RequestedAt)
                .ToListAsync();

            var childIds = pending.Select(r => r.ChildId).Distinct().ToList();
            var childNames = await _db.Childs
                .Where(c => childIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name ?? "");
            var paIds = pending.Select(r => r.PaId).Distinct().ToList();
            var paNames = await _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? "");
            var priorCounts = await _db.VaccineRefusals
                .Where(r => childIds.Contains(r.ChildId) && r.Status == "Approved")
                .GroupBy(r => r.ChildId)
                .Select(g => new { ChildId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ChildId, x => x.Count);

            var result = pending.Select(r => new
            {
                RefusalId = r.Id,
                AssignmentId = r.AssignmentId,
                ChildId = r.ChildId,
                ChildName = childNames.ContainsKey(r.ChildId) ? childNames[r.ChildId] : "",
                PaId = r.PaId,
                PaName = paNames.ContainsKey(r.PaId) ? paNames[r.PaId] : "",
                RequestedAt = r.RequestedAt,
                Reason = r.Reason,
                PriorRefusals = priorCounts.ContainsKey(r.ChildId) ? priorCounts[r.ChildId] : 0
            });

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // PATCH /api/PAAssignment/{id}/approve-refusal?doctorId=
        // Approving IS the reversal: runs the existing FullReset (ungive, stock back, invoice
        // deleted, payment cleared, assignment removed), which marks the request Approved in
        // its own transaction. If the reset fails the request stays Pending.
        [HttpPatch("{id}/approve-refusal")]
        public async Task<IActionResult> ApproveRefusal(long id, [FromQuery] long doctorId)
        {
            var request = await _db.VaccineRefusals
                .FirstOrDefaultAsync(r => r.AssignmentId == id && r.Status == "Pending");
            if (request == null)
                return Ok(new { IsSuccess = false, Message = "This request has already been resolved" });
            if (request.DoctorId != doctorId)
                return Ok(new { IsSuccess = false, Message = "Not authorised" });

            var approveResult = await DeleteAssignment(id, doctorId, "FullReset",
                "Client refused at home" + (string.IsNullOrWhiteSpace(request.Reason) ? "" : ": " + request.Reason));
            if (await _db.VaccineRefusals.AnyAsync(r => r.Id == request.Id && r.Status == "Approved"))
            {
                var apRefAsg = await _db.PAAssignments.FindAsync(id);
                if (apRefAsg != null)
                    VaccineAPI.Services.AssignmentAudit.Add(_db, "RefusalApproved", apRefAsg, "DOCTOR", doctorId,
                        "Approved the PA's refusal-at-home report", request.Reason);
                var apRefKid = await _db.Childs.FindAsync(request.ChildId);
                VaccineAPI.Services.NotifyHelper.Add(_db, "PaRefusalApproved", "PA", request.PaId, "Refusal approved",
                    $"Your \"refused at home\" report for {apRefKid?.Name ?? "the patient"} was approved.", request.ChildId, null);
                await _db.SaveChangesAsync();
            }
            return approveResult;
        }

        // PATCH /api/PAAssignment/{id}/reject-refusal
        [HttpPatch("{id}/reject-refusal")]
        public async Task<IActionResult> RejectRefusal(long id, [FromBody] RejectCancelDto dto)
        {
            var request = await _db.VaccineRefusals
                .FirstOrDefaultAsync(r => r.AssignmentId == id && r.Status == "Pending");
            if (request == null)
                return Ok(new { IsSuccess = false, Message = "This request has already been resolved" });
            if (request.DoctorId != dto.DoctorId)
                return Ok(new { IsSuccess = false, Message = "Not authorised" });

            request.Status = "Rejected";
            request.ResolvedAt = DateTime.UtcNow;
            request.RejectionNote = dto.Notes;
            var rjAsg = await _db.PAAssignments.FindAsync(request.AssignmentId);
            if (rjAsg != null)
                VaccineAPI.Services.AssignmentAudit.Add(_db, "RefusalRejected", rjAsg, "DOCTOR", dto.DoctorId,
                    "Doctor rejected the refusal report; the visit continues", dto.Notes);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }

            var rjRefKid = await _db.Childs.FindAsync(request.ChildId);
            VaccineAPI.Services.NotifyHelper.Add(_db, "PaRefusalRejected", "PA", request.PaId, "Refusal rejected",
                $"Your \"refused at home\" report for {rjRefKid?.Name ?? "the patient"} was rejected. The assignment stays active.", request.ChildId, null);
            await _db.SaveChangesAsync();

            var pa = await _db.PersonalAssistant.FindAsync(request.PaId);
            if (pa != null && !string.IsNullOrEmpty(pa.Email))
            {
                var child = await _db.Childs.FindAsync(request.ChildId);
                var childNameStr = child?.Name ?? "the patient";
                var reason = !string.IsNullOrEmpty(dto.Notes) ? dto.Notes : "No reason given";
                var rejectDoctor = await _db.Doctors.FindAsync(request.DoctorId);
                var rejectSender = EmailSenderResolver.Resolve(rejectDoctor, _db);
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    $"Hi {pa.Name},<br><br>Your \"refused at home\" request for patient <b>{childNameStr}</b> was <b>rejected</b>.<br>Note: {reason}<br><br>The assignment remains active.",
                    "Refusal Request Rejected",
                    sender: rejectSender
                ));
            }

            return Ok(new { IsSuccess = true, Message = "Request rejected" });
        }

        // GET /api/PAAssignment/refusal-flag/{childId}
        // Golden marker data. Active while the child has an approved refusal and no dose has
        // been given since it was approved. Count is the child's total approved refusals.
        [HttpGet("refusal-flag/{childId}")]
        public async Task<IActionResult> GetRefusalFlag(long childId)
        {
            var approved = await _db.VaccineRefusals
                .Where(r => r.ChildId == childId && r.Status == "Approved")
                .OrderByDescending(r => r.ResolvedAt)
                .ToListAsync();

            if (!approved.Any())
                return Ok(new { IsSuccess = true, ResponseData = new { Active = false, Count = 0 } });

            var last = approved.First();
            var givenSince = await _db.Schedules
                .AnyAsync(s => s.ChildId == childId && s.IsDone == true && s.DoneAt != null && s.DoneAt > last.ResolvedAt);

            return Ok(new
            {
                IsSuccess = true,
                ResponseData = new { Active = !givenSince, Count = approved.Count, LastAt = last.ResolvedAt, LastReason = last.Reason }
            });
        }

        // PATCH /api/PAAssignment/{id}/reassign
        // dto.RequestingManagerId: set when a Manager (not the doctor) is initiating the
        // reassign from the shared PA Assignment Tracking view. Requires ReassignPaTask, and
        // the assignment's own clinic must be one the Manager has ManagerAccess to — same fence
        // as GetForDoctor's requestingManagerId, so a Manager can't reassign an assignment they
        // weren't even allowed to see.
        [HttpPatch("{id}/reassign")]
        public async Task<IActionResult> Reassign(long id, [FromBody] ReassignDto dto)
        {
            var old = await _db.PAAssignments.FindAsync(id);
            if (old == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (dto.RequestingManagerId.HasValue)
            {
                var managerPerm = await _db.ManagerPermissions.FirstOrDefaultAsync(p => p.ManagerId == dto.RequestingManagerId.Value);
                if (managerPerm == null || !managerPerm.ReassignPaTask)
                    return Ok(new { IsSuccess = false, Message = "You do not have permission to reassign PA tasks." });

                if (old.ClinicId.HasValue)
                {
                    var hasAccess = await _db.ManagerAccess.AnyAsync(a =>
                        a.ManagerId == dto.RequestingManagerId.Value && a.ClinicId == old.ClinicId.Value);
                    if (!hasAccess)
                        return Ok(new { IsSuccess = false, Message = "This assignment's clinic is outside your access." });
                }
            }
            else
            {
                // Doctor-initiated reassign (e.g. vaccine.page.ts's reassign popup) — caller
                // must be the owning doctor, verified the same way the manager path is fenced
                // above rather than trusting old.DoctorId's own request implicitly.
                var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == old.DoctorId);
                if (doctor == null || !await VerifyCaller(dto.CallerUserId, dto.SecurityStamp) || dto.CallerUserId!.Value != doctor.UserId)
                    return Ok(new { IsSuccess = false, Message = "Not authorised to reassign this assignment." });
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
                BookingId                   = old.BookingId,
                // R6: a scheduled visit keeps its window/area/order on reassign. An explicit
                // completion does not carry (the new PA completes it themselves).
                IsScheduledVisit            = old.IsScheduledVisit,
                WindowStart                 = old.IsScheduledVisit && (dto.TargetDate == null || dto.TargetDate.Value.Date == old.TargetDate?.Date) ? old.WindowStart : null,
                WindowMinutes               = old.WindowMinutes,
                Area                        = old.Area,
                VisitOrder                  = old.VisitOrder
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

            // Archive the outgoing PA's give/ungive/skip/unskip counts before resetting them
            // for the incoming PA. Without this, a reassigned dose's Schedule.*Count fields
            // carry the old PA's history forward — the new PA can get blocked by a "given/
            // ungiven twice" cap they never triggered themselves. One PaActivityLog row per
            // affected Schedule that actually has a non-zero count, so the old PA's history is
            // preserved (queryable via ArchivedGiveCount etc.) rather than just discarded.
            if (linksToMove.Count > 0)
            {
                var scheduleIds = linksToMove.Select(l => l.ScheduleId).ToList();
                var schedulesToReset = await _db.Schedules
                    .Where(s => scheduleIds.Contains(s.Id))
                    .ToListAsync();

                foreach (var s in schedulesToReset)
                {
                    // The doses still owed to the old PA move to the new PA: otherwise cash-in-hand
                    // and the collection task list keep crediting the old PA for this visit.
                    if (s.PaymentCollectorPaId == old.PersonalAssistantId && !s.IsPaymentCollected)
                        s.PaymentCollectorPaId = dto.NewPaId;

                    if (s.GiveCount == 0 && s.UngiveCount == 0 && s.SkipCount == 0 && s.UnskipCount == 0)
                        continue;

                    _db.PaActivityLogs.Add(new PaActivityLog
                    {
                        PaId = old.PersonalAssistantId,
                        DoctorId = old.DoctorId,
                        ClinicId = old.ClinicId,
                        PatientId = old.ChildId,
                        ActionCode = "ReassignCounterSnapshot",
                        Description = $"PA counters archived on reassignment away from schedule {s.Id}",
                        Notes = $"Give={s.GiveCount}, Ungive={s.UngiveCount}, Skip={s.SkipCount}, Unskip={s.UnskipCount}",
                        ArchivedGiveCount = s.GiveCount,
                        ArchivedUngiveCount = s.UngiveCount,
                        ArchivedSkipCount = s.SkipCount,
                        ArchivedUnskipCount = s.UnskipCount,
                        ActionDate = DateTime.UtcNow
                    });

                    s.GiveCount = 0;
                    s.UngiveCount = 0;
                    s.SkipCount = 0;
                    s.UnskipCount = 0;
                }

                await _db.SaveChangesAsync();
            }

            // Move the linked invoice's PaId to the new PA — exact, via the FK just carried
            // forward above, not a date-window guess.
            if (old.InvoiceSubmissionId.HasValue)
            {
                var invoiceToMove = await _db.InvoiceSubmissions.FindAsync(old.InvoiceSubmissionId.Value);
                // Only re-label an invoice that belongs wholly to the reassigned assignment (every
                // dose on it is among the pins just moved); a legacy invoice shared with another
                // assignment keeps its PA.
                var movedDoseIds = linksToMove.Select(l => l.ScheduleId).ToList();
                var invoiceSharedElsewhere = movedDoseIds.Count > 0
                    && await _db.Schedules.AnyAsync(x => x.InvoiceSubmissionId == old.InvoiceSubmissionId.Value && !movedDoseIds.Contains(x.Id));
                if (invoiceToMove != null && !invoiceSharedElsewhere)
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
                var reassignDoctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == newAssignment.DoctorId);
                var reassignSender = EmailSenderResolver.Resolve(reassignDoctor, _db);
                string reassignEmailBody = await BuildAssignmentEmailBody(newAssignment.Id, pa.UserId, old.ChildId);
                _ = Task.Run(() => UserEmail.SendEmail(
                    pa.Email,
                    reassignEmailBody,
                    "New Patient Assignment",
                    sender: reassignSender
                ));
            }

            // Notify the PARENT that their vaccine coordinator has changed. Fires on every
            // reassignment (not just first assignment) — the parent needs to know who is
            // coordinating now. Same helper as Create; passes isReassignment for the wording.
            await NotifyParentOfAssignment(old.ChildId, dto.NewPaId, old.ClinicId, isReassignment: true);

            var reKid = await _db.Childs.FindAsync(old.ChildId);
            VaccineAPI.Services.NotifyHelper.Add(_db, "PaNewAssignment", "PA", dto.NewPaId, "New patient assigned",
                $"{reKid?.Name ?? "A patient"} has been reassigned to you: {await DoseListFor(newAssignment.Id)}. Open Assignments to see the visit.", old.ChildId, old.ClinicId);
            if (old.PersonalAssistantId != dto.NewPaId)
                VaccineAPI.Services.NotifyHelper.Add(_db, "PaAssignmentRemoved", "PA", old.PersonalAssistantId, "Assignment reassigned",
                    $"{reKid?.Name ?? "A patient"} has been reassigned to another assistant.", old.ChildId, old.ClinicId);
            VaccineAPI.Services.AssignmentAudit.Add(_db, "Reassigned", newAssignment,
                dto.RequestingManagerId.HasValue ? "MANAGER" : "DOCTOR", dto.RequestingManagerId ?? old.DoctorId,
                $"Reassigned from PA {old.PersonalAssistantId} to PA {dto.NewPaId}", dto.Reason,
                new { fromAssignmentId = old.Id, fromPaId = old.PersonalAssistantId, toPaId = dto.NewPaId });
            await _db.SaveChangesAsync();

            return Ok(new { IsSuccess = true, ResponseData = new { NewAssignmentId = newAssignment.Id } });
        }

        // PATCH /api/PAAssignment/{id}/take-back
        // Doctor-only. The PA has given the vaccine but not collected money, and the doctor takes
        // the visit back to handle the payment personally (PA_TRACKING_MERGE_SPEC.md Q4):
        //   - the doses stay given (nothing is ungiven, no stock moves),
        //   - the PA stops being credited as collector for the unpaid doses (cash-in-hand and the
        //     collection task list stop counting them),
        //   - an invoice that belongs wholly to this assignment and is not yet confirmed loses its PA,
        //   - the assignment is closed (cancelled) and the PA is told.
        // Refused once any pinned dose has payment recorded (use Reassign or reverse), and when
        // nothing is given yet (a plain Cancel does that). One SaveChanges, so it is all-or-nothing.
        [HttpPatch("{id}/take-back")]
        public async Task<IActionResult> TakeBack(long id, [FromBody] TakeBackDto dto)
        {
            var asg = await _db.PAAssignments.FindAsync(id);
            if (asg == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == asg.DoctorId);
            if (doctor == null || !await VerifyCaller(dto.CallerUserId, dto.SecurityStamp) || dto.CallerUserId!.Value != doctor.UserId)
                return Ok(new { IsSuccess = false, Message = "Not authorised to take back this assignment." });

            if (asg.IsCompleted)
                return Ok(new { IsSuccess = false, Message = "Cannot take back a completed assignment" });
            if (asg.IsCancelled)
                return Ok(new { IsSuccess = false, Message = "Assignment is already cancelled" });
            if (asg.IsCashConfirmedByDoctor)
                return Ok(new { IsSuccess = false, Message = "Cash is already confirmed for this assignment. Changes go through an Amendment." });
            if (asg.AssignmentStatus == "PendingCancellation")
                return Ok(new { IsSuccess = false, Message = "This assignment has a pending cancellation request — resolve it first." });

            var doses = await _db.PAAssignmentSchedules
                .Where(l => l.AssignmentId == asg.Id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .ToListAsync();

            if (!doses.Any(s => s.IsDone))
                return Ok(new { IsSuccess = false, Message = "Nothing is given yet, so use Cancel instead of Take it back." });
            if (doses.Any(s => s.IsPaymentCollected))
                return Ok(new { IsSuccess = false, Message = "Payment is already recorded, so this visit cannot be taken back. Reassign it to another PA, or reverse the visit." });

            var paId = asg.PersonalAssistantId;
            var uncredited = 0;
            foreach (var s in doses)
            {
                if (s.PaymentCollectorPaId == paId)
                {
                    s.PaymentCollectorPaId = null;
                    uncredited++;
                }
            }

            // An invoice is re-labelled only when every dose on it belongs to this assignment and the
            // doctor has not confirmed it; a legacy invoice shared with another assignment keeps its PA.
            var doseIds = doses.Select(s => s.Id).ToList();
            var invoiceIds = doses.Where(s => s.InvoiceSubmissionId.HasValue).Select(s => s.InvoiceSubmissionId!.Value).Distinct().ToList();
            var invoicesRelabelled = 0;
            foreach (var invId in invoiceIds)
            {
                var shared = await _db.Schedules.AnyAsync(x => x.InvoiceSubmissionId == invId && !doseIds.Contains(x.Id));
                var inv = await _db.InvoiceSubmissions.FindAsync(invId);
                if (inv != null && !shared && !inv.IsConfirmedByDoctor && inv.PaId == paId)
                {
                    inv.PaId = null;
                    invoicesRelabelled++;
                }
            }

            asg.IsCancelled = true;
            asg.CancelledAt = DateTime.UtcNow;
            asg.CancelReason = "Taken back by the doctor" + (string.IsNullOrWhiteSpace(dto.Reason) ? "" : ": " + dto.Reason);

            VaccineAPI.Services.AssignmentAudit.Add(_db, "TakenBack", asg, "DOCTOR", asg.DoctorId,
                $"Taken back from PA {paId}: doses stay given, doctor collects", dto.Reason,
                new { paId, dosesUncredited = uncredited, invoicesRelabelled });

            var kid = await _db.Childs.FindAsync(asg.ChildId);
            VaccineAPI.Services.NotifyHelper.Add(_db, "PaAssignmentRemoved", "PA", paId, "Visit taken back",
                $"The doctor has taken back {kid?.Name ?? "a patient"}'s visit and will handle the payment.", asg.ChildId, asg.ClinicId);

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message });
            }
            return Ok(new { IsSuccess = true, ResponseData = new { DosesUncredited = uncredited, InvoicesRelabelled = invoicesRelabelled } });
        }

        // GET /api/PAAssignment/{id}/history?requestingManagerId=
        // The assignment's audit trail: log rows written at each action (reassign, cancel, take back,
        // reverse, create) merged with events rebuilt from record timestamps (given, invoiced,
        // confirmed). "derived" rows are not audit entries: they carry no reason and no actor name
        // beyond what the record holds. Follows the reassigned-from chain so a reassigned
        // assignment shows its whole history. A manager never receives invoice/payment/cash events.
        [HttpGet("{id}/history")]
        public async Task<IActionResult> GetHistory(long id, [FromQuery] long? requestingManagerId = null)
        {
            var asg = await _db.PAAssignments.FindAsync(id);
            if (asg == null)
                return Ok(new { IsSuccess = false, Message = "Assignment not found" });

            if (requestingManagerId.HasValue)
            {
                var perm = await _db.ManagerPermissions.FirstOrDefaultAsync(p => p.ManagerId == requestingManagerId.Value);
                if (perm == null || !perm.ViewPaAssignmentStatus)
                    return Ok(new { IsSuccess = false, Message = "You do not have permission to view PA assignments." });
                if (asg.ClinicId.HasValue && !await _db.ManagerAccess.AnyAsync(a => a.ManagerId == requestingManagerId.Value && a.ClinicId == asg.ClinicId.Value))
                    return Ok(new { IsSuccess = false, Message = "This assignment's clinic is outside your access." });
            }

            var chain = new List<long> { asg.Id };
            var cursor = asg;
            while (cursor.ReassignedFromAssignmentId.HasValue && chain.Count < 20)
            {
                chain.Add(cursor.ReassignedFromAssignmentId.Value);
                cursor = await _db.PAAssignments.FindAsync(cursor.ReassignedFromAssignmentId.Value);
                if (cursor == null) break;
            }

            var entries = VaccineAPI.Services.AssignmentAudit.Read(_db, asg.ChildId, chain);

            var dosesQ = await _db.PAAssignmentSchedules.Where(l => l.AssignmentId == asg.Id)
                .Join(_db.Schedules, l => l.ScheduleId, s => s.Id, (l, s) => s)
                .Join(_db.Doses, s => s.DoseId, d => d.Id, (s, d) => new { s.Id, DoseId = d.Id, d.Name, s.IsDone, s.DoneAt, s.GivenByPaId, s.InvoiceSubmissionId })
                .ToListAsync();


            // Events rebuilt from record timestamps only fill gaps the log does not cover (actions
            // before the log existed). A logged entry always wins over its derived copy.
            var loggedGiven = new HashSet<long>(entries.Where(e => e.Action == "Given").SelectMany(e => e.ScheduleIds));
            var loggedInv = new HashSet<long>(entries.Where(e => e.Action == "Invoiced" && e.InvoiceId.HasValue).Select(e => e.InvoiceId!.Value));
            var loggedConf = new HashSet<long>(entries.Where(e => e.Action == "Confirmed" && e.InvoiceId.HasValue).Select(e => e.InvoiceId!.Value));

            foreach (var d in dosesQ.Where(x => x.IsDone && x.DoneAt.HasValue && !loggedGiven.Contains(x.Id)))
                entries.Add(new VaccineAPI.Services.AssignmentAudit.Entry { At = d.DoneAt!.Value, Actor = d.GivenByPaId.HasValue ? "PA" : "DOCTOR", ActorId = d.GivenByPaId ?? asg.DoctorId,
                    Action = "Given", Text = d.Name + " given", Source = "derived" });

            if (!requestingManagerId.HasValue)
            {
                var invIds = dosesQ.Where(x => x.InvoiceSubmissionId.HasValue).Select(x => x.InvoiceSubmissionId!.Value).Distinct().ToList();
                foreach (var inv in await _db.InvoiceSubmissions.Where(i => invIds.Contains(i.Id)).ToListAsync())
                {
                    if (!loggedInv.Contains(inv.Id))
                        entries.Add(new VaccineAPI.Services.AssignmentAudit.Entry { At = inv.SubmittedAt, Action = "Invoiced", Text = "Invoice " + inv.Id + " created, " + inv.TotalAmount.ToString("0"), Source = "derived" });
                    if (inv.IsConfirmedByDoctor && inv.ConfirmedAt.HasValue && !loggedConf.Contains(inv.Id))
                        entries.Add(new VaccineAPI.Services.AssignmentAudit.Entry { At = inv.ConfirmedAt.Value, Actor = "DOCTOR", ActorId = asg.DoctorId, Action = "Confirmed", Text = "Invoice " + inv.Id + " confirmed", Source = "derived" });
                    // Cash handover is recorded on the invoice only as a timestamp (the handover row
                    // is per PA, not per child), so it is always a derived line.
                    if (inv.HandoverDoneAt.HasValue)
                        entries.Add(new VaccineAPI.Services.AssignmentAudit.Entry { At = inv.HandoverDoneAt.Value, Actor = "PA", ActorId = asg.PersonalAssistantId, Action = "HandedOver", Text = "Invoice " + inv.Id + " cash handed over", Source = "derived", InvoiceId = inv.Id });
                }

                // The parent's own download of the invoice PDF (the only download the server stamps;
                // the PA/doctor first download is the "Invoiced" line above).
                var doseIds = dosesQ.Select(x => x.DoseId).Distinct().ToList();
                foreach (var pd in await _db.Invoices.Where(i => i.ChildId == asg.ChildId && i.ParentDownloadedAt != null
                                                                  && i.ParentDownloadedAt >= asg.AssignedAt && doseIds.Contains(i.DoseId)).ToListAsync())
                    entries.Add(new VaccineAPI.Services.AssignmentAudit.Entry { At = pd.ParentDownloadedAt!.Value, Actor = "PARENT", Action = "InvoiceDownloaded", Text = "invoice " + pd.InvoiceId + " downloaded by parent", Source = "derived" });
            }
            else
            {
                // Q7: managers never see invoice, payment or cash events.
                var hidden = new[] { "Invoiced", "Confirmed", "PaymentRecorded", "PaymentVerified", "MarkedDone", "TakenBack" };
                entries = entries.Where(e => !hidden.Contains(e.Action)).ToList();
            }

            // Dose names for folded per-dose lines, and the actor's name on every line.
            var allIds = entries.SelectMany(e => e.ScheduleIds).Distinct().ToList();
            var doseNames = await _db.Schedules.Where(s => allIds.Contains(s.Id))
                .Join(_db.Doses, s => s.DoseId, d => d.Id, (s, d) => new { s.Id, d.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name);
            var paIds = entries.Where(e => e.Actor == "PA").Select(e => e.ActorId).Distinct().ToList();
            var mgrIds = entries.Where(e => e.Actor == "MANAGER").Select(e => e.ActorId).Distinct().ToList();
            var docIds = entries.Where(e => e.Actor == "DOCTOR").Select(e => e.ActorId).Distinct().ToList();
            var paNames = await _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
            var mgrNames = await _db.Manager.Where(m => mgrIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
            var docNames = await _db.Doctors.Where(d => docIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => string.IsNullOrWhiteSpace(d.DisplayName) ? "Dr. " + d.FirstName : d.DisplayName);
            foreach (var e in entries)
            {
                string? n = null;
                if (e.Actor == "PA") paNames.TryGetValue(e.ActorId, out n);
                else if (e.Actor == "MANAGER") mgrNames.TryGetValue(e.ActorId, out n);
                else if (e.Actor == "DOCTOR") docNames.TryGetValue(e.ActorId, out n);
                e.ActorName = n ?? "";
            }

            var ordered = VaccineAPI.Services.AssignmentAudit.FoldPerDose(entries.OrderBy(e => e.At).ToList(), doseNames);
            return Ok(new { IsSuccess = true, ResponseData = ordered });
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
                CreatedAt       = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        // Comma-separated dose names pinned to an assignment, for push/notification text.
        private async Task<string> DoseListFor(long assignmentId)
        {
            var names = await (
                from pas in _db.PAAssignmentSchedules
                join s in _db.Schedules on pas.ScheduleId equals s.Id
                join d in _db.Doses on s.DoseId equals d.Id
                where pas.AssignmentId == assignmentId
                select d.Name
            ).ToListAsync();
            return names.Count > 0 ? string.Join(", ", names) : "vaccination";
        }

        // Builds the plain-text body for the PA-assignment email (Create + Reassign both use
        // this). Names the child and doses instead of the old generic "a patient has been
        // assigned to you" text, and appends a 24-hour magic link straight to this assignment's
        // card in VacDoc (PaAssignmentLinkToken) — read-only auto-login, no write action is ever
        // reachable from an email link.
        private async Task<string> BuildAssignmentEmailBody(long assignmentId, long paUserId, long childId)
        {
            var child = await _db.Childs.Include(c => c.User).FirstOrDefaultAsync(c => c.Id == childId);
            string childName = child?.Name ?? "a patient";

            var doseNames = await (
                from pas in _db.PAAssignmentSchedules
                join s in _db.Schedules on pas.ScheduleId equals s.Id
                join d in _db.Doses on s.DoseId equals d.Id
                where pas.AssignmentId == assignmentId
                select d.Name
            ).ToListAsync();
            string doseList = doseNames.Count > 0 ? string.Join(", ", doseNames) : "vaccination";

            var assignmentRow = await _db.PAAssignments.FindAsync(assignmentId);
            string targetDateLine = assignmentRow?.TargetDate != null
                ? "\nTarget date: " + assignmentRow.TargetDate.Value.ToString("dd MMM yyyy")
                : "";

            string token = PaAssignmentLinkToken.Generate(paUserId, assignmentId, LinkLoginSecret());
            string link = "https://doctor.vaccinationcentre.com/members/pa/assignments?t=" + Uri.EscapeDataString(token) + "&aid=" + assignmentId;

            // Same digits GetByPA already sends as ParentWhatsApp for the in-app WhatsApp
            // button — wrapped here as a wa.me link since an email can't use the app-scheme
            // (whatsapp://) or web.whatsapp.com paths the frontend uses instead.
            string parentWhatsAppLine = "";
            if (child?.User != null)
            {
                string parentWhatsAppDigits = ToWhatsAppNumber(child.User.MobileNumber, child.User.CountryCode);
                if (!string.IsNullOrEmpty(parentWhatsAppDigits))
                    parentWhatsAppLine = $"\nMessage parent on WhatsApp: https://wa.me/{parentWhatsAppDigits}";
            }

            return $"A patient has been assigned to you: {childName}, {doseList}.{targetDateLine}\n\n" +
                   $"View assignment: {link}" +
                   parentWhatsAppLine + "\n" +
                   "(link valid for 24 hours)";
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

            var oldTarget = assignment.TargetDate;
            assignment.TargetDate = dto.TargetDate;
            VaccineAPI.Services.AssignmentAudit.Add(_db, "TargetDateSet", assignment, "DOCTOR", assignment.DoctorId,
                "Target date " + (oldTarget.HasValue ? oldTarget.Value.ToString("dd MMM yyyy") + " to " : "set to ")
                + (dto.TargetDate.HasValue ? dto.TargetDate.Value.ToString("dd MMM yyyy") : "none"));

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
                if (dto.RequestingManagerId.HasValue)
                {
                    // Manager-initiated create (vaccine.page.ts's ASSIGN PA, Manager view) — fenced
                    // the same way Reassign's manager path is: permission flag + clinic access,
                    // not the doctor-ownership check below (Manager never has CallerUserId==doctor.UserId).
                    var managerPerm = await _db.ManagerPermissions.FirstOrDefaultAsync(p => p.ManagerId == dto.RequestingManagerId.Value);
                    if (managerPerm == null || !managerPerm.AssignPaToPatient)
                        return Ok(new { IsSuccess = false, Message = "You do not have permission to assign a PA." });

                    if (dto.ClinicId.HasValue)
                    {
                        var hasAccess = await _db.ManagerAccess.AnyAsync(a =>
                            a.ManagerId == dto.RequestingManagerId.Value && a.ClinicId == dto.ClinicId.Value);
                        if (!hasAccess)
                            return Ok(new { IsSuccess = false, Message = "This clinic is outside your access." });
                    }
                }
                else
                {
                    // Both current doctor callers (bookings.page.ts, vaccine.page.ts) create on
                    // behalf of the doctor whose session they're in — verify the caller actually
                    // owns DoctorId rather than trusting it as sent.
                    var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == dto.DoctorId);
                    if (doctor == null || !await VerifyCaller(dto.CallerUserId, dto.SecurityStamp) || dto.CallerUserId!.Value != doctor.UserId)
                        return Ok(new { IsSuccess = false, Message = "Not authorised to create this assignment." });
                }

                // Each press of the person icon creates its OWN assignment for ONE vaccine group.
                // A child may have many open assignments, even to the same PA; they are never
                // merged (PA_MULTI_ASSIGNMENT_SPEC.md). The only thing that blocks a new one is a
                // dose that an OPEN assignment already pins — a dose has exactly one owner.
                var requestedIds = (dto.ScheduleIds ?? new List<long>())
                    .Concat(dto.GroupScheduleIds ?? new List<long>()).Distinct().ToList();
                if (requestedIds.Count > 0)
                {
                    var holder = await (from l in _db.PAAssignmentSchedules
                                        join a in _db.PAAssignments on l.AssignmentId equals a.Id
                                        where requestedIds.Contains(l.ScheduleId) && a.ChildId == dto.ChildId
                                              && !a.IsCancelled && !a.IsCashConfirmedByDoctor
                                        orderby a.AssignedAt descending
                                        select a).FirstOrDefaultAsync();
                    if (holder != null)
                    {
                        var holderPa = await _db.PersonalAssistant.FindAsync(holder.PersonalAssistantId);
                        var holderName = holderPa?.Name ?? "another PA";
                        return Ok(new
                        {
                            IsSuccess = false,
                            Message = $"This vaccine group is already assigned to {holderName}. Cancel or reassign that assignment instead.",
                            BlockedByAssignment = true,
                            BlockingAssignmentId = holder.Id,
                            BlockingPaId = holder.PersonalAssistantId,
                            BlockingPaName = holderName,
                            BlockingAssignedDate = holder.AssignedAt.AddHours(Pkt.OffsetHours).ToString("dd MMM yyyy"),
                            BlockingClinicId = holder.ClinicId,
                            BlockingHasInvoice = holder.InvoiceSubmissionId.HasValue,
                            BlockingInvoiceSubmissionId = holder.InvoiceSubmissionId,
                            AlreadyConfirmedButUnsynced = false,
                            BlockingConfirmedAt = (DateTime?)null
                        });
                    }
                }
                if (dto.BookingId.HasValue
                    && await _db.PAAssignments.AnyAsync(a => a.BookingId == dto.BookingId && !a.IsCancelled))
                    return Ok(new { IsSuccess = false, Message = "This booking already has a PA assignment." });

                // PA Daily Route: a home booking makes the assignment a scheduled visit automatically;
                // otherwise only the explicit flag does. Nothing is inferred from date/window/area.
                var scheduled = dto.IsScheduledVisit == true;
                if (!scheduled && dto.BookingId.HasValue)
                    scheduled = await _db.Bookings.AnyAsync(b => b.Id == dto.BookingId.Value && b.Type == "HomeBooked");
                var visitErr = VaccineAPI.Services.VisitStatus.Validate(scheduled, dto.TargetDate, dto.WindowStart, dto.WindowMinutes,
                    dto.Area, true, Pkt.Today(), out var visitWs, out var visitWm, out var visitArea);
                if (visitErr != null)
                    return Ok(new { IsSuccess = false, Message = visitErr });

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
                    CreatedByManagerId  = dto.RequestingManagerId,
                    TargetDate          = dto.TargetDate,
                    BookingId           = dto.BookingId,
                    IsScheduledVisit    = scheduled,
                    WindowStart         = scheduled ? visitWs : null,
                    WindowMinutes       = (short)visitWm,
                    Area                = scheduled ? visitArea : null
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

                // The clicked group's GIVEN doses (e.g. the doctor gave them and downloaded the invoice
                // before assigning a PA to collect): the doctor's click is the explicit scope, so
                // they are pinned here and the PA becomes their collector. Doses already on a
                // doctor-confirmed invoice belong to a closed visit and are left out. An unclaimed
                // (no PA) unconfirmed invoice is adopted only when ALL its doses are in this group;
                // an invoice that also covers another group is never adopted.
                int excludedStaleDoses = 0;
                long? bookingConfirmedId = null;
                bool spanningInvoiceNotAdopted = false;
                var clickedGroupIds = dto.GroupScheduleIds ?? new List<long>();
                if (clickedGroupIds.Count > 0)
                {
                    var givenInGroup = await _db.Schedules
                        .Where(s => clickedGroupIds.Contains(s.Id) && s.ChildId == dto.ChildId && s.IsDone)
                        .ToListAsync();
                    var groupInvoiceIds = givenInGroup.Where(s => s.InvoiceSubmissionId.HasValue)
                        .Select(s => s.InvoiceSubmissionId!.Value).Distinct().ToList();
                    var groupInvoices = await _db.InvoiceSubmissions
                        .Where(i => groupInvoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
                    var pinnedInvoiceIds = new HashSet<long>();
                    var pa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
                    var paName = pa?.Name ?? "PA";

                    foreach (var s in givenInGroup)
                    {
                        InvoiceSubmission? inv = null;
                        if (s.InvoiceSubmissionId.HasValue) groupInvoices.TryGetValue(s.InvoiceSubmissionId.Value, out inv);
                        if (inv != null && inv.IsConfirmedByDoctor) { excludedStaleDoses++; continue; }
                        if (!_db.PAAssignmentSchedules.Any(l => l.AssignmentId == assignment.Id && l.ScheduleId == s.Id))
                            _db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = assignment.Id, ScheduleId = s.Id });
                        if (!s.IsPaymentCollected && s.PaymentCollectorPaId != dto.PersonalAssistantId)
                        {
                            s.PaymentCollectorPaId = dto.PersonalAssistantId;
                            _db.Entry(s).State = EntityState.Modified;
                        }
                        if (inv != null) pinnedInvoiceIds.Add(inv.Id);
                    }

                    foreach (var invId in pinnedInvoiceIds)
                    {
                        var inv = groupInvoices[invId];
                        if (inv.PaId != null) continue;                       // already belongs to a PA
                        var coversOtherGroup = await _db.Schedules.AnyAsync(x => x.InvoiceSubmissionId == invId && !clickedGroupIds.Contains(x.Id));
                        if (coversOtherGroup) { spanningInvoiceNotAdopted = true; continue; }
                        inv.PaId = dto.PersonalAssistantId;
                        inv.SubmittedByLabel = dto.RequestingManagerId.HasValue
                            ? "Manager/(" + paName + ")"
                            : "Doctor/(" + paName + ")";
                        if (inv.ClinicId == null && dto.ClinicId.HasValue)
                            inv.ClinicId = dto.ClinicId;
                        _db.Entry(inv).State = EntityState.Modified;
                    }
                    if (pinnedInvoiceIds.Count == 1 && !assignment.InvoiceSubmissionId.HasValue)
                    {
                        assignment.InvoiceSubmissionId = pinnedInvoiceIds.First();
                        _db.Entry(assignment).State = EntityState.Modified;
                    }
                    await _db.SaveChangesAsync();
                }

                // Assigning a PA to a child with a pending home-visit booking confirms that booking
                // and tells the parent (same "Booking Confirmed" notification BookingController
                // writes). The booking the doctor linked (BookingId) wins; otherwise the child's
                // pending home-visit booking whose preferred date is closest to the assigned
                // group's date. Never lets a booking problem fail the assignment.
                try
                {
                    Booking? bookingToConfirm = null;
                    if (dto.BookingId.HasValue)
                        bookingToConfirm = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == dto.BookingId.Value && b.Status == "Pending");
                    if (bookingToConfirm == null && !dto.BookingId.HasValue)
                    {
                        var pendingBookings = await _db.Bookings
                            .Where(b => b.ChildId == dto.ChildId && b.Type == "HomeBooked" && b.Status == "Pending")
                            .ToListAsync();
                        if (pendingBookings.Count > 0)
                        {
                            var groupDate = (await _db.Schedules.Where(x => requestedIds.Contains(x.Id))
                                                .Select(x => (DateTime?)x.Date).OrderBy(x => x).FirstOrDefaultAsync())
                                            ?? Pkt.NowWall();
                            bookingToConfirm = pendingBookings
                                .OrderBy(b => Math.Abs(((b.PreferredDate ?? b.CreatedAt).Date - groupDate.Date).TotalDays))
                                .First();
                        }
                    }
                    if (bookingToConfirm != null)
                    {
                        bookingToConfirm.Status = "Confirmed";
                        bookingToConfirm.UpdatedAt = DateTime.UtcNow;
                        if (!string.IsNullOrWhiteSpace(dto.BookingDoctorComment))
                            bookingToConfirm.DoctorComment = dto.BookingDoctorComment;
                        _db.Notifications.Add(new Notification
                        {
                            Type = "BookingConfirmed",
                            RecipientType = "PARENT",
                            RecipientId = bookingToConfirm.ParentUserId,
                            BookingId = bookingToConfirm.Id,
                            ChildId = bookingToConfirm.ChildId,
                            ClinicId = bookingToConfirm.ClinicId,
                            Title = "Booking Confirmed",
                            Message = "Your " + (bookingToConfirm.Type == "HomeBooked" ? "home visit" : "clinic visit") +
                                " booking for " + bookingToConfirm.ChildName + " has been confirmed." +
                                (string.IsNullOrEmpty(bookingToConfirm.DoctorComment) ? "" : " Note: " + bookingToConfirm.DoctorComment),
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                        await _db.SaveChangesAsync();
                        bookingConfirmedId = bookingToConfirm.Id;
                    }
                }
                catch (Exception)
                {
                    // booking confirmation is a courtesy step; the assignment itself already succeeded
                }

                // Fire-and-forget email
                var newPa = await _db.PersonalAssistant.FindAsync(dto.PersonalAssistantId);
                if (newPa != null && !string.IsNullOrEmpty(newPa.Email))
                {
                    var createDoctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == dto.DoctorId);
                    var createSender = EmailSenderResolver.Resolve(createDoctor, _db);
                    string createEmailBody = await BuildAssignmentEmailBody(assignment.Id, newPa.UserId, dto.ChildId);
                    _ = Task.Run(() => UserEmail.SendEmail(
                        newPa.Email,
                        createEmailBody,
                        "New Patient Assignment",
                        sender: createSender
                    ));
                }

                // Notify the PARENT that a PA is now coordinating their child's vaccines.
                // In-app notification only (VacParent already polls parent notifications on
                // login). Kept synchronous — cheap DB row on the request's own _db context;
                // backgrounding it would race a disposed context.
                await NotifyParentOfAssignment(dto.ChildId, dto.PersonalAssistantId, dto.ClinicId);

                // Push to the PA (the Notification row is what triggers it).
                var createKid = await _db.Childs.FindAsync(dto.ChildId);
                VaccineAPI.Services.NotifyHelper.Add(_db, "PaNewAssignment", "PA", dto.PersonalAssistantId, "New patient assigned",
                    $"{createKid?.Name ?? "A patient"} has been assigned to you: {await DoseListFor(assignment.Id)}. Open Assignments to see the visit.", dto.ChildId, dto.ClinicId);
                VaccineAPI.Services.AssignmentAudit.Add(_db, "Created", assignment,
                    dto.RequestingManagerId.HasValue ? "MANAGER" : "DOCTOR", dto.RequestingManagerId ?? dto.DoctorId,
                    $"Assigned to PA {dto.PersonalAssistantId}", null,
                    new { paId = dto.PersonalAssistantId, bookingId = dto.BookingId, targetDate = dto.TargetDate });
                await _db.SaveChangesAsync();

                return Ok(new { IsSuccess = true, ResponseData = new { assignment.Id, ExcludedStaleDoses = excludedStaleDoses, SpanningInvoiceNotAdopted = spanningInvoiceNotAdopted, BookingConfirmedId = bookingConfirmedId } });
            }
            catch (Exception ex)
            {
                Console.WriteLine("PAAssignmentController.Create failed: " + (ex.InnerException?.Message ?? ex.Message));
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
            VaccineAPI.Services.AssignmentAudit.Add(_db, "MarkedDone", assignment, paId.HasValue ? "PA" : "DOCTOR", paId ?? assignment.DoctorId,
                "Marked done, cash to hand over to the doctor");

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
            // Was restricted to AssignedAt falling on today — any open assignment made on a
            // prior day (e.g. still PendingHandover, waiting on the doctor) went invisible here
            // while still blocking a new one via Create's own unrestricted duplicate check
            // (same !IsCompleted && !IsCancelled, no date filter). That mismatch showed as
            // "Not assigned to a PA" on the child's vaccine page with Assign PA then failing —
            // confirmed live on 112 children. Now matches Create's own check exactly.
            var raw = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId
                         && !a.IsCompleted
                         && !a.IsCancelled)
                .ToListAsync();

            var childIds = raw.Select(a => a.ChildId).Distinct().ToList();
            var paIds    = raw.Select(a => a.PersonalAssistantId).Distinct().ToList();

            var children = childIds.Any()
                ? await _db.Childs.Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id)
                : new Dictionary<long, VaccineAPI.Models.Child>();

            var pas = paIds.Any()
                ? await _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id)
                : new Dictionary<long, VaccineAPI.Models.PersonalAssistant>();

            // Schedule ids pinned to each assignment, so the child's vaccine page can colour
            // the date-group(s) that belong to it.
            var rawIds = raw.Select(a => a.Id).ToList();
            var links = rawIds.Any()
                ? await _db.PAAssignmentSchedules.Where(l => rawIds.Contains(l.AssignmentId))
                      .Select(l => new { l.AssignmentId, l.ScheduleId }).ToListAsync()
                : null;
            var linkLookup = links == null
                ? new Dictionary<long, List<long>>()
                : links.GroupBy(l => l.AssignmentId).ToDictionary(g => g.Key, g => g.Select(x => x.ScheduleId).ToList());

            var assignments = raw.Select(a => new {
                AssignmentId = a.Id,
                a.AssignedAt,
                a.Notes,
                ScheduleIds  = linkLookup.ContainsKey(a.Id) ? linkLookup[a.Id] : new List<long>(),
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

        // GET /api/PAAssignment/doctor/{doctorId}?clinicId=&paId=&status=&fromDate=&toDate=&requestingManagerId=
        // Doctor-facing tracking list — the gap GetByPA (PA-only, excludes completed) and
        // GetActiveForDoctor (today-only, unfiltered) both leave open. Scoped through the
        // doctor's own clinics (multi-clinic safe, same pattern as
        // PaCashHandoverController.GetReconciliation), with clinic/PA/status/date-range
        // filters all optional. status defaults to "Active" to match the doctor's usual
        // "what's outstanding" view; pass "All" for full history.
        //
        // requestingManagerId: set when a Manager (not the doctor) is calling this same
        // endpoint from the shared PA Assignment Tracking view. Requires ViewPaAssignmentStatus
        // and — unlike the doctor's own unrestricted view — further fences results down to only
        // clinics in that Manager's own ManagerAccess rows, so a Manager can never see
        // assignments at a clinic they don't personally have access to, even though the doctor
        // who owns all of them can.
        [HttpGet("doctor/{doctorId}")]
        public async Task<IActionResult> GetForDoctor(
            long doctorId,
            [FromQuery] long? clinicId = null,
            [FromQuery] long? paId = null,
            [FromQuery] string status = "Active",
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] long? requestingManagerId = null)
        {
            var clinicIds = await _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => c.Id)
                .ToListAsync();

            if (requestingManagerId.HasValue)
            {
                var managerPerm = await _db.ManagerPermissions.FirstOrDefaultAsync(p => p.ManagerId == requestingManagerId.Value);
                if (managerPerm == null || !managerPerm.ViewPaAssignmentStatus)
                    return Ok(new { IsSuccess = false, Message = "You do not have permission to view PA assignments." });

                var managerClinicIds = await _db.ManagerAccess
                    .Where(a => a.ManagerId == requestingManagerId.Value)
                    .Select(a => a.ClinicId)
                    .ToListAsync();
                clinicIds = clinicIds.Where(id => managerClinicIds.Contains(id)).ToList();
            }

            var query = _db.PAAssignments.Where(a =>
                a.DoctorId == doctorId &&
                (a.ClinicId == null ? !requestingManagerId.HasValue : clinicIds.Contains(a.ClinicId.Value)));

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
                    // Excludes PendingCancellation too — those await a doctor decision on the
                    // dedicated GetPendingCancellations view, not action as a normal open task.
                    // Without this, a request that nobody approves/rejects sits here forever
                    // looking exactly like a live assignment (confirmed live: 0 currently stuck,
                    // but the code path allowed it — see project_pa_orphan_cleanup memory).
                    query = query.Where(a => !a.IsCancelled && !a.IsCashConfirmedByDoctor
                                           && !a.IsCompleted && a.AssignmentStatus != "PendingHandover"
                                           && a.AssignmentStatus != "PendingCancellation");
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

            // Stage inputs (PA_TRACKING_MERGE_SPEC.md). Read separately and never serialised, because
            // amounts and payment flags must not reach a manager. The stage and its age are computed
            // here, in one place, so every screen shows the same answer.
            var factRows = scheduleIds.Any()
                ? await _db.Schedules.Where(s => scheduleIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.IsDone, s.Amount, s.IsPaymentCollected, s.InvoiceSubmissionId, s.DoneAt })
                    .ToListAsync()
                : null;
            var factsById = factRows == null ? new Dictionary<long, VaccineAPI.Services.AssignmentStage.DoseFacts>()
                : factRows.ToDictionary(s => s.Id, s => new VaccineAPI.Services.AssignmentStage.DoseFacts
                {
                    IsDone = s.IsDone, Amount = s.Amount ?? 0m, IsPaymentCollected = s.IsPaymentCollected,
                    InvoiceSubmissionId = s.InvoiceSubmissionId, DoneAt = s.DoneAt
                });
            var factInvIds = factsById.Values.Where(f => f.InvoiceSubmissionId.HasValue).Select(f => f.InvoiceSubmissionId!.Value).Distinct().ToList();
            var invSubmittedAt = factInvIds.Any()
                ? await _db.InvoiceSubmissions.Where(i => factInvIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.SubmittedAt)
                : new Dictionary<long, DateTime>();
            foreach (var f in factsById.Values)
                if (f.InvoiceSubmissionId.HasValue && invSubmittedAt.ContainsKey(f.InvoiceSubmissionId.Value))
                    f.InvoiceSubmittedAt = invSubmittedAt[f.InvoiceSubmissionId.Value];
            var nowUtc = DateTime.UtcNow;
            var forManager = requestingManagerId.HasValue;

            var result = raw.Select(a =>
            {
                var child = children.ContainsKey(a.ChildId) ? children[a.ChildId] : null;
                var pa    = pas.ContainsKey(a.PersonalAssistantId) ? pas[a.PersonalAssistantId] : null;

                var doses = scheduleLinks
                    .Where(l => l.AssignmentId == a.Id)
                    .Where(l => schedulesById.ContainsKey(l.ScheduleId))
                    .Select(l => schedulesById[l.ScheduleId])
                    .ToList();

                var stage = VaccineAPI.Services.AssignmentStage.Compute(a.AssignedAt, a.IsCashConfirmedByDoctor, a.CashConfirmedAt,
                    scheduleLinks.Where(l => l.AssignmentId == a.Id && factsById.ContainsKey(l.ScheduleId)).Select(l => factsById[l.ScheduleId]).ToList());
                // A manager only ever sees Assigned / Given: invoice, payment and cash steps are
                // collapsed server-side so the data never leaves the API.
                var stageNo = forManager && stage.Stage > 2 ? 2 : stage.Stage;
                var stageLabel = forManager && stage.Stage > 2 ? "Given" : stage.Label;

                return new
                {
                    Stage         = stageNo,
                    StageLabel    = stageLabel,
                    StageAgeDays  = VaccineAPI.Services.AssignmentStage.AgeDays(stage.Since, nowUtc),
                    AssignmentId  = a.Id,
                    a.AssignedAt,
                    a.TargetDate,
                    a.IsScheduledVisit,
                    WindowStart = VaccineAPI.Services.VisitStatus.FormatHm(a.WindowStart),
                    a.WindowMinutes,
                    a.Area,
                    a.VisitCompletedAt,
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
                    // Never sent to a manager: managers get no cash or financial figure at all.
                    CashAmount = !forManager && a.InvoiceSubmissionId.HasValue && invoiceAmounts.ContainsKey(a.InvoiceSubmissionId.Value)
                        ? invoiceAmounts[a.InvoiceSubmissionId.Value] : (decimal?)null
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // ───────────── PA Daily Route (spec v3.1) ─────────────

        // Doctor-side caller check shared by the route writes: verified session, owns the doctor.
        private async Task<(bool Ok, Doctor? Doctor)> VerifyDoctorCaller(long doctorId, long? userId, string? stamp)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);
            if (doctor == null || !await VerifyCaller(userId, stamp) || userId!.Value != doctor.UserId) return (false, null);
            return (true, doctor);
        }

        // GET /api/PAAssignment/route/{doctorId}?date=yyyy-MM-dd — the day's scheduled visits, ranked and
        // with derived state. Batched like GetForDoctor (no per-row queries). Managers: read-only, fenced
        // to their clinics, completion is a boolean (no payment detail).
        [HttpGet("route/{doctorId}")]
        public async Task<IActionResult> GetRoute(long doctorId, [FromQuery] DateTime? date = null,
            [FromQuery] long? requestingManagerId = null, [FromQuery] long? callerUserId = null, [FromQuery] string? securityStamp = null)
        {
            var forManager = requestingManagerId.HasValue;
            List<long>? managerClinics = null;
            if (forManager)
            {
                var perm = await _db.ManagerPermissions.FirstOrDefaultAsync(p => p.ManagerId == requestingManagerId!.Value);
                if (perm == null || !perm.ViewPaAssignmentStatus)
                    return Ok(new { IsSuccess = false, Message = "You do not have permission to view PA assignments." });
                managerClinics = await _db.ManagerAccess.Where(a => a.ManagerId == requestingManagerId!.Value).Select(a => a.ClinicId).ToListAsync();
            }
            else if (!(await VerifyDoctorCaller(doctorId, callerUserId, securityStamp)).Ok)
                return Ok(new { IsSuccess = false, Message = "Not authorised." });

            var now = Pkt.NowWall();
            var day = (date ?? now).Date;
            var dayEnd = day.AddDays(1);

            var raw = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId && !a.IsCancelled && !a.IsAutoCreated
                            && a.TargetDate >= day && a.TargetDate < dayEnd)
                .ToListAsync();
            if (forManager) raw = raw.Where(a => a.ClinicId.HasValue && managerClinics!.Contains(a.ClinicId.Value)).ToList();

            var atClinic = raw.Count(a => !a.IsScheduledVisit);
            var visits = raw.Where(a => a.IsScheduledVisit).ToList();
            var ids = visits.Select(a => a.Id).ToList();
            var childIds = visits.Select(a => a.ChildId).Distinct().ToList();
            var paIds = visits.Select(a => a.PersonalAssistantId).Distinct().ToList();

            var children = childIds.Any() ? await _db.Childs.Where(c => childIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id) : new Dictionary<long, Child>();
            var pas = paIds.Any() ? await _db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id) : new Dictionary<long, PersonalAssistant>();
            var links = ids.Any() ? await _db.PAAssignmentSchedules.Where(l => ids.Contains(l.AssignmentId)).ToListAsync() : new List<PAAssignmentSchedule>();
            var schedIds = links.Select(l => l.ScheduleId).Distinct().ToList();
            var facts = schedIds.Any()
                ? (await _db.Schedules.Where(s => schedIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.DoseId, s.IsDone, s.Amount, s.IsPaymentCollected, s.GivenDate }).ToListAsync()).ToDictionary(s => s.Id)
                : null;
            var doseIds = facts == null ? new List<long>() : facts.Values.Select(f => f.DoseId).Distinct().ToList();
            var doseNames = doseIds.Any() ? await _db.Doses.Where(d => doseIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name) : new Dictionary<long, string>();

            // Visit counts for the day strip: scheduled visits per day, today for 22 days (same doctor/clinic fence).
            var stripFrom = now.Date;
            var stripRaw = await _db.PAAssignments
                .Where(a => a.DoctorId == doctorId && !a.IsCancelled && !a.IsAutoCreated && a.IsScheduledVisit
                            && a.TargetDate >= stripFrom && a.TargetDate < stripFrom.AddDays(22))
                .Select(a => new { a.TargetDate, a.ClinicId }).ToListAsync();
            if (forManager) stripRaw = stripRaw.Where(a => a.ClinicId.HasValue && managerClinics!.Contains(a.ClinicId.Value)).ToList();
            var dayCounts = stripRaw.GroupBy(a => a.TargetDate!.Value.Date).ToDictionary(g => g.Key.ToString("yyyy-MM-dd"), g => g.Count());
            var pending = new HashSet<long>(ids.Any()
                ? await _db.VaccineRefusals.Where(r => r.Status == "Pending" && ids.Contains(r.AssignmentId)).Select(r => r.AssignmentId).ToListAsync()
                : new List<long>());

            // PA-recorded payment events (AuditPayment). Filter by child + date in SQL, parse the envelope in memory.
            var events = new Dictionary<long, List<DateTime>>();
            if (visits.Any())
            {
                var since = visits.Min(a => a.AssignedAt);
                var logs = await _db.PaActivityLogs
                    .Where(l => l.ActionCode == "AsgPaymentRecorded" && l.PatientId.HasValue && childIds.Contains(l.PatientId.Value) && l.ActionDate >= since)
                    .Select(l => new { l.ActionDate, l.Notes }).ToListAsync();
                var paByAssignment = visits.ToDictionary(a => a.Id, a => a.PersonalAssistantId);
                foreach (var l in logs)
                {
                    try
                    {
                        var env = Newtonsoft.Json.Linq.JObject.Parse(l.Notes ?? "{}");
                        var aid = env.Value<long?>("assignmentId");
                        if (!aid.HasValue || !paByAssignment.TryGetValue(aid.Value, out var assignedPa)) continue;
                        if (env.Value<string>("actor") != "PA" || env.Value<long?>("actorId") != assignedPa) continue;
                        if (!events.ContainsKey(aid.Value)) events[aid.Value] = new List<DateTime>();
                        events[aid.Value].Add(l.ActionDate);
                    }
                    catch { }
                }
            }

            VaccineAPI.Services.VisitStatus.Stop ToStop(PAAssignment a, bool hidePayment)
            {
                var doseFacts = links.Where(l => l.AssignmentId == a.Id && facts != null && facts.ContainsKey(l.ScheduleId))
                    .Select(l => facts![l.ScheduleId]).ToList();
                return new VaccineAPI.Services.VisitStatus.Stop
                {
                    Id = a.Id, PersonalAssistantId = a.PersonalAssistantId, TargetDate = a.TargetDate, WindowStart = a.WindowStart,
                    WindowMinutes = a.WindowMinutes, VisitOrder = a.VisitOrder, AssignedAt = a.AssignedAt,
                    VisitCompletedAt = hidePayment ? null : a.VisitCompletedAt, RefusalPending = pending.Contains(a.Id),
                    Doses = hidePayment ? new List<(decimal, bool)>() : doseFacts.Select(f => ((f.Amount ?? 0m), f.IsPaymentCollected)).ToList(),
                    PaPaymentEvents = hidePayment || !events.ContainsKey(a.Id) ? new List<DateTime>() : events[a.Id]
                };
            }

            var minute = (int)now.TimeOfDay.TotalMinutes;
            var computed = VaccineAPI.Services.VisitStatus.Compute(visits.Select(a => ToStop(a, false)), day.Date == now.Date ? now.Date : now.Date, minute)
                .ToDictionary(r => r.Id);
            if (forManager)
            {
                // Managers get a boolean: paid-pending (payment detail) falls back to the time-based state.
                var fallback = VaccineAPI.Services.VisitStatus.Compute(visits.Select(a => ToStop(a, true)), now.Date, minute).ToDictionary(r => r.Id);
                foreach (var a in visits)
                    if (computed[a.Id].State == VaccineAPI.Services.VisitStatus.PaidPending) computed[a.Id] = fallback[a.Id];
            }

            var stops = visits.Select(a =>
            {
                var r = computed[a.Id];
                var child = children.ContainsKey(a.ChildId) ? children[a.ChildId] : null;
                var given = links.Where(l => l.AssignmentId == a.Id && facts != null && facts.ContainsKey(l.ScheduleId))
                    .Select(l => facts![l.ScheduleId]).Where(f => f.IsDone && f.GivenDate.HasValue).Select(f => f.GivenDate!.Value).ToList();
                return new
                {
                    AssignmentId = a.Id,
                    PaId = a.PersonalAssistantId,
                    PaName = pas.ContainsKey(a.PersonalAssistantId) ? pas[a.PersonalAssistantId].Name : "",
                    ChildId = a.ChildId,
                    ChildName = child?.Name ?? "",
                    Area = a.Area,
                    WindowStart = VaccineAPI.Services.VisitStatus.FormatHm(a.WindowStart),
                    WindowMinutes = (int)a.WindowMinutes,
                    WindowEnd = a.WindowStart.HasValue ? VaccineAPI.Services.VisitStatus.FormatHm(a.WindowStart.Value.Add(TimeSpan.FromMinutes(a.WindowMinutes))) : null,
                    r.StopNumber,
                    r.State,
                    r.NeedsTime,
                    CompletedAt = r.CompletedAt.HasValue ? Pkt.Iso(r.CompletedAt.Value) : null,
                    RefusalPending = pending.Contains(a.Id),
                    ExplicitCompletion = !forManager && a.VisitCompletedAt.HasValue,
                    DoseCount = links.Count(l => l.AssignmentId == a.Id),
                    Doses = links.Where(l => l.AssignmentId == a.Id && facts != null && facts.ContainsKey(l.ScheduleId))
                        .Select(l => facts![l.ScheduleId]).Select(f => new { Name = doseNames.ContainsKey(f.DoseId) ? doseNames[f.DoseId] : "", Given = f.IsDone }).ToList(),
                    PapersDate = given.Any() ? given.Max() : (DateTime?)null,
                    NoCharge = !forManager && links.Any(l => l.AssignmentId == a.Id)
                               && !links.Where(l => l.AssignmentId == a.Id && facts != null && facts.ContainsKey(l.ScheduleId)).Any(l => (facts![l.ScheduleId].Amount ?? 0m) > 0),
                    CashConfirmed = a.IsCashConfirmedByDoctor
                };
            }).OrderBy(s => s.PaName).ThenBy(s => s.StopNumber).ToList();

            return Ok(new
            {
                IsSuccess = true,
                ResponseData = new
                {
                    Date = day.ToString("yyyy-MM-dd"),
                    PktNow = now.ToString("HH:mm"),
                    PktToday = now.ToString("yyyy-MM-dd"),
                    AtClinicCount = atClinic,
                    DayCounts = dayCounts,
                    Stops = stops
                }
            });
        }

        public class VisitEditDto
        {
            public long DoctorId { get; set; }
            public DateTime? TargetDate { get; set; }
            public string? WindowStart { get; set; }
            public int? WindowMinutes { get; set; }
            public string? Area { get; set; }
            public long? CallerUserId { get; set; }
            public string? SecurityStamp { get; set; }
        }

        public class VisitActionDto
        {
            public long DoctorId { get; set; }
            public string? Direction { get; set; }
            public bool Confirm { get; set; }
            public long? CallerUserId { get; set; }
            public string? SecurityStamp { get; set; }
        }

        private static bool VisitOpen(PAAssignment a) => a.IsScheduledVisit && !a.IsCancelled && !a.IsCashConfirmedByDoctor;

        // PATCH {id}/visit — doctor edits date / window / area as entered (R2, R3).
        [HttpPatch("{id}/visit")]
        public async Task<IActionResult> EditVisit(long id, [FromBody] VisitEditDto dto)
        {
            var a = await _db.PAAssignments.FindAsync(id);
            if (a == null || a.DoctorId != dto.DoctorId) return Ok(new { IsSuccess = false, Message = "Assignment not found" });
            if (!(await VerifyDoctorCaller(dto.DoctorId, dto.CallerUserId, dto.SecurityStamp)).Ok)
                return Ok(new { IsSuccess = false, Message = "Not authorised." });
            if (!VisitOpen(a)) return Ok(new { IsSuccess = false, Message = "This is not an open scheduled visit." });

            var dateMoved = dto.TargetDate?.Date != a.TargetDate?.Date;
            var err = VaccineAPI.Services.VisitStatus.Validate(true, dto.TargetDate, dto.WindowStart, dto.WindowMinutes, dto.Area,
                dateMoved && dto.TargetDate.HasValue && dto.TargetDate.Value.Date < Pkt.Today().Date, Pkt.Today(), out var ws, out var wm, out var area);
            if (err != null) return Ok(new { IsSuccess = false, Message = err });

            var before = $"{a.TargetDate:dd MMM} {VaccineAPI.Services.VisitStatus.FormatHm(a.WindowStart) ?? "TBD"} {a.Area}";
            a.TargetDate = dto.TargetDate?.Date;
            a.WindowStart = ws;
            a.WindowMinutes = (short)wm;
            a.Area = area;
            if (dateMoved || ws == null) a.VisitOrder = null;
            VaccineAPI.Services.AssignmentAudit.Add(_db, "VisitEdited", a, "DOCTOR", a.DoctorId,
                $"Visit changed from {before.Trim()} to {a.TargetDate:dd MMM} {VaccineAPI.Services.VisitStatus.FormatHm(a.WindowStart) ?? "TBD"} {a.Area}".Trim());
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) { return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message }); }
            return Ok(new { IsSuccess = true });
        }

        // PATCH {id}/visit-order — doctor moves a stop up or down within its PA's day (R1, R2).
        [HttpPatch("{id}/visit-order")]
        public async Task<IActionResult> MoveVisit(long id, [FromBody] VisitActionDto dto)
        {
            var a = await _db.PAAssignments.FindAsync(id);
            if (a == null || a.DoctorId != dto.DoctorId) return Ok(new { IsSuccess = false, Message = "Assignment not found" });
            if (!(await VerifyDoctorCaller(dto.DoctorId, dto.CallerUserId, dto.SecurityStamp)).Ok)
                return Ok(new { IsSuccess = false, Message = "Not authorised." });
            if (!VisitOpen(a) || !a.TargetDate.HasValue) return Ok(new { IsSuccess = false, Message = "This is not an open scheduled visit with a date." });
            var up = dto.Direction == "up";
            if (!up && dto.Direction != "down") return Ok(new { IsSuccess = false, Message = "Direction must be up or down." });

            var day = a.TargetDate.Value.Date;
            var peers = (await _db.PAAssignments
                .Where(x => x.DoctorId == a.DoctorId && x.PersonalAssistantId == a.PersonalAssistantId && x.IsScheduledVisit
                            && !x.IsCancelled && !x.IsCashConfirmedByDoctor && x.TargetDate >= day && x.TargetDate < day.AddDays(1))
                .ToListAsync())
                .Select(x => new { A = x, S = new VaccineAPI.Services.VisitStatus.Stop { Id = x.Id, WindowStart = x.WindowStart, VisitOrder = x.VisitOrder } })
                .OrderBy(x => x.S, VaccineAPI.Services.VisitStatus.Order).ToList();
            var idx = peers.FindIndex(x => x.A.Id == a.Id);
            var ni = up ? idx - 1 : idx + 1;
            if (idx < 0 || ni < 0 || ni >= peers.Count) return Ok(new { IsSuccess = true, NoChange = true });
            var nb = peers[ni].A;

            if (a.WindowStart != nb.WindowStart)
            {
                if (!a.WindowStart.HasValue)
                    return Ok(new { IsSuccess = false, NeedsTime = true, Message = "Set a time for this stop first. Stops without a time always come last." });
                if (!nb.WindowStart.HasValue)
                    return Ok(new { IsSuccess = false, Message = "Stops without a time always come last. Remove the time on this stop to place it there." });
                if (!dto.Confirm)
                    return Ok(new { IsSuccess = false, NeedsConfirm = true, AdoptWindowStart = VaccineAPI.Services.VisitStatus.FormatHm(nb.WindowStart),
                                    Message = "Moving past this stop changes the time to " + VaccineAPI.Services.VisitStatus.FormatHm(nb.WindowStart) + "." });
                a.WindowStart = nb.WindowStart;
            }

            // Renumber the same-time group so the moved stop sits just before (up) or after (down) the neighbour.
            var group = peers.Select(x => x.A).Where(x => x.WindowStart == nb.WindowStart && x.Id != a.Id).ToList();
            var at = group.FindIndex(x => x.Id == nb.Id);
            group.Insert(up ? at : at + 1, a);
            for (var i = 0; i < group.Count; i++) group[i].VisitOrder = i + 1;
            VaccineAPI.Services.AssignmentAudit.Add(_db, "VisitReordered", a, "DOCTOR", a.DoctorId, "Stop moved " + (up ? "earlier" : "later"));
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) { return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message }); }
            return Ok(new { IsSuccess = true });
        }

        // PATCH {id}/visit-complete — explicit completion for no-charge visits only (spec 4). The assigned PA
        // (today or earlier) or the doctor on the PA's behalf.
        [HttpPatch("{id}/visit-complete")]
        public async Task<IActionResult> CompleteVisit(long id, [FromBody] VisitActionDto dto)
        {
            var a = await _db.PAAssignments.FindAsync(id);
            if (a == null) return Ok(new { IsSuccess = false, Message = "Assignment not found" });
            if (!await VerifyCaller(dto.CallerUserId, dto.SecurityStamp)) return Ok(new { IsSuccess = false, Message = "Not authorised." });

            string actorType; long actorId;
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == a.DoctorId);
            var pa = await _db.PersonalAssistant.FirstOrDefaultAsync(p => p.Id == a.PersonalAssistantId);
            if (doctor != null && dto.CallerUserId == doctor.UserId) { actorType = "DOCTOR"; actorId = a.DoctorId; }
            else if (pa != null && dto.CallerUserId == pa.UserId)
            {
                actorType = "PA"; actorId = pa.Id;
                if (a.TargetDate.HasValue && a.TargetDate.Value.Date > Pkt.Today().Date)
                    return Ok(new { IsSuccess = false, Message = "A visit cannot be completed before its date." });
            }
            else return Ok(new { IsSuccess = false, Message = "Not authorised." });

            if (!VisitOpen(a) || a.IsCompleted) return Ok(new { IsSuccess = false, Message = "This is not an open scheduled visit." });
            if (a.VisitCompletedAt.HasValue) return Ok(new { IsSuccess = true, AlreadyCompleted = true });

            var pinned = await (from l in _db.PAAssignmentSchedules join s in _db.Schedules on l.ScheduleId equals s.Id
                                where l.AssignmentId == a.Id
                                select new { s.Amount, s.IsDone, s.Weight, s.Height, s.Circle, s.GrowthNotMeasured }).ToListAsync();
            if (pinned.Any(p => (p.Amount ?? 0m) > 0))
                return Ok(new { IsSuccess = false, Message = "This visit has a charge. It completes when the PA records payment." });
            if (!pinned.Any(p => p.IsDone))
                return Ok(new { IsSuccess = false, Message = "Give at least one dose before completing the visit." });
            if (!pinned.Any(p => p.IsDone && (p.Weight.HasValue || p.Height.HasValue || p.Circle.HasValue || p.GrowthNotMeasured)))
                return Ok(new { IsSuccess = false, Message = "Record growth or tick Not measured before completing the visit." });

            a.VisitCompletedAt = DateTime.UtcNow;
            VaccineAPI.Services.AssignmentAudit.Add(_db, "VisitCompleted", a, actorType, actorId,
                actorType == "DOCTOR" ? "Visit marked completed by the doctor" : "Visit marked completed by the PA");
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) { return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message }); }
            return Ok(new { IsSuccess = true });
        }

        // PATCH {id}/visit-reopen — doctor reopens an EXPLICIT completion (a derived one follows the payment).
        [HttpPatch("{id}/visit-reopen")]
        public async Task<IActionResult> ReopenVisit(long id, [FromBody] VisitActionDto dto)
        {
            var a = await _db.PAAssignments.FindAsync(id);
            if (a == null || a.DoctorId != dto.DoctorId) return Ok(new { IsSuccess = false, Message = "Assignment not found" });
            if (!(await VerifyDoctorCaller(dto.DoctorId, dto.CallerUserId, dto.SecurityStamp)).Ok)
                return Ok(new { IsSuccess = false, Message = "Not authorised." });
            if (!a.VisitCompletedAt.HasValue) return Ok(new { IsSuccess = false, Message = "Only an explicit completion can be reopened here. A payment-based completion follows the payment." });
            a.VisitCompletedAt = null;
            VaccineAPI.Services.AssignmentAudit.Add(_db, "VisitReopened", a, "DOCTOR", a.DoctorId, "Explicit visit completion reopened");
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) { return Ok(new { IsSuccess = false, Message = ex.InnerException?.Message ?? ex.Message }); }
            return Ok(new { IsSuccess = true });
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
        // Why the doctor reassigned (written to the assignment history). Optional, additive.
        public string? Reason { get; set; }
        public DateTime? TargetDate { get; set; }
        public long? RequestingManagerId { get; set; }
        // Required on the doctor-initiated path (RequestingManagerId absent) — ignored when a
        // manager is calling, since that path is fenced by ManagerPermission/ManagerAccess instead.
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
    }

    public class TakeBackDto
    {
        public string? Reason { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
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
        // Given + undone ids of the clicked date-group. Lets the orphan-invoice test recognise a
        // dose from the clicked group even when the group is already fully given (ScheduleIds is
        // undone-only, so it is empty then). Optional: older clients omit it.
        public List<long> GroupScheduleIds { get; set; } = new List<long>();
        public DateTime? TargetDate { get; set; }
        // PA Daily Route (spec v3.1). All optional; older clients omit them.
        public bool? IsScheduledVisit { get; set; }
        public string? WindowStart { get; set; }   // "HH:mm" PKT wall time
        public int? WindowMinutes { get; set; }
        public string? Area { get; set; }
        public long? BookingId { get; set; }
        // Doctor's note saved on the booking when this assignment confirms it.
        public string? BookingDoctorComment { get; set; }
        public long? RequestingManagerId { get; set; }
        // Required on the doctor-initiated path (RequestingManagerId absent) — ignored when a
        // manager is calling, since that path is fenced by ManagerPermission/ManagerAccess instead.
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
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
