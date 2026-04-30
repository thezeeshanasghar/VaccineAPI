using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class DirectSale
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long BrandId { get; set; }

        [Required]
        public long ClinicId { get; set; }

        [Required]
        public long DoctorId { get; set; }

        public string? BatchLot { get; set; }

        public DateTime? ExpiryDate { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal SalePricePerUnit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PurchasePricePerUnit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalSaleValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCostValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Profit { get; set; }

        public string? ClientName { get; set; }

        public string PaymentMode { get; set; } = "Cash";

        public string? Notes { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.UtcNow;

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;

        [ForeignKey("ClinicId")]
        public virtual Clinic Clinic { get; set; } = null!;
    }
}
