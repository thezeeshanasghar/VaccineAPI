using System;
using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.ModelDTO
{
    public class BillDTO
    {
        public int Id { get; set; }
        public string BillNo { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateTime BillDate { get; set; }
        public bool IsPaid { get; set; }
        public DateTime PaidDate { get; set; }
        public bool IsPAApprove { get; set; }
        [Required]
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public long? SupplierId { get; set; }
        public decimal? AwtAmount { get; set; }
        public string DoctorName { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int TotalItems { get; set; }
    }
}
