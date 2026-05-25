using System;

namespace VaccineAPI.Models
{
    public class Expense
    {
        public long Id { get; set; }
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

        // Fixed asset fields (Capital only)
        public string? AssetName { get; set; }
        public decimal? ExpectedLifeYrs { get; set; }
        public DateTime? WarrantyExpiry { get; set; }
        public string? ReceiptImagePath { get; set; }
        public string? WarrantyImagePath { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
