namespace VaccineAPI.ModelDTO
{
    public class BatchBreakdownDTO
    {
        public long BrandId { get; set; }
        public string BrandName { get; set; } = "";
        public string BatchLot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int Quantity { get; set; }
    }
}
