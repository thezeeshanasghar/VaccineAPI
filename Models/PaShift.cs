using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // One on-duty session of a PA. Location sharing only runs while EndedAt is null.
    [Table("pashifts")]
    public class PaShift
    {
        public long Id { get; set; }
        public long PaId { get; set; }
        public long DoctorId { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
    }
}
