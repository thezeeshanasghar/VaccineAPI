using System;

namespace VaccineAPI.ModelDTO
{
    public class FixedAssetDTO
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string? ClinicName { get; set; }
        public long? ExpenseId { get; set; }
        public string AssetName { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTime PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public decimal ExpectedLifeYrs { get; set; } = 5;
        public decimal MonthlyDeprn { get; set; }
        public decimal AccumDeprn { get; set; }
        public decimal BookValue { get; set; }
        public DateTime? WarrantyExpiry { get; set; }
        public string? InvoiceImagePath { get; set; }
        public string? WarrantyImagePath { get; set; }
        public bool IsDisposed { get; set; }
        public DateTime? DisposalDate { get; set; }
        public decimal? DisposalValue { get; set; }
        public string? Notes { get; set; }
    }
}
