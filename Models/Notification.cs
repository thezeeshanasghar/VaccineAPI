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
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

}
