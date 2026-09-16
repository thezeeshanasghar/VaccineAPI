using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Central place to decide which SMTP credentials an email should go out through:
    // the doctor's own (when AllowOwnEmail is on and configured), else the app-wide default.
    public static class EmailSenderResolver
    {
        public static UserEmail.SmtpSender Resolve(Doctor doctor, Context db)
        {
            if (doctor != null && doctor.AllowOwnEmail && !string.IsNullOrWhiteSpace(doctor.SmtpHost))
            {
                return new UserEmail.SmtpSender
                {
                    Host = doctor.SmtpHost,
                    Port = doctor.SmtpPort ?? 587,
                    UseSsl = doctor.SmtpUseSsl,
                    Username = doctor.SmtpUsername ?? "",
                    Password = doctor.SmtpPassword ?? "",
                    FromEmail = doctor.SmtpFromEmail ?? doctor.Email,
                    FromName = doctor.SmtpFromName ?? doctor.DisplayName
                };
            }

            var defaultSetting = db.AppEmailSettings.FirstOrDefault();
            if (defaultSetting == null)
            {
                return null;
            }

            return new UserEmail.SmtpSender
            {
                Host = defaultSetting.Host,
                Port = defaultSetting.Port,
                UseSsl = defaultSetting.UseSsl,
                Username = defaultSetting.Username,
                Password = defaultSetting.Password,
                FromEmail = defaultSetting.FromEmail,
                FromName = defaultSetting.FromName
            };
        }
    }
}
