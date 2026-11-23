using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Single definition of "this dose carries no money or stock" (HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md).
    // The date is deliberately NOT part of the test.
    public static class PaOwnership
    {
        // OHF (no brand, no stock) AND no invoice AND no fee AND no payment.
        public static bool SkipsPaOwnership(Schedule s)
            => s.BrandId == null
               && s.StockId == null
               && s.InvoiceSubmissionId == null
               && !(s.Amount.HasValue && s.Amount.Value > 0m)
               && !s.IsPaymentCollected;

        // True only when the assignment pins at least one dose, every pinned dose is given, and
        // every one passes SkipsPaOwnership. Evaluated from the DB on every call; never trust the UI.
        public static bool IsRecordOnly(Context db, long assignmentId)
        {
            var doses = (from l in db.PAAssignmentSchedules
                         where l.AssignmentId == assignmentId
                         join s in db.Schedules on l.ScheduleId equals s.Id
                         select s).ToList();
            return doses.Count > 0 && doses.All(d => d.IsDone && SkipsPaOwnership(d));
        }
    }
}
