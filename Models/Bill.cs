using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // [Table("bill")]
    public class Bill
    {
        // [Key]
        // [Column("Id")]
        public int Id { get; set; }

        // [Required]
        // [Column("BillNo")]
        // [StringLength(100)]
        public string BillNo { get; set; }

        // [Required]
        // [Column("Supplier")]
        // [StringLength(100)]
        public string Supplier { get; set; }

        // [Required]
        // [Column("Date")]
        public DateTime Date { get; set; }

        // [Required]
        // [Column("IsPaid")]
        public bool IsPaid { get; set; }
    }
}