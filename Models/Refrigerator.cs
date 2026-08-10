using System;

namespace VaccineAPI.Models
{
    public class Refrigerator
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string Name { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Type { get; set; } = "Refrigerator"; // Refrigerator / Freezer / CoolBox
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public string? Location { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
