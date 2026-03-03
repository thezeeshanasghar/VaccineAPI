using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.Models
{

    public class DoctorSchedule
    {
        public long Id { get; set; }
        public long DoseId { get; set; }
        public virtual Dose Dose { get; set; } = null!;
        // public long InvoiceId { get; set; }
        public long DoctorId { get; set; }
        public virtual Doctor Doctor { get; set; } = null!;
        public int GapInDays { get; set; }  // min age is treating as gap in days
        public bool? IsActive { get; set; }

    }

}