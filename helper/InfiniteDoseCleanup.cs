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

        // Removes every undone future row for this child+vaccine except the earliest, leaving
        // exactly one undone row in place (matching ScheduleController.Delete's contract).
        // Call this once per distinct VaccineId after ungiving a dose belonging to that vaccine.
        public static void RemoveExtraUndoneRows(Context db, long childId, long vaccineId)
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

            var schedulesToDelete = undoneSchedules.Skip(1).ToList();
            db.Schedules.RemoveRange(schedulesToDelete);
        }
    }
}
