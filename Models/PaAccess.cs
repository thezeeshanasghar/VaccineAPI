namespace VaccineAPI.Models
{
    public class PaAccess
    {
        public long Id { get; set; }
        public long PersonalAssistantId { get; set; }
        public long ClinicId { get; set; }
        public bool IsOnline { get; set; }
        public PersonalAssistant PersonalAssistant { get; set; } = null!;
        public Clinic Clinic { get; set; } = null!;
    }
}