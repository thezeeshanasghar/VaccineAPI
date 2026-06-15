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

        public string? OnlineService { get; set; }

        public bool IsPaymentApproved { get; set; } = false;

        public string? Notes { get; set; }

        public string? SaleBillNo { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.UtcNow;

        // PA who collected the cash for this sale (mirrors Schedule.PaymentCollectorPaId).
        // Only meaningful when PaymentMode == "Cash"; flows into that PA's cash-in-hand pool.
        public long? PaymentCollectorPaId { get; set; }

        // True once payment mode is finalized: either set immediately at sale creation
        // (no PA assigned), or recorded later by the assigned PA via record-payment-mode.
        // Mirrors Schedule.IsPaymentCollected.
        public bool IsPaymentCollected { get; set; } = true;

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;

        [ForeignKey("ClinicId")]
        public virtual Clinic Clinic { get; set; } = null!;
    }
}
