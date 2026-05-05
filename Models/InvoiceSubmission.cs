using System;

namespace VaccineAPI.Models
{
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
    }
}
