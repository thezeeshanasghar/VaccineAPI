using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // Append-only: who did what, with the actor taken from the validated token (never the request body).
    // Created by add_featureflags_and_financialaudit.sql. Written only while the identity.stamp flag is on.
    [Table("financialaudit")]
    public class FinancialAuditRow
    {
        public long Id { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
        [MaxLength(40)] public string Action { get; set; } = "";
        public long? DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long? ChildId { get; set; }
        public long? ScheduleId { get; set; }
        public long? InvoiceSubmissionId { get; set; }
        [MaxLength(40)] public string? InvoiceNumber { get; set; }
        public long? AssignmentId { get; set; }
        public decimal? Amount { get; set; }
        public long? ActorUserId { get; set; }
        [MaxLength(20)] public string? ActorRole { get; set; }
        [MaxLength(10)] public string ActorSource { get; set; } = "none";   // token | body | none
        public long? ClaimedPaId { get; set; }
        [MaxLength(120)] public string? Endpoint { get; set; }
        [MaxLength(30)] public string? ClientVersion { get; set; }
        public string? Detail { get; set; }
    }
}
