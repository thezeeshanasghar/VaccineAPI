using System;

namespace VaccineAPI.Models
{
    public class TemperatureReading
    {
        public long Id { get; set; }
        public long RefrigeratorId { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public decimal Temperature { get; set; }
        public DateTime RecordedDate { get; set; } // date only, no time component
        public string RecordedTime { get; set; } = ""; // "HH:mm" 24h format
        public long? RecordedByPaId { get; set; } // null if the doctor logged it themselves
        public string RecordedByName { get; set; } = ""; // PA's or doctor's display name, captured at write time
        public string? Notes { get; set; }
        public bool IsInRange { get; set; } // server-calculated
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
