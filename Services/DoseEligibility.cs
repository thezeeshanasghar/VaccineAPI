using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Controllers;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // "Could a PA give this dose on this date?" — the date floor/ceiling half of the give-time
    // rules in ScheduleController.Update (Dose.MinAge, Dose.MaxAge, Dose.MinGap + CDC 4-day grace).
    // Used by PAAssignmentController.Create so a dose is never handed to a PA for a date on which the
    // PA would be refused at give time (a PA has no override there). The Dose.DueDate is NOT the test:
    // a dose due 10 Jan may be assigned for 3 Jan if 3 Jan is inside its legal window.
    //
    // Deliberately LESS strict than give-time where give-time depends on state that can still
    // change before the visit: an unrecorded predecessor dose is not a block here (it may be
    // recorded before the visit); give-time still enforces it. Kept in step with the give-time
    // block by hand — if those rules change, change this too (DoseEligibilityTests pins them).
    public static class DoseEligibility
    {
        public sealed class Result
        {
            public bool Ok = true;
            public string? Message;
            // Earliest legal date when blocked for being too early; null otherwise.
            public DateTime? EarliestDate;
        }

        public static Result CanGiveOn(Context db, Schedule schedule, DateTime onDate)
        {
            var ok = new Result();
            if (schedule.IsDisease == true) return ok;            // disease entries have no given date to gate

            var dose = db.Doses.Include(d => d.Vaccine).FirstOrDefault(d => d.Id == schedule.DoseId);
            var child = db.Childs.FirstOrDefault(c => c.Id == schedule.ChildId);
            if (dose == null || child == null) return ok;

            var day = onDate.Date;
            var name = dose.Name ?? "This dose";

            if (dose.MinAge > 0)
            {
                var minAgeDate = ScheduleController.calculateDate(child.DOB, dose.MinAge).Date;
                if (day < minAgeDate)
                    return Early(name, minAgeDate, "minimum age not reached");
            }

            if (dose.MaxAge.HasValue)
            {
                var maxAgeDate = ScheduleController.calculateDate(child.DOB, dose.MaxAge.Value).Date;
                if (day > maxAgeDate)
                    return new Result { Ok = false, Message = name + " cannot be given after " + maxAgeDate.ToString("dd-MM-yyyy") + " (maximum age exceeded)." };
            }

            if ((dose.DoseOrder ?? 0) > 1 && dose.MinGap.HasValue)
            {
                var prevDose = db.Doses.FirstOrDefault(x => x.VaccineId == dose.VaccineId && x.DoseOrder == (dose.DoseOrder - 1));
                var prev = prevDose == null ? null
                    : db.Schedules.FirstOrDefault(x => x.ChildId == schedule.ChildId && x.DoseId == prevDose.Id);

                DateTime? anchor = null;
                if (prev != null && prev.IsDone && prev.GivenDate.HasValue)
                    anchor = prev.GivenDate.Value.Date;
                else if (dose.Vaccine != null && dose.Vaccine.ContainsDTaP
                         && ScheduleController.GetDTaPCoverageCount(db, schedule.ChildId) >= (dose.DoseOrder - 1))
                    anchor = ScheduleController.GetLastDTaPCoverageGivenDate(db, schedule.ChildId)?.Date;

                if (anchor.HasValue)
                {
                    var minGapDate = ScheduleController.calculateDate(anchor.Value, dose.MinGap.Value).Date;
                    var exact = dose.Vaccine != null && dose.Vaccine.ExactIntervalRequired;
                    var enforced = exact ? minGapDate : minGapDate.AddDays(-ScheduleController.CdcGraceDays);
                    if (day < enforced)
                        return Early(name, minGapDate, "minimum gap from previous dose not met");
                }
            }

            return ok;
        }

        private static Result Early(string name, DateTime earliest, string why) => new Result
        {
            Ok = false,
            EarliestDate = earliest,
            Message = name + " cannot be given before " + earliest.ToString("dd-MM-yyyy") + " (" + why + ")."
        };
    }
}
