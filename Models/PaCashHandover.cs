using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("pacashhandovers")]
    public class PaCashHandover
    {
        public long Id { get; set; }
        public long PaId { get; set; }
        public virtual PersonalAssistant Pa { get; set; } = null!;
        public long ClinicId { get; set; }
        public long DoctorId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ConfirmedAt { get; set; }
        public string? RejectionNote { get; set; }
    }
}
