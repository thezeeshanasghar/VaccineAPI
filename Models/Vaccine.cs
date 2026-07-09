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

        // CDC 4-day grace period exclusion. When true, this vaccine's minimum dose
        // interval is enforced as an EXACT floor at give-time (no 4-day grace) —
        // for accelerated/precise schedules CDC excludes from the grace rule.
        // Default false. Set true for cholera and rabies (admin-toggleable, no code
        // change needed to exempt a future vaccine).
        public bool ExactIntervalRequired { get; set; }

        public virtual ICollection<Dose> Doses { get; set; } = new HashSet<Dose>();
    }

}