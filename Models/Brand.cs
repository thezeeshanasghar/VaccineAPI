using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Brand
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Brand()
        {
            this.BrandAmounts = new HashSet<BrandAmount>();
            // this.BrandInventories = new HashSet<BrandInventory>();
            this.Schedules = new HashSet<Schedule>();
        }
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        // Route of administration (IM/SC/ID/Oral/Intranasal). Fixed pharmacological property set by
        // admin, entered via a constrained pick-list. Auto-loaded read-only at give-time and
        // snapshotted onto Schedule.Route. No per-patient override anywhere.
        public string Route { get; set; } = "";
        // Admin-set SUGGESTED default site (a hint, not a lock). Valid for Route. Nurse overrides
        // per dose at give-time. Distinct from Route: Site is a per-patient operational choice.
        public string? SiteDefault { get; set; }
        public int? MinAge { get; set; }

        // v2: true when another brand of the same doctor collides case-insensitively
        // (e.g. HEXAXIM vs Hexaxim). Set on brand save so the give/sale UI can warn without
        // any string logic. The brands remain distinct (case-sensitive utf8mb4_bin storage).
        public bool HasCaseTwin { get; set; }
        public virtual ICollection<BrandAmount> BrandAmounts { get; set; } = new HashSet<BrandAmount>();
        // public virtual ICollection<BrandInventory> BrandInventories { get; set; }
        public virtual ICollection<Schedule> Schedules { get; set; } = new HashSet<Schedule>();
    }

}