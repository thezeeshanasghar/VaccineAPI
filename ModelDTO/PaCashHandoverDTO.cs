using System;

namespace VaccineAPI.ModelDTO
{
    public class PaCashHandoverDTO
    {
        public long Id { get; set; }
        public long PaId { get; set; }
        public string PaName { get; set; } = "";
        public long ClinicId { get; set; }
        public string ClinicName { get; set; } = "";
        public long DoctorId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public string? RejectionNote { get; set; }
        public decimal CashInHand { get; set; }
    }
}
