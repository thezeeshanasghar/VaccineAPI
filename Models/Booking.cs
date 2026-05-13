using System;

namespace VaccineAPI.Models
{
    public class Booking
    {
        public long Id { get; set; }
        public long ChildId { get; set; }
        public long DoctorId { get; set; }
        public long UserId { get; set; }
        public string Type { get; set; } = "";        // "HomeBooked" | "ClinicBooked"
        public string Vaccines { get; set; } = "";
        public string Address { get; set; } = "";
        public string Location { get; set; } = "";    // Google Maps URL
        public string City { get; set; } = "";
        public string ChildName { get; set; } = "";
        public string FatherName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Status { get; set; } = "Pending"; // "Pending" | "Confirmed" | "Cancelled"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(5);
        public virtual Child Child { get; set; } = null!;
    }
}
