using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class BillCreateDTO
    {
        // Optional. Send the same value when retrying: a repeated request is answered from the first
        // result and never posts twice.
        public string? ClientRequestId { get; set; }

        public string? BillNo { get; set; }
        public DateTime BillDate { get; set; }
        public long? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public decimal AwtPercent { get; set; }
        public decimal AmountPaid { get; set; }
        public string? PaymentMethod { get; set; }
        public List<BillLineDTO> Lines { get; set; } = new List<BillLineDTO>();

        // Opt-in: pending doses (UnbatchedUse ids) the doctor chose to allocate to the batches this
        // bill creates ("include this batch details and deduct the units already used"). Absent =
        // the doses stay pending and the bill saves exactly as before.
        public List<long>? ClaimUnbatchedUseIds { get; set; }

        // Caller identity for StockActionGuard — see AdjustStockCreateDTO for why all four
        // are needed together.
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
    }

    public class BillLineDTO
    {
        // Optional. When the client sends the batch id of an existing line, an edit updates THAT
        // batch in place; without it the line is matched by brand + lot + expiry.
        public int? StockId { get; set; }
        public long BrandId { get; set; }
        public string BatchLot { get; set; } = "";
        public DateTime Expiry { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class BillListDTO
    {
        public int Id { get; set; }
        public string BillNo { get; set; } = "";
        public DateTime BillDate { get; set; }
        public string SupplierName { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public decimal AwtPercent { get; set; }
        public decimal AwtAmount { get; set; }
        public decimal TotalPayable { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal PendingAmount { get; set; }
        public string PaymentStatus { get; set; } = "Unpaid";
        public bool IsPaid { get; set; }
        public int LineCount { get; set; }
    }

    public class BillDetailDTO
    {
        public int Id { get; set; }
        public string BillNo { get; set; } = "";
        public DateTime BillDate { get; set; }
        public long? SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public decimal AwtPercent { get; set; }
        public decimal AwtAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalPayable { get; set; }
        public bool IsPaid { get; set; }
        public List<BillLineDetailDTO> Lines { get; set; } = new List<BillLineDetailDTO>();
    }

    public class BillLineDetailDTO
    {
        public int StockId { get; set; }
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public string BatchLot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class BillUpdateDTO
    {
        public string? BillNo { get; set; }
        public DateTime BillDate { get; set; }
        public long? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public decimal AwtPercent { get; set; }
        public List<BillLineDTO> Lines { get; set; } = new List<BillLineDTO>();

        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
    }

    public class ConsumedCheckDTO
    {
        public int StockId { get; set; }
        public int Quantity { get; set; }
        public int OriginalQuantity { get; set; }
        public int Consumed { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal ConsumedAmount { get; set; }
        public string BrandName { get; set; } = "";
        public string BatchLot { get; set; } = "";
    }

    public class SplitConsumedResultDTO
    {
        public int NewBillId { get; set; }
        public string NewBillNo { get; set; } = "";
        public decimal ConsumedAmount { get; set; }
    }

    public class SupplierPaymentCreateDTO
    {
        public int BillId { get; set; }
        public long? SupplierId { get; set; }
        public long ClinicId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? Notes { get; set; }
        public DateTime PaymentDate { get; set; }

        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
    }

    public class SupplierPaymentDTO
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "";
        public DateTime PaymentDate { get; set; }
        public string? Notes { get; set; }
    }
}
