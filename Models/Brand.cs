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
            this.BrandInventories = new HashSet<BrandInventory>();
            this.Schedules = new HashSet<Schedule>();
        }
        public long Id { get; set; }
        public string Name { get; set; }
        public long VaccineId { get; set; }
        public virtual Vaccine Vaccine { get; set; }
        public virtual ICollection<BrandAmount> BrandAmounts { get; set; }
        public virtual ICollection<BrandInventory> BrandInventories { get; set; }
        public virtual ICollection<Schedule> Schedules { get; set; }
    }

}