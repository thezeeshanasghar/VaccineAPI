using System;

namespace VaccineAPI.ModelDTO
{
    public class ExpenseCreateDTO
    {
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public bool IsShared { get; set; } = false;
        public DateTime ExpenseDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string ExpenseType { get; set; } = "Recurring";
        public string PaymentMode { get; set; } = "Cash";
        public string? Notes { get; set; }

        // Capital fields
        public string? AssetName { get; set; }
        public decimal? ExpectedLifeYrs { get; set; }
        public DateTime? WarrantyExpiry { get; set; }
        public string? ReceiptImage { get; set; }   // base64 data URL
        public string? WarrantyImage { get; set; }  // base64 data URL
    }

    public class ExpenseResponseDTO
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public bool IsShared { get; set; }
        public DateTime ExpenseDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string ExpenseType { get; set; } = "";
        public string PaymentMode { get; set; } = "";
        public string? Notes { get; set; }
        public string? AssetName { get; set; }
        public decimal? ExpectedLifeYrs { get; set; }
        public DateTime? WarrantyExpiry { get; set; }
        public string? ReceiptImagePath { get; set; }
        public string? WarrantyImagePath { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
