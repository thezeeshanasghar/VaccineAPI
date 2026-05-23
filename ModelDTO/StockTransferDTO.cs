using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class StockTransferItemDTO
    {
        public long BrandId { get; set; }
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class StockTransferCreateDTO
    {
        public long DoctorId { get; set; }
        public long FromClinicId { get; set; }
        public long ToClinicId { get; set; }
        public decimal AwtPercent { get; set; }
        public string Reason { get; set; } = "";
        public DateTime TransferDate { get; set; }
        public List<StockTransferItemDTO> Items { get; set; } = new List<StockTransferItemDTO>();
    }

    public class StockTransferListDTO
    {
        public long Id { get; set; }
        public int? BillId { get; set; }
        public string BillNo { get; set; } = "";
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public string FromClinicName { get; set; } = "";
        public string ToClinicName { get; set; } = "";
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public decimal AwtPercent { get; set; }
        public decimal AwtAmount { get; set; }
        public string Reason { get; set; } = "";
        public DateTime TransferDate { get; set; }
    }
}
