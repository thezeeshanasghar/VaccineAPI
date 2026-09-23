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
        // True for every agent until they change their PIN for the first time. Set false
        // by every doctor-side create (default PIN "0000"); cleared by Agent/change-password.
        // VacAgent's login checks this and forces a redirect to change-password before /search.
        public bool MustChangePassword { get; set; } = true;
    }
}
