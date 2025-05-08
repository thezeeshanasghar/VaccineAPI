namespace VaccineAPI.ModelDTO
{
    public class PaAccessDTO
    {
         public long Id { get; set; }
        public long PaId { get; set; }
        public long ClinicId { get; set; }
        public PersonalAssistant PersonalAssistant { get; set; }
        public Clinic Clinic { get; set; }
    }
}
