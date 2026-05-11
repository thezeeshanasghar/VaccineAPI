using System;

namespace VaccineAPI.Models
{
    public class Expense
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public long? CategoryId { get; set; }
        public DateTime ExpenseDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = "";
        public string PaymentMode { get; set; } = "Cash";
        public string ExpenseType { get; set; } = "Recurring";
        public string? ReceiptPath { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Doctor Doctor { get; set; } = null!;
        public Clinic Clinic { get; set; } = null!;
        public ExpenseCategory? Category { get; set; }
    }
}
