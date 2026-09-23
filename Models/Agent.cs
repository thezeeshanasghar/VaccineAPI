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
        // Default clinic for patients this agent refers. Null = no default (registering
        // PA/Manager picks manually as before). Only applied as an auto-fill suggestion on
        // the registration form for PA/Manager — a doctor's own registrations always use his
        // active clinic regardless of the selected agent. No FK constraint (agents is MyISAM).
        public long? ClinicId { get; set; }
    }
}
