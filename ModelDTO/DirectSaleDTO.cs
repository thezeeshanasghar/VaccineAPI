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
        // Optional. Send the same value when retrying: a repeated request is answered from the first
        // result and never posts twice.
        public string? ClientRequestId { get; set; }

        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string ClientName { get; set; } = "";
        public string PaymentMode { get; set; } = "Cash";
        public string OnlineService { get; set; } = "";
        public string Notes { get; set; } = "";
        public DateTime SaleDate { get; set; }
        public List<DirectSaleItemDTO> Items { get; set; } = new List<DirectSaleItemDTO>();
        public long? PaymentCollectorPaId { get; set; }

        // Caller identity for StockActionGuard — the actor performing the sale (may differ
        // from PaymentCollectorPaId above, which is who ends up owing the cash).
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
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
