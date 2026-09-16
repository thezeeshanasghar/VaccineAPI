namespace VaccineAPI.Models
{
    // Single-row table: the default/fallback SMTP sender (e.g. info@vaccinationcentre.com)
    // used for any doctor without their own AllowOwnEmail SMTP settings.
    public class AppEmailSetting
    {
        public long Id { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public bool UseSsl { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "";
    }
}
