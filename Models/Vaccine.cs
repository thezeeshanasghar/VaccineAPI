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

        public virtual ICollection<Dose> Doses { get; set; } = new HashSet<Dose>();
    }

}