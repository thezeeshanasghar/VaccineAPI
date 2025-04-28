namespace VaccineAPI.ModelDTO
{
    public class PersonalAssistantDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public long DoctorId { get; set; }
        public string MobileNumber { get; set; }
        public string Password { get; set; }
        public string CountryCode { get; set; }
        public UserDTO User { get; set; }
        public DoctorDTO Doctor { get; set; }
    }
}