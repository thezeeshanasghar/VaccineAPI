namespace VaccineAPI.ModelDTO
{
    public class AgentLoginDTO
    {
        public string PhoneNumber { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class AgentChangePasswordDTO
    {
        public string PhoneNumber { get; set; } = "";
        public string OldPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }
}
