namespace VaccineAPI.ModelDTO
{
    public class AdjustStockDTO
    {
        public long Id { get; set; }
        public long BrandId { get; set; }
        public int Adjustment { get; set; }
        public string Reason { get; set; }
        public DateTime Date { get; set; }
        public string BrandName { get; set; }  // For display purposes
        public string VaccineName { get; set; } // For display purposes
    }
}