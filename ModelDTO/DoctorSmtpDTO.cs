namespace VaccineAPI.ModelDTO
{
    public class DoctorSmtpDTO
    {
        public bool AllowOwnEmail { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public bool SmtpUseSsl { get; set; }
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public string? SmtpFromEmail { get; set; }
        public string? SmtpFromName { get; set; }
    }
}
