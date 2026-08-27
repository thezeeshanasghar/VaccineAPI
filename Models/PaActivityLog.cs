using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("paactivitylogs")]
    public class PaActivityLog
    {
        public long Id { get; set; }
        // Nullable: a Manager-initiated action logs ManagerId instead, never both — see
        // Schedule.GivenByManagerId comment for why PA/Manager ID spaces are kept separate.
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long? PatientId { get; set; }
        public string ActionCode { get; set; } = "";
        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool IsReversal { get; set; }
        public bool IsReversalApproved { get; set; } = false;
        public bool IsReversalRejected { get; set; } = false;
        public long? ReversalOfLogId { get; set; }
        public DateTime ActionDate { get; set; } = DateTime.UtcNow;
        [ForeignKey("PaId")]
        public PersonalAssistant PersonalAssistant { get; set; } = null!;
    }
}
