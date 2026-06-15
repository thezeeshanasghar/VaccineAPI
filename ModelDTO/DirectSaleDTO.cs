using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class DirectSaleItemDTO
    {
        public long BrandId { get; set; }
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public decimal SalePricePerUnit { get; set; }
    }

    public class DirectSaleCreateDTO
    {
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string ClientName { get; set; } = "";
        public string PaymentMode { get; set; } = "Cash";
        public string OnlineService { get; set; } = "";
        public string Notes { get; set; } = "";
        public DateTime SaleDate { get; set; }
        public List<DirectSaleItemDTO> Items { get; set; } = new List<DirectSaleItemDTO>();
        public long? PaymentCollectorPaId { get; set; }
    }

    public class DirectSalePaymentModeDTO
    {
        public string PaymentMode { get; set; } = "";
        public string? OnlineService { get; set; }
    }

    public class DirectSaleListDTO
    {
        public long Id { get; set; }
        public string SaleBillNo { get; set; } = "";
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public decimal SalePricePerUnit { get; set; }
        public decimal PurchasePricePerUnit { get; set; }
        public decimal TotalSaleValue { get; set; }
        public decimal TotalCostValue { get; set; }
        public decimal Profit { get; set; }
        public string ClientName { get; set; } = "";
        public string PaymentMode { get; set; } = "";
        public string OnlineService { get; set; } = "";
        public string Notes { get; set; } = "";
        public DateTime SaleDate { get; set; }
        public long? PaymentCollectorPaId { get; set; }
        public string? PaymentCollectorPaName { get; set; }
    }
}
