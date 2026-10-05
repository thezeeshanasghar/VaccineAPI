using System.Linq;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Shared cleanup for infinite/repeating vaccines (Flu, Typhoid, Vitamin A): giving a dose
    // inserts a brand-new future Schedule row (GivenDate + MinGap), with no FK linking it back
    // to the dose that created it. When that dose is later ungiven, the future row it caused
    // must be removed too, or it's left behind as a permanent orphan (e.g. a mis-dated give
    // years in the past leaving a "due" row years in the future that ungive never touches).
    //
    // Mirrors ScheduleController.Delete's existing infinite-dose branch (its normal ungive path
    // for the UNGIVE button), which finds "the" future row by keeping only the earliest undone
    // row per child+vaccine and deleting the rest — there's no real FK to match on, so this is
    // the same coincidental-but-correct-in-practice matching, reused instead of re-copied so a
    // third call site (PAAssignmentController's FullReset cascade) can't silently diverge again.
    public static class InfiniteDoseCleanup
    {
        public static bool IsInfiniteDoseName(string? doseName)
        {
            var name = doseName ?? string.Empty;
            return name.StartsWith("Flu", System.StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Typhoid", System.StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Vitamin A", System.StringComparison.OrdinalIgnoreCase);
        }

        // Removes every undone row for this child+vaccine except one (the ungiven row via keepDate,
        // else the earliest), skipping rows linked to a PA assignment.
        // Call this once per distinct VaccineId after ungiving a dose belonging to that vaccine.
        public static void RemoveExtraUndoneRows(Context db, long childId, long vaccineId, System.DateTime? keepDate = null)
        {
            var undoneSchedules = db.Schedules
                .Include(x => x.Dose)
                .Where(x => x.ChildId == childId
                    && x.Dose.VaccineId == vaccineId
                    && x.IsDone == false
                    && x.IsSkip != true)
                .OrderBy(x => x.Date)
                .ToList();

            if (undoneSchedules.Count <= 1)
                return;

            // keepDate = the Date of the row the caller just ungave. That row must survive: keeping
            // "the earliest" instead deletes it whenever an older (e.g. DOB-anchored) undone row
            // exists. No match, or no keepDate (PA FullReset), falls back to the earliest row.
            var keep = (keepDate.HasValue
                ? undoneSchedules.Where(x => x.Date.Date == keepDate.Value.Date).OrderBy(x => x.Id).FirstOrDefault()
                : null) ?? undoneSchedules[0];

            // Never delete a row an assignment still points at.
            var linkedIds = db.PAAssignmentSchedules
                .Where(l => undoneSchedules.Select(u => u.Id).Contains(l.ScheduleId))
                .Select(l => l.ScheduleId)
                .ToList();

            var schedulesToDelete = undoneSchedules
                .Where(x => x.Id != keep.Id && !linkedIds.Contains(x.Id))
                .ToList();
            db.Schedules.RemoveRange(schedulesToDelete);
        }
    }
}
