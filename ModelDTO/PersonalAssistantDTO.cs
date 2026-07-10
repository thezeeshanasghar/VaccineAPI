namespace VaccineAPI.ModelDTO
{
    public class PersonalAssistantDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public long DoctorId { get; set; }
        public string MobileNumber { get; set; } = "";
        public string ProfileImage { get; set; } = "";
        public string Password { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public UserDTO User { get; set; } = null!;
        public DoctorDTO Doctor { get; set; } = null!;
    }
}
