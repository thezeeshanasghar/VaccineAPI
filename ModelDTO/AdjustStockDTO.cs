namespace VaccineAPI.ModelDTO
{
    public class AdjustStockDTO
    {
        public long Id { get; set; }
        public long BrandId { get; set; }
        public int Adjustment { get; set; }
        public string Reason { get; set; } = "";
        public decimal Price { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public DateTime Date { get; set; }
        public string? BatchLot { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public string ClinicName { get; set; } = "";
        public string DoctorName { get; set; } = "";
    }
}
