using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("papayableadjustments")]
    public class PaPayableAdjustment
    {
        public long Id { get; set; }
        public long PaId { get; set; }
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "";
        public DateTime AdjustedAt { get; set; } = DateTime.UtcNow;
    }
}
