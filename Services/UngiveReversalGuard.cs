using System;
using System.Collections.Generic;
using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // A PA ungive of a dose that already has an invoice or payment writes a pending "UngiveAfterPayment"
    // request (paactivitylogs, IsReversal). The ungive itself clears Schedules.InvoiceSubmissionId, so a
    // quick re-give (live case: Hakeem Ullah, 19 Nov 2026, re-given 7 seconds later) left the invoice
    // active with no dose pointing at it: a leftover "Awaiting Invoice" row, and a stale request whose
    // Approve would have un-paid a given dose. Notes format written by ScheduleController:
    //   "Amount pending reversal: X | ScheduleId: Y | InvoiceSubmissionId: Z"
    public static class UngiveReversalGuard
    {
        public const string Code = "UngiveAfterPayment";

        public static (long ScheduleId, long InvoiceId) ParseNotes(string notes)
        {
            long sid = 0, iid = 0;
            var n = notes ?? "";
            var s = n.IndexOf("ScheduleId: ", StringComparison.Ordinal);
            if (s >= 0)
            {
                var rest = n.Substring(s + 12);
                var end = rest.IndexOf(" |", StringComparison.Ordinal);
                long.TryParse((end >= 0 ? rest.Substring(0, end) : rest).Trim(), out sid);
            }
            var i = n.IndexOf("InvoiceSubmissionId: ", StringComparison.Ordinal);
            if (i >= 0)
            {
                var rest = n.Substring(i + 21);
                var end = rest.IndexOf(" |", StringComparison.Ordinal);
                long.TryParse((end >= 0 ? rest.Substring(0, end) : rest).Trim(), out iid);
            }
            return (sid, iid);
        }

        // "Amount pending reversal: X" is the dose Amount captured before the ungive nulled it. 0 when absent.
        public static decimal ParseAmount(string notes)
        {
            var n = notes ?? "";
            const string key = "Amount pending reversal: ";
            var a = n.IndexOf(key, StringComparison.Ordinal);
            if (a < 0) return 0m;
            var rest = n.Substring(a + key.Length);
            var end = rest.IndexOf(" |", StringComparison.Ordinal);
            decimal.TryParse((end >= 0 ? rest.Substring(0, end) : rest).Trim(),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt);
            return amt < 0 ? 0m : amt;
        }

        // A request older than this is never used to relink a dose: it is history, not a live correction.
        public static readonly TimeSpan RelinkWindow = TimeSpan.FromDays(1);

        public const string ConfirmedInvoiceUngiveMsg =
            "This dose is on an invoice the doctor already confirmed. Ungiving it would delete that confirmed invoice, so it is blocked. Adjust or refund through the invoice instead.";

        // A doctor ungive of a given dose deletes the invoice the dose is linked to. For a doctor-confirmed
        // invoice that erases a settled cash record with no trace, so it is refused. True = refuse.
        public static bool DoctorUngiveWouldDeleteConfirmedInvoice(Context db, Schedule dose)
        {
            if (!dose.IsDone || !dose.InvoiceSubmissionId.HasValue) return false;
            var id = dose.InvoiceSubmissionId.Value;
            return db.InvoiceSubmissions.Any(i => i.Id == id && i.IsConfirmedByDoctor);
        }

        // Given, paid doses pinned to an assignment that owns this invoice but not linked to any invoice:
        // confirming would settle the visit while that money has no invoice behind it (the Hakeem state).
        public static List<long> UnlinkedPaidDosesOfInvoiceOwners(Context db, long invoiceId)
        {
            var owners = AssignmentResolver.OwnersOfInvoice(db, invoiceId).Where(a => !a.IsCashConfirmedByDoctor).Select(a => a.Id).ToList();
            if (owners.Count == 0) return new List<long>();
            return (from l in db.PAAssignmentSchedules
                    join sc in db.Schedules on l.ScheduleId equals sc.Id
                    where owners.Contains(l.AssignmentId) && sc.IsDone && sc.IsPaymentCollected && !sc.InvoiceSubmissionId.HasValue
                    select sc.Id).Distinct().ToList();
        }

        public static IQueryable<PaActivityLog> Pending(Context db) =>
            db.PaActivityLogs.Where(l => l.ActionCode == Code && l.IsReversal
                                         && !l.IsReversalApproved && l.IsReversalRejected != true);

        // A dose is given again while its ungive request is still undecided: put it back on the invoice the
        // request names. Bounded (D1): the request must be under RelinkWindow old, written for the same PA that
        // now gives the dose, and the invoice must be active, unconfirmed, with money, for the same child.
        // Restores (D3): the dose Amount from the request note (the ungive nulled it) and the per-dose
        // certificate row the ungive voided. PaymentMode / IsPaymentCollected are NOT restored: the PA
        // re-records the payment. The request itself is left for the doctor (it shows stale and cannot be
        // approved). Caller saves.
        public static bool RelinkOnRegive(Context db, Schedule dose)
        {
            if (!dose.IsDone || dose.InvoiceSubmissionId.HasValue) return false;
            var needle = "ScheduleId: " + dose.Id + " |";
            var since = DateTime.UtcNow - RelinkWindow;
            var logs = Pending(db)
                .Where(l => l.PatientId == dose.ChildId && l.Notes.Contains(needle) && l.ActionDate >= since)
                .OrderByDescending(l => l.Id)
                .ToList();
            foreach (var l in logs)
            {
                if (l.PaId.HasValue && dose.GivenByPaId.HasValue && l.PaId.Value != dose.GivenByPaId.Value) continue;
                var p = ParseNotes(l.Notes);
                if (p.ScheduleId != dose.Id || p.InvoiceId <= 0) continue;
                var ok = db.InvoiceSubmissions.Any(i => i.Id == p.InvoiceId && i.ChildId == dose.ChildId
                                                        && i.InvoiceStatus != "Cancelled" && i.TotalAmount > 0
                                                        && !i.IsConfirmedByDoctor);
                if (!ok) continue;
                dose.InvoiceSubmissionId = p.InvoiceId;
                var amt = ParseAmount(l.Notes);
                if ((dose.Amount ?? 0) <= 0 && amt > 0) dose.Amount = amt;
                var certDoctorId = PaDoctor.Effective(db, l);
                var cert = db.Invoices.FirstOrDefault(i => i.DoseId == dose.DoseId && i.ChildId == dose.ChildId
                                                           && i.DoctorId == certDoctorId && i.IsVoided
                                                           && i.SupersededBy == "UNGIVEN");
                if (cert != null) { cert.IsVoided = false; cert.SupersededBy = null; }
                return true;
            }
            return false;
        }

        // Pending requests that name this invoice AND whose dose is still not given: the invoice has no
        // vaccine behind it until the doctor decides. Used to flag the row and block Confirm.
        public static List<long> PendingUngiveLogIdsForInvoice(Context db, long invoiceId, long childId)
        {
            var needle = "InvoiceSubmissionId: " + invoiceId;
            var logs = Pending(db).Where(l => l.PatientId == childId && l.Notes.Contains(needle)).ToList();
            var result = new List<long>();
            foreach (var l in logs)
            {
                var p = ParseNotes(l.Notes);
                if (p.InvoiceId != invoiceId || p.ScheduleId <= 0) continue;
                var given = db.Schedules.Where(s => s.Id == p.ScheduleId).Select(s => s.IsDone).FirstOrDefault();
                if (!given) result.Add(l.Id);
            }
            return result;
        }
    }
}
