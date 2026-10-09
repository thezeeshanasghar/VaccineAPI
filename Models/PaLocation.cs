using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // One GPS fix posted by the assistant app during a shift. Kept 30 days (purged on shift start).
    [Table("palocations")]
    public class PaLocation
    {
        public long Id { get; set; }
        public long ShiftId { get; set; }
        public long PaId { get; set; }
        public long DoctorId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Accuracy { get; set; }
        public int? Battery { get; set; }
        public DateTime RecordedAt { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    }
}
