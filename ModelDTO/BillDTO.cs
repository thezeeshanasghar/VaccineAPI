using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class BillCreateDTO
    {
        public string? BillNo { get; set; }
        public DateTime BillDate { get; set; }
        public long? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public decimal AwtPercent { get; set; }
        public List<BillLineDTO> Lines { get; set; } = new List<BillLineDTO>();
    }

    public class BillLineDTO
    {
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
}
