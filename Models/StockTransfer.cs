using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class StockTransfer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long FromClinicId { get; set; }

        [Required]
        public long ToClinicId { get; set; }

        [Required]
        public long BrandId { get; set; }

        public string? BatchNumber { get; set; }

        public DateTime? ExpiryDate { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalValue { get; set; }

        public long CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("FromClinicId")]
        public virtual Clinic FromClinic { get; set; } = null!;

        [ForeignKey("ToClinicId")]
        public virtual Clinic ToClinic { get; set; } = null!;

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;
    }
}
