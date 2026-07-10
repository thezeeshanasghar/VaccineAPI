using System;

namespace VaccineAPI.ModelDTO
{

    public class NotificationDTO
    {
        public long Id { get; set; }
        public string Type { get; set; } = "";
        public string RecipientType { get; set; } = "";
        public long RecipientId { get; set; }
        public long? BookingId { get; set; }
        public long? ChildId { get; set; }
        public long? ClinicId { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? PaName { get; set; }
        public string? PaPhone { get; set; }
        public string? PaPhoneWhatsApp { get; set; }
        public string? PaProfileImage { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

}
