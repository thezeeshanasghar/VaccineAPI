using System.Collections.Generic;

namespace VaccineAPI.Models
{
    public class Supplier
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? NTN { get; set; }
        public string? STRN { get; set; }
        public decimal? OpeningBalance { get; set; }
        public bool IsActive { get; set; } = true;
        public virtual ICollection<Bill> Bills { get; set; } = new HashSet<Bill>();
    }
}
