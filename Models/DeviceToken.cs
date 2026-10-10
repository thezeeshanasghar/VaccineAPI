using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // One installed app on one phone. RecipientType/RecipientId use the same keys as Notification
    // (PARENT = Users.Id, DOCTOR = Doctor.Id, PA = PersonalAssistant.Id, AGENT = Agents.Id).
    [Table("devicetokens")]
    public class DeviceToken
    {
        public long Id { get; set; }
        public string RecipientType { get; set; } = "";
        public long RecipientId { get; set; }
        public string Token { get; set; } = "";
        public string Platform { get; set; } = "android";
        public string AppFlavor { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }

    // Dedup for scheduler-generated pushes: one row per (key) so a reminder never goes out twice.
    [Table("pushlog")]
    public class PushLog
    {
        public long Id { get; set; }
        public string DedupKey { get; set; } = "";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }
}
