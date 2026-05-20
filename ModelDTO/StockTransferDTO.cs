using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.ModelDTO
{
    public class StockTransferItemDTO
    {
        [Required]
        public long BrandId { get; set; }

        public string? BrandName { get; set; }

        public string? BatchNumber { get; set; }

        public DateTime? ExpiryDate { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [Required]
        public decimal CostPrice { get; set; }

        public decimal TotalValue => Quantity * CostPrice;
    }

    public class StockTransferRequestDTO
    {
        [Required]
        public long FromClinicId { get; set; }

        [Required]
        public long ToClinicId { get; set; }

        [Required]
        public long DoctorId { get; set; }

        [Required]
        public List<StockTransferItemDTO> Items { get; set; } = new();
    }

    public class StockTransferHistoryDTO
    {
        public long Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public long FromClinicId { get; set; }
        public string FromClinicName { get; set; } = "";
        public long ToClinicId { get; set; }
        public string ToClinicName { get; set; } = "";
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public decimal CostPrice { get; set; }
        public decimal TotalValue { get; set; }
        public long CreatedBy { get; set; }
        public string TransferredByName { get; set; } = "";
    }

    public class StockTransferEditDTO
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [Required]
        public decimal CostPrice { get; set; }

        public string? BatchNumber { get; set; }

        public DateTime? ExpiryDate { get; set; }
    }

    public class AvailableBatchDTO
    {
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public string? BatchLot { get; set; }
        public DateTime? Expiry { get; set; }
        public int AvailableQuantity { get; set; }
        public decimal CostPrice { get; set; }
    }
}
