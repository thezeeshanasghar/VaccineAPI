using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("paassignments")]
    public class PAAssignment
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long PersonalAssistantId { get; set; }
        public long ChildId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public bool IsCompleted { get; set; } = false;
        public DateTime? CompletedAt { get; set; }
        public string? Notes { get; set; }
        public bool IsCancelled { get; set; } = false;
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }
        public long? ReassignedFromAssignmentId { get; set; }
        public bool IsAutoCreated { get; set; } = false;

        // "Active" | "InvoiceDownloaded" | "PaymentCollected" | "PendingHandover" | "Completed" | "PendingCancellation"
        public string AssignmentStatus { get; set; } = "Active";
        public DateTime? HandoverDoneAt { get; set; }
        public long? InvoiceSubmissionId { get; set; }

        // PA's cancellation request — set on RequestCancel, cleared/finalized on doctor approve/reject
        public DateTime? CancelRequestedAt { get; set; }
        public string? CancelRequestReason { get; set; }
        public string? RejectionNote { get; set; }
    }
}
