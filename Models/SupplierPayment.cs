using System;

namespace VaccineAPI.Models
{
    public class SupplierPayment
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public virtual Supplier Supplier { get; set; } = null!;
        public long ClinicId { get; set; }
        public virtual Clinic Clinic { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash"; // Cash | Online Transfer | Partial
        public string? Notes { get; set; }
        public int? BillId { get; set; }
        public virtual Bill? Bill { get; set; }
    }
}
