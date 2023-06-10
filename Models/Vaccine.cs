using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace VaccineAPI.Models
{

    public class Vaccine
    {
        public Vaccine()
        {
            this.Brands = new HashSet<Brand>();
            this.Doses = new HashSet<Dose>();
        }
        public long Id { get; set; }
        public string Name { get; set; }
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }

        public bool isInfinite { get; set; }

        public virtual ICollection<Brand> Brands { get; set; }
        public virtual ICollection<Dose> Doses { get; set; }
    }

}