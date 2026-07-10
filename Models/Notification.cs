using System;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Notification
    {
        public long Id { get; set; }

        public string Type { get; set; } = "";
        public string RecipientType { get; set; } = "";
        public long RecipientId { get; set; }

        public long? BookingId { get; set; }
        [JsonIgnore]
        public virtual Booking Booking { get; set; } = null!;

        public long? ChildId { get; set; }
        public long? ClinicId { get; set; }

        public string Title { get; set; } = "";
        public string Message { get; set; } = "";

        // Coordinator fields — populated on "PaAssigned" notifications so VacParent can render
        // the PA's photo and wire Call / WhatsApp buttons without a second lookup. Null for
        // other notification types. Phone is captured at assignment time; WhatsApp is the same
        // number normalised to international format (no leading 0, country code prefixed).
        public string? PaName { get; set; }
        public string? PaPhone { get; set; }
        public string? PaPhoneWhatsApp { get; set; }
        public string? PaProfileImage { get; set; }

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

}
