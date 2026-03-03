using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class Bill
    {
        public int Id { get; set; }
        public string BillNo { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateTime BillDate { get; set; }
        public bool IsPaid { get; set; }
        public DateTime PaidDate { get; set; }
        public long DoctorId { get; set; }
        public bool IsPAApprove{get; set;}
        public virtual Doctor Doctor { get; set; } = null!;
        public long ClinicId { get; set; }
        public virtual Clinic Clinic { get; set; } = null!;
        public virtual ICollection<Stock> Stocks { get; set; } = new HashSet<Stock>();
    }
}