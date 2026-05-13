using System;

namespace VaccineAPI.Models
{
    public class Notification
    {
        public long Id { get; set; }
        public long BookingId { get; set; }
        public long RecipientId { get; set; }        // DoctorId or UserId
        public string RecipientType { get; set; } = ""; // "Doctor" | "Parent"
        public string Message { get; set; } = "";
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(5);
        public virtual Booking Booking { get; set; } = null!;
    }
}
