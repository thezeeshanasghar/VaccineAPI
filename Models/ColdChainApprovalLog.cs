using System;

namespace VaccineAPI.Models
{
    public class ColdChainApprovalLog
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public DateTime WeekStartDate { get; set; }
        public DateTime WeekEndDate { get; set; }
        public int TotalReadings { get; set; }
        public int InRangeCount { get; set; }
        public int OutOfRangeCount { get; set; }
        public int RequiredChecks { get; set; } // fridges x 14 (2/day x 7 days)
        public int MissedChecks { get; set; }
        public string Status { get; set; } = "pending"; // pending / approved / flagged / rejected
        public string? DoctorComments { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
