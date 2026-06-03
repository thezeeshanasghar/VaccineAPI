using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("invoicesubmissions")]
    public class InvoiceSubmission
    {
        public long Id { get; set; }
        public long ChildId { get; set; }
        public long DoctorId { get; set; }
        public long? PaId { get; set; }
        public long? ClinicId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public DateTime SubmittedAt { get; set; }
        public int EditCount { get; set; } = 0;
        public decimal ConsultationFee { get; set; }
        public decimal TotalAmount { get; set; } = 0;
        public bool IsConfirmedByDoctor { get; set; } = false;
        public DateTime? ConfirmedAt { get; set; }

        // "Active" | "UngiveReversal" | "Cancelled"
        public string InvoiceStatus { get; set; } = "Active";
        // true while an InvoiceAmendment row awaits doctor approve/reject
        public bool HasPendingAmendment { get; set; } = false;
        // payment mode copied from Schedule at download time
        public string? PaymentMode { get; set; }
        // true after PA clicks "Mark Done", before doctor confirms
        public bool PendingHandover { get; set; } = false;
        public DateTime? HandoverDoneAt { get; set; }
    }
}
