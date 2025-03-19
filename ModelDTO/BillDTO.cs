using System;
using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.ModelDTO
{
    public class BillDTO
    {
        public int Id { get; set; }
        public string BillNo { get; set; }
        public string Supplier { get; set; }
        public DateTime Date { get; set; }
        public bool IsPaid { get; set; }
        
        [Required]
        public long DoctorId { get; set; }
        public string DoctorName { get; set; }
    }
}