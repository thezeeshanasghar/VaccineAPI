namespace VaccineAPI.Models
{
    public class Agent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public decimal ReferralFeePerClient { get; set; }
    }
}
