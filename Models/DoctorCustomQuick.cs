using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // A doctor's "Custom quick select" list: up to 4 doses shown as one-tap buttons on the
    // Add Child form when Customize is selected. Independent of the Travel quick select
    // (own table, own controller). Table name is all-lowercase (case-sensitive host).
    [Table("doctorcustomquick")]
    public class DoctorCustomQuick
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long DoseId { get; set; }
        public int SortOrder { get; set; }
    }
}
