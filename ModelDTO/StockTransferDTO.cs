using System;

namespace VaccineAPI.ModelDTO
{
    public class StockTransferCreateDTO
    {
        public long DoctorId { get; set; }
        public long FromClinicId { get; set; }
        public long ToClinicId { get; set; }
        public long BrandId { get; set; }
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public string Reason { get; set; } = "";
        public DateTime TransferDate { get; set; }
    }

    public class StockTransferListDTO
    {
        public long Id { get; set; }
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public string FromClinicName { get; set; } = "";
        public string ToClinicName { get; set; } = "";
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public string Reason { get; set; } = "";
        public DateTime TransferDate { get; set; }
    }
}
