using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // A doctor's "Travel quick select" list: up to 4 doses shown as one-tap buttons on the
    // Add Child form when Travel is selected. Table name is all-lowercase (case-sensitive host).
    [Table("doctortravelquick")]
    public class DoctorTravelQuick
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long DoseId { get; set; }
        public int SortOrder { get; set; }
    }
}
