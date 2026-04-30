using System;

namespace VaccineAPI.ModelDTO
{
    public class DirectSaleDTO
    {
        public long Id { get; set; }
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public long ClinicId { get; set; }
        public string ClinicName { get; set; } = "";
        public long DoctorId { get; set; }
        public string? BatchLot { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public decimal SalePricePerUnit { get; set; }
        public decimal PurchasePricePerUnit { get; set; }
        public decimal TotalSaleValue { get; set; }
        public decimal TotalCostValue { get; set; }
        public decimal Profit { get; set; }
        public string? ClientName { get; set; }
        public string PaymentMode { get; set; } = "Cash";
        public string? Notes { get; set; }
        public DateTime SaleDate { get; set; }
    }
}
