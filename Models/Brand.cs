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