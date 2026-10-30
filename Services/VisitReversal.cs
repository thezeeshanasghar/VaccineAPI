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
        public static int CloseEmptied(Context db, IEnumerable<Schedule> ungivenDoses, string note)
        {
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
