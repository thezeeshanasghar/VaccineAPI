using System;

namespace VaccineAPI.ModelDTO
{
    public class SupplierPaymentDTO
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public long ClinicId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? Notes { get; set; }
        public long? BillId { get; set; }
        public string? BillNo { get; set; }
    }
}
