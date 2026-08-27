using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("invoiceamendments")]
    public class InvoiceAmendment
    {
        public long Id { get; set; }
        public long InvoiceSubmissionId { get; set; }
        public string AmendmentType { get; set; } = "";   // "Edit" | "Ungive"
        public decimal OldAmount { get; set; }
        public decimal NewAmount { get; set; }
        // Nullable: a Manager-driven amendment has no PA acting on it — see
        // Schedule.GivenByManagerId comment for why PA/Manager ID spaces are kept separate.
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long DoctorId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsApprovedByDoctor { get; set; } = false;
        public DateTime? ApprovedAt { get; set; }
        public long? ApprovedByDoctorId { get; set; }
        public bool IsRejectedByDoctor { get; set; } = false;
        public DateTime? RejectedAt { get; set; }
        public string? Notes { get; set; }

        [ForeignKey("InvoiceSubmissionId")]
        public InvoiceSubmission? InvoiceSubmission { get; set; }
    }
}
