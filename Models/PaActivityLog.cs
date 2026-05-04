using System;

namespace VaccineAPI.Models
{
    public class PaActivityLog
    {
        public long Id { get; set; }
        public long PaId { get; set; }
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long? PatientId { get; set; }
        public string ActionCode { get; set; } = "";
        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool IsReversal { get; set; }
        public long? ReversalOfLogId { get; set; }
        public DateTime ActionDate { get; set; } = DateTime.UtcNow;
        public PersonalAssistant PersonalAssistant { get; set; } = null!;
    }
}
