namespace VaccineAPI.ModelDTO
{
    // Body of POST api/Referral — what a VacAgent referral form submits. DOB is a string
    // ("dd-MM-yyyy", the format the apps already send, or "yyyy-MM-dd") and parsed by hand: the
    // DateTime binder is known to reject raw ISO dates from VacAgent (see ChildController.agent-register).
    public class AgentReferralCreateDTO
    {
        public int AgentId { get; set; }
        public string Name { get; set; } = "";
        public string FatherName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string DOB { get; set; } = "";
        public string CountryCode { get; set; } = "92";
        public string MobileNumber { get; set; } = "";
        public string? City { get; set; }
        public string? Email { get; set; }
        public string? Note { get; set; }
    }

    public class ReferralConvertDTO
    {
        public long ChildId { get; set; }
    }

    public class ReferralRejectDTO
    {
        public string Reason { get; set; } = "";
    }

    public class ReferralClinicDTO
    {
        public long ClinicId { get; set; }
    }
}
