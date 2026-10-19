using System;
using System.Collections.Generic;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Single "does this dose belong to this PA visit?" test, shared by every place that adopts
    // an invoice into a PA assignment (PAAssignmentController.Create's orphan-invoice block,
    // ScheduleController.SyncInvoicePaToActiveAssignment, the update-bulk-invoice pin loop and
    // ResolveAssignmentForInvoice's self-heal). Before this, each site pinned/stamped EVERY dose
    // on the invoice, so an old doctor-given dose on a months-old (often "Historical Backfill")
    // invoice got swept into today's assignment and stamped as the PA's uncollected cash.
    // Root cause + scale: project_pa_assignment_stale_pin_orphan_invoice_2026_10_19.
    public static class PaVisitGuard
    {
        // A dose still owed (unpaid, invoice unconfirmed) stays collectable for this long after
        // it was given, so "doctor gives Monday, assigns a PA Thursday" is not lost.
        private const int UnpaidWindowDays = 7;
        // Same-visit tolerance, matching the 1-day window used elsewhere in the PA flow.
        private const int SameVisitDays = 1;

        // True when the dose legitimately belongs to the visit that started at assignedAtUtc.
        // groupScheduleIds = ids of the group the doctor clicked (given + undone), when known.
        public static bool DoseBelongsToVisit(
            Schedule s,
            InvoiceSubmission invoice,
            DateTime assignedAtUtc,
            long assignedPaId,
            ICollection<long>? groupScheduleIds = null)
        {
            if (!s.IsDone) return true;                                   // undone: assignment's own work
            if (s.GivenByPaId == assignedPaId) return true;               // the PA gave it: always theirs
            if (groupScheduleIds != null && groupScheduleIds.Contains(s.Id)) return true;

            // DoneAt (UTC) is the real give time. GivenDate is deliberately back-datable for
            // catch-up entries, so it is only a fallback for legacy rows with no DoneAt.
            var gaveAt = s.DoneAt ?? s.GivenDate;
            if (gaveAt == null) return false;

            if (gaveAt.Value >= assignedAtUtc.AddDays(-SameVisitDays)) return true;

            // Still owed money on a not-yet-confirmed invoice, given recently: keep collectable.
            if (invoice != null
                && !invoice.IsConfirmedByDoctor
                && !s.IsPaymentCollected
                && (s.Amount ?? 0) > 0
                && gaveAt.Value >= assignedAtUtc.AddDays(-UnpaidWindowDays))
                return true;

            return false;
        }
    }
}
