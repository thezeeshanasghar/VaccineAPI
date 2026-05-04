namespace VaccineAPI.Models
{
    public class Agent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string AgentCode { get; set; } = "";
        public decimal ReferralFeePerClient { get; set; }
    }
}
