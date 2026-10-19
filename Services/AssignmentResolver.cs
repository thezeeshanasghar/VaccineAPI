using System;
using System.Collections.Generic;
using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // One place that answers "which PA assignment owns this dose / invoice?". Every assignment is
    // a separate entity (one per pressed vaccine group): ownership is ALWAYS resolved through the
    // pin table (PAAssignmentSchedules), never by "the child's newest open assignment". Before,
    // five hand-copied "newest open assignment for this child" queries picked the wrong
    // assignment as soon as a child had two (cash stamped to the wrong PA, the wrong assignment
    // closed on invoice confirm). See PA_MULTI_ASSIGNMENT_SPEC.md invariants I1-I5.
    public static class AssignmentResolver
    {
        // Open = not cancelled and not cash-confirmed (same definition used everywhere else).
        public static IQueryable<PAAssignment> OpenForChild(Context db, long childId)
        {
            return db.PAAssignments.Where(a => a.ChildId == childId && !a.IsCancelled && !a.IsCashConfirmedByDoctor);
        }

        // The open assignment that pins this dose. Newest wins only for legacy doses that were
        // pinned by two open assignments before the one-open-pin-per-dose rule.
        public static PAAssignment? OpenAssignmentForDose(Context db, long scheduleId)
        {
            return (from l in db.PAAssignmentSchedules
                    join a in db.PAAssignments on l.AssignmentId equals a.Id
                    where l.ScheduleId == scheduleId && !a.IsCancelled && !a.IsCashConfirmedByDoctor
                    orderby a.AssignedAt descending
                    select a).FirstOrDefault();
        }

        // PA who should be stamped as collector for a dose: the PA of the assignment that pins
        // it. If the dose is unpinned, only when every open assignment of the child belongs to
        // the same PA (no guess between different PAs); otherwise null.
        public static long? OwnerPaIdForDose(Context db, long childId, long scheduleId)
        {
            var owner = OpenAssignmentForDose(db, scheduleId);
            if (owner != null) return owner.PersonalAssistantId;
            var pas = OpenForChild(db, childId).Select(a => a.PersonalAssistantId).Distinct().ToList();
            return pas.Count == 1 ? (long?)pas[0] : null;
        }

        // Open assignments that own an invoice: they pin a dose of the child that sits in the
        // same vaccine group (same scheduled Date) as the invoice's doses. One invoice = one
        // group, so normally exactly one.
        public static List<PAAssignment> OpenOwnersOfDoses(Context db, long childId, ICollection<Schedule> invoiceDoses)
        {
            if (invoiceDoses == null || invoiceDoses.Count == 0) return new List<PAAssignment>();
            var dates = invoiceDoses.Select(d => d.Date.Date).Distinct().ToList();
            var ids = (from l in db.PAAssignmentSchedules
                       join a in db.PAAssignments on l.AssignmentId equals a.Id
                       join s in db.Schedules on l.ScheduleId equals s.Id
                       where a.ChildId == childId && !a.IsCancelled && !a.IsCashConfirmedByDoctor
                             && s.ChildId == childId && dates.Contains(s.Date.Date)
                       select a.Id).Distinct().ToList();
            return db.PAAssignments.Where(a => ids.Contains(a.Id)).OrderByDescending(a => a.AssignedAt).ToList();
        }

        // Every non-cancelled assignment that owns an invoice: its single-FK hint plus any
        // assignment pinning a dose that carries this invoice id.
        public static List<PAAssignment> OwnersOfInvoice(Context db, long invoiceId)
        {
            var pinned = (from l in db.PAAssignmentSchedules
                          join s in db.Schedules on l.ScheduleId equals s.Id
                          where s.InvoiceSubmissionId == invoiceId
                          select l.AssignmentId).Distinct().ToList();
            return db.PAAssignments
                .Where(a => !a.IsCancelled && (a.InvoiceSubmissionId == invoiceId || pinned.Contains(a.Id)))
                .OrderByDescending(a => a.AssignedAt)
                .ToList();
        }
    }
}
