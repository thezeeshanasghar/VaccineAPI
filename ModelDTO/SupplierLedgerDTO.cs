using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class LedgerEntryDTO
    {
        public DateTime Date { get; set; }
        public string Type { get; set; } = "";        // OpeningBalance | Bill | AWT | Payment
        public string Description { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
        public long? ReferenceId { get; set; }        // BillId or PaymentId
    }

    public class SupplierLedgerDTO
    {
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public long ClinicId { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal TotalBills { get; set; }
        public decimal TotalAWT { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal OutstandingBalance { get; set; }
        public List<LedgerEntryDTO> Entries { get; set; } = new List<LedgerEntryDTO>();
    }
}
