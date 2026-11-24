using System;
using System.Collections.Generic;
using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // A doctor ungive that empties a PA visit ends it exactly like an approved "refused at home"
    // request (PA_MULTI_ASSIGNMENT_SPEC.md §11): the assignment is removed, an Approved
    // VaccineRefusal is written (it drives the "refused before: n" count and the golden marker),
    // the audit log keeps a "Reversed" row and the PA is told. The dose, stock and invoice side of
    // the ungive has already run in the caller; nothing here touches them. The caller saves.
    //
    // Only open assignments are looked at (AssignmentResolver.OpenAssignmentForDose), so a
    // cash-confirmed assignment is never closed from here. A visit that still has a given dose
    // is left open.
    public static class VisitReversal
    {
        public const string FlagV2 = "reversal.v2";
        // S4 (UNGIVE_GUARDS_SPEC.md section 5): its own flag, so turning the PA ungive block on does not also change
        // doctor-ungive assignment closing or PA assignment removal, which reversal.v2 does. Seeded OFF.
        public const string FlagUngiveBlock = "pa.ungive.block";
        public const string PaUngiveAfterPaymentMsg = "A PA or manager cannot ungive once an invoice or payment exists. Ask the doctor.";
        // S0: once the doctor has confirmed the cash, a PA or manager can never ungive any dose on that invoice.
        public const string StaffUngiveConfirmedMsg = "Cash already confirmed by the doctor. Ask the doctor.";
        public const string PaCannotReverseMsg = "A PA cannot remove or reverse assignments. Ask the doctor.";

        // PA or manager acting. Role comes from the token when there is one; the claimed ids in the request body are
        // kept as a fallback for non-enforcing auth mode. A doctor token with a stray PaId is not treated as staff.
        public static bool IsPaOrManager(long? paId, long? managerId)
        {
            var role = AuthContext.Current?.Role;
            if (role == "DOCTOR" || role == "SUPERADMIN") return false;
            return role == "PA" || role == "MANAGER" || paId.HasValue || managerId.HasValue;
        }

        // PA_REVERSAL_SPEC.md R5 / S4: with pa.ungive.block (or reversal.v2) on, a PA or manager can never ungive a
        // dose that has an invoice link or a recorded payment. A missing flag row reads as OFF.
        public static bool PaUngiveBlocked(Context db, Schedule dose)
            => (FeatureFlags.IsOn(db, FlagUngiveBlock) || FeatureFlags.IsOn(db, FlagV2))
               && (dose.InvoiceSubmissionId.HasValue || dose.IsPaymentCollected);

        // S0: true when the dose is given and sits on an invoice the doctor already confirmed. Not behind a flag.
        public static bool StaffUngiveOnConfirmedInvoice(Context db, Schedule dose)
            => UngiveReversalGuard.DoctorUngiveWouldDeleteConfirmedInvoice(db, dose);

        // A refused PA/manager ungive leaves one log row (not a reversal request, never shown in the pending list).
        // The refusal rolls the request's transaction back, so the caller builds the row here and saves it with
        // SaveBlockedUngive AFTER the rollback. Logging must never change the refusal.
        public static PaActivityLog BlockedUngiveLog(Context db, long? paId, long? managerId, long suppliedDoctorId, Schedule dose, string reason)
            => new PaActivityLog
            {
                PaId = paId, ManagerId = paId.HasValue ? null : managerId,
                DoctorId = PaDoctor.Resolve(db, paId, managerId, suppliedDoctorId),
                PatientId = dose.ChildId, ActionCode = "UngiveBlocked", IsReversal = false,
                Description = "Ungive refused: " + reason,
                Notes = $"ScheduleId: {dose.Id} | InvoiceSubmissionId: {dose.InvoiceSubmissionId}",
                ActionDate = DateTime.UtcNow
            };

        public static void SaveBlockedUngive(Context db, PaActivityLog? log)
        {
            if (log == null) return;
            try { db.ChangeTracker.Clear(); db.PaActivityLogs.Add(log); db.SaveChanges(); }
            catch { }
        }

        public static int CloseEmptied(Context db, IEnumerable<Schedule> ungivenDoses, string note)
        {
            // PA_REVERSAL_SPEC.md R1: with reversal.v2 on, a plain doctor ungive is a correction. The
            // assignment and pins stay, no refusal is written, the PA gets no "Visit reversed" push.
            // "Client refused" is a separate explicit action (R2), not built here.
            if (FeatureFlags.IsOn(db, FlagV2)) return 0;
            var owners = new Dictionary<long, PAAssignment>();
            foreach (var dose in ungivenDoses)
            {
                var owner = AssignmentResolver.OpenAssignmentForDose(db, dose.Id);
                if (owner != null && !owners.ContainsKey(owner.Id)) owners[owner.Id] = owner;
            }

            var closed = 0;
            foreach (var a in owners.Values)
                if (CloseIfEmpty(db, a, note, "Visit reversed: the doctor ungave the last given dose, nothing left to collect", "DoctorUngive")) closed++;
            return closed;
        }

        // Closes one open assignment when none of its pinned doses is given, no invoice is linked
        // and no payment was taken. Also the self-heal for rows orphaned before CloseEmptied
        // existed: Create calls it on the assignment that blocks a new one. False = left open.
        public static bool CloseIfEmpty(Context db, PAAssignment a, string note, string auditText, string mode)
        {
            if (a.InvoiceSubmissionId.HasValue) return false;
            var pins = db.PAAssignmentSchedules.Where(l => l.AssignmentId == a.Id).ToList();
            var pinnedIds = pins.Select(l => l.ScheduleId).ToList();
            // Tracked rows keep their unsaved values, so the dose ungiven in this request reads as not given.
            var stillGiven = db.Schedules.Where(s => pinnedIds.Contains(s.Id)).ToList().Any(s => s.IsDone || s.IsPaymentCollected);
            if (stillGiven) return false;

            var now = DateTime.UtcNow;
            var pending = db.VaccineRefusals.Where(r => r.AssignmentId == a.Id && r.Status == "Pending").ToList();
            if (pending.Count > 0)
            {
                foreach (var r in pending) { r.Status = "Approved"; r.ResolvedAt = now; }
            }
            else
            {
                db.VaccineRefusals.Add(new VaccineRefusal
                {
                    AssignmentId = a.Id, ChildId = a.ChildId, DoctorId = a.DoctorId, PaId = a.PersonalAssistantId,
                    Reason = note, Status = "Approved", RequestedAt = now, ResolvedAt = now
                });
            }

            AssignmentAudit.Add(db, "Reversed", a, "DOCTOR", a.DoctorId, auditText, note, new { mode, paId = a.PersonalAssistantId });
            var kid = db.Childs.Where(c => c.Id == a.ChildId).Select(c => c.Name).FirstOrDefault();
            NotifyHelper.Add(db, "PaRefusalApproved", "PA", a.PersonalAssistantId, "Visit reversed",
                "The doctor reversed the visit for " + (kid ?? "the patient") + ". Nothing is left to collect.", a.ChildId, a.ClinicId);

            db.PAAssignmentSchedules.RemoveRange(pins);
            db.PAAssignments.Remove(a);
            return true;
        }
    }
}
