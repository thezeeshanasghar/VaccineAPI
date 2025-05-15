namespace VaccineAPI.Models
{
    public class PaAccess
    {
        public long Id { get; set; }
        public long PersonalAssistantId { get; set; }
        public long ClinicId { get; set; }
        public PersonalAssistant PersonalAssistant { get; set; }
        public Clinic Clinic { get; set; }
    }
}