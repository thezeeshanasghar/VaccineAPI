namespace VaccineAPI.ModelDTO
{
    public class BrandAmountDTO
    {
        public long Id { get; set; }
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public decimal Amount { get; set; }
        public long? PaId { get; set; }
    }
}
