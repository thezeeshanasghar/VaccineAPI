using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace VaccineAPI.Models
{

    public class Vaccine
    {
        public Vaccine()
        {
            this.Doses = new HashSet<Dose>();
        }
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }

        public bool isInfinite { get; set; }
        public int Validity { get; set; }

        // Vaccine platform/classification: 1=Live, 2=Inactivated (Non-Live),
        // 3=Subunit/Conjugate/Polysaccharide (Non-Live), 4=Toxoid (Non-Live),
        // 5=mRNA (Non-Live), 6=Viral Vector (Non-Live).
        // Optional (0/null = not set). Metadata only today; not read by any
        // scheduling/rule logic yet.
        public int? Type { get; set; }

        // CDC 4-day grace period exclusion. When true, this vaccine's minimum dose
        // interval is enforced as an EXACT floor at give-time (no 4-day grace) —
        // for accelerated/precise schedules CDC excludes from the grace rule.
        // Default false. Set true for cholera and rabies (admin-toggleable, no code
        // change needed to exempt a future vaccine).
        public bool ExactIntervalRequired { get; set; }

        // Marks this vaccine as containing a DTaP/DPT (diphtheria-tetanus-pertussis)
        // component — whether standalone DTaP itself or any combo product with DTaP/DPT
        // bundled in (e.g. HBV+DPT+Hib, OPV+DPT+HBV, OPV/IPV+HBV+DPT+Hib). Admin/doctor-
        // toggled manually per vaccine; generic flag, not brand-specific and not a
        // hardcoded name list. Drives the "DTaP dose already covered by a combo vaccine"
        // grey-out check in DoseController.GetSDosesForChild and the give-time predecessor
        // check in ScheduleController (see GetDTaPCoverageCount) — doses are counted
        // positionally across ALL of the child's ContainsDTaP-flagged vaccines, not
        // matched by name. Default false.
        public bool ContainsDTaP { get; set; }

        public virtual ICollection<Dose> Doses { get; set; } = new HashSet<Dose>();
    }

}