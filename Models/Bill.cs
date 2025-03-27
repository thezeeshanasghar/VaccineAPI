using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class Bill
    {
        public int Id { get; set; }
        public string BillNo { get; set; }
        public string Supplier { get; set; }
        public DateTime Date { get; set; }
        public bool IsPaid { get; set; }

        [Required]
        public long DoctorId { get; set; }

        [ForeignKey("DoctorId")]
        public virtual Doctor Doctor { get; set; }

        public virtual ICollection<Stock> Stocks { get; set; }
    }
}