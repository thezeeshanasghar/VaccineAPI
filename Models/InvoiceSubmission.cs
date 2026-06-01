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
    }
}
