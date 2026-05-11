using System;

namespace VaccineAPI.ModelDTO
{
    public class ExpenseDTO
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string? ClinicName { get; set; }
        public long? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public DateTime ExpenseDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = "";
        public string PaymentMode { get; set; } = "Cash";
        public string ExpenseType { get; set; } = "Recurring";
        public string? ReceiptPath { get; set; }
        public string? Notes { get; set; }
    }
}
