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

        // PA assigned to collect/record payment for this sale (mirrors Schedule.PaymentCollectorPaId).
        // When set, PaymentMode starts as "Pending" until the PA records the actual mode;
        // if Cash, the amount flows into that PA's cash-in-hand pool once recorded.
        public long? PaymentCollectorPaId { get; set; }

        // True once payment mode is finalized: either set immediately at sale creation
        // (no PA assigned), or recorded later by the assigned PA via record-payment-mode.
        // Mirrors Schedule.IsPaymentCollected.
        public bool IsPaymentCollected { get; set; } = true;

        // PA's second step (after recording payment mode): marks the sale as handed off /
        // ready for the doctor to reconcile. Mirrors PAAssignment "mark done" -> PendingHandover.
        public bool IsMarkedDoneByPA { get; set; } = false;

        // Doctor's confirmation that this sale's payment has been received/reconciled.
        // Mirrors InvoiceSubmission.IsConfirmedByDoctor. Irrelevant (left false) when no
        // PA is assigned, since those sales never appear on the reconciliation page.
        public bool IsConfirmedByDoctor { get; set; } = false;

        public DateTime? ConfirmedAt { get; set; }

        // v2: 0 = Active, 1 = Reversed. Never hard-deleted; a reverse flips this.
        public byte Status { get; set; }

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;

        [ForeignKey("ClinicId")]
        public virtual Clinic Clinic { get; set; } = null!;
    }
}
