using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;


namespace VaccineAPI
{
    public class UserEmail
    {
        #region Parent Email

        public static string ParentEmail(Child child, string linkToken, string contentRootPath, Context db)
        {
            string honorific = child.Gender == "Girl" ? "Miss." : "Mr.";
            string openAppUrl = "https://client.vaccinationcentre.com/child?t=" + Uri.EscapeDataString(linkToken);
            string maskedPassword = "••••";

            string body = $@"
<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:600px;margin:0 auto;background:#ffffff;border:1px solid #DCE7E8;border-radius:14px;overflow:hidden;"">
  <div style=""padding:32px 32px 24px;text-align:center;border-bottom:1px solid #DCE7E8;"">
    <div style=""width:52px;height:52px;border-radius:50%;background:#DFF3EA;display:inline-flex;align-items:center;justify-content:center;margin:0 0 14px;"">
      <div style=""font-size:24px;color:#1E8E5A;"">&#10003;</div>
    </div>
    <h1 style=""font-size:20px;line-height:1.3;margin:0 0 6px;font-weight:700;color:#0E2A38;"">Registration confirmed</h1>
    <p style=""font-size:14.5px;color:#5B7480;margin:0;line-height:1.5;"">{honorific} {child.Name}, your account is active at {child.Clinic.Name}</p>
  </div>
  <div style=""padding:26px 32px 8px;"">
    <p style=""font-size:11.5px;font-weight:700;letter-spacing:0.07em;text-transform:uppercase;color:#5B7480;margin:0 0 12px;"">Sign in details</p>
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #DCE7E8;border-radius:10px;border-collapse:separate;overflow:hidden;"">
      <tr>
        <td style=""padding:12px 16px;font-size:13.5px;color:#5B7480;"">Mobile number</td>
        <td style=""padding:12px 16px;font-size:14.5px;font-weight:600;font-family:ui-monospace,Menlo,Consolas,monospace;text-align:right;"">{child.User.MobileNumber}</td>
      </tr>
      <tr>
        <td style=""padding:12px 16px;font-size:13.5px;color:#5B7480;border-top:1px solid #DCE7E8;"">Password</td>
        <td style=""padding:12px 16px;font-size:14.5px;font-weight:600;font-family:ui-monospace,Menlo,Consolas,monospace;text-align:right;border-top:1px solid #DCE7E8;"">{maskedPassword}</td>
      </tr>
    </table>
    <p style=""font-size:12.5px;color:#5B7480;margin:12px 0 0;"">Keep these safe. You'll use them to log in.</p>
  </div>
  <div style=""padding:22px 32px 6px;"">
    <p style=""font-size:11.5px;font-weight:700;letter-spacing:0.07em;text-transform:uppercase;color:#5B7480;margin:0 0 12px;"">What you can do now</p>
    <div style=""border:1px solid #DCE7E8;border-radius:10px;padding:14px 16px;margin-bottom:10px;"">
      <div style=""font-size:14.5px;font-weight:700;color:#0E2A38;margin:0 0 2px;"">Book appointments</div>
      <div style=""font-size:13px;color:#5B7480;"">Schedule vaccinations in seconds — no phone calls needed.</div>
    </div>
    <div style=""border:1px solid #DCE7E8;border-radius:10px;padding:14px 16px;margin-bottom:10px;"">
      <div style=""font-size:14.5px;font-weight:700;color:#0E2A38;margin:0 0 2px;"">View vaccine records</div>
      <div style=""font-size:13px;color:#5B7480;"">Access complete history for you and your family.</div>
    </div>
    <div style=""border:1px solid #DCE7E8;border-radius:10px;padding:14px 16px;"">
      <div style=""font-size:14.5px;font-weight:700;color:#0E2A38;margin:0 0 2px;"">Track upcoming doses</div>
      <div style=""font-size:13px;color:#5B7480;"">Get reminders so you never miss an appointment.</div>
    </div>
  </div>
  <div style=""margin:26px 32px 30px;"">
    <a href=""{openAppUrl}"" style=""display:block;width:100%;box-sizing:border-box;text-align:center;background:#3B6FE0;color:#ffffff;font-weight:700;font-size:15px;padding:14px 20px;border-radius:9px;text-decoration:none;"">Open app now</a>
  </div>
</div>";

            var sender = EmailSenderResolver.Resolve(child.Clinic?.Doctor, db);
            return SendEmail(child.Email, body, "Registration confirmed", isHtml: true, sender: sender);
        }

        private static string BuildLogoImgTag(string monogramImagePath, string contentRootPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(monogramImagePath))
                    return "";

                string fullPath = Path.Combine(contentRootPath, monogramImagePath);
                if (!File.Exists(fullPath))
                    return "";

                string ext = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
                string mime = ext switch
                {
                    "png" => "image/png",
                    "jpg" or "jpeg" => "image/jpeg",
                    "gif" => "image/gif",
                    "svg" => "image/svg+xml",
                    _ => "image/png"
                };

                string base64 = Convert.ToBase64String(File.ReadAllBytes(fullPath));
                return $@"<img src=""data:{mime};base64,{base64}"" alt=""Clinic logo"" style=""height:44px;width:44px;flex-shrink:0;"">";
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error embedding clinic logo in email: " + ex.Message);
                return "";
            }
        }



        public static void ParentAlertEmail(List<(string DoseName, DateTime Date)> dueDoses, Child child, string linkToken, string contentRootPath, Context db)
        {
            if (dueDoses.Count == 0) return;

            DateTime today = DateTime.UtcNow.AddHours(5).Date;
            string logoTag = BuildLogoImgTag(child.Clinic?.MonogramImage, contentRootPath);
            bool isVaccinePkBranded = child.Clinic?.Doctor?.Id == 1;
            string poweredByLine = isVaccinePkBranded
                ? @"<div style=""font-size:11.5px;color:#5B7480;"">Powered by Vaccine.pk</div>"
                : "";

            var doseCardsBuilder = new StringBuilder();
            foreach (var dose in dueDoses)
            {
                bool overdue = dose.Date.Date < today;
                string cardBg = overdue ? "#FBF1EF" : "#EEF9F6";
                string cardBorder = overdue ? "#E7C9C3" : "#CDEBE3";
                string pillColor = overdue ? "#B84A3E" : "#2E9FB5";
                string statusLabel = overdue ? "Overdue" : "Due soon";
                string dateLabel = overdue ? "Was due" : "Due";
                doseCardsBuilder.Append($@"
    <div style=""border:1px solid {cardBorder};background:{cardBg};border-radius:10px;padding:13px 16px;display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:10px;"">
      <div>
        <div style=""font-size:14.5px;font-weight:700;color:#0E2A38;"">{dose.DoseName.Trim()}</div>
        <span style=""display:inline-block;font-size:10px;font-weight:700;letter-spacing:0.05em;text-transform:uppercase;padding:2px 7px;border-radius:20px;margin-top:3px;background:{pillColor}2E;color:{pillColor};"">{statusLabel}</span>
      </div>
      <div style=""font-size:13px;font-weight:700;text-align:right;color:{pillColor};"">{dateLabel}<br>{dose.Date:dd MMM yyyy}</div>
    </div>");
            }

            // All doses in dueDoses share the same run date by construction (callers pass in
            // only today's due schedules — see GetRawAlertSchedules/GetAlert2/SendAlertEmail),
            // so this is always a same-day batch, never an open-ended overdue sweep. "Overdue"
            // above only reflects a dose whose date fell in the past relative to when the run
            // executes, which cannot happen for an exact-date-match batch but is kept as a
            // defensive label in case a caller ever passes a mixed-date list.
            bool anyOverdue = dueDoses.Any(d => d.Date.Date < today);
            string doseCount = dueDoses.Count == 1 ? "1 vaccine" : dueDoses.Count + " vaccines";
            string dueWord = anyOverdue ? "due" : "due today";
            string headline = dueDoses.Count == 1
                ? $"{child.Name}'s vaccine is {dueWord}"
                : $"{doseCount} are {dueWord} for {child.Name}";

            string body = $@"
<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:600px;margin:0 auto;background:#ffffff;border:1px solid #DCE7E8;border-radius:14px;overflow:hidden;"">
  <div style=""padding:26px 32px;border-bottom:1px solid #DCE7E8;display:flex;align-items:center;gap:14px;"">
    {logoTag}
    <div>
      <div style=""font-size:16px;font-weight:700;color:#0E2A38;"">{child.Clinic.Name}</div>
      {poweredByLine}
    </div>
  </div>
  <div style=""padding:32px 32px 8px;"">
    <p style=""font-size:12px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:#2E9FB5;margin:0 0 10px;"">Vaccination reminder</p>
    <h1 style=""font-size:21px;line-height:1.4;margin:0 0 6px;font-weight:700;color:#0E2A38;"">{headline}</h1>
    <p style=""font-size:14.5px;color:#5B7480;margin:0 0 24px;line-height:1.6;"">Please confirm your appointment with the clinic to keep the schedule on track.</p>
  </div>
  <div style=""margin:0 32px 24px;"">
    {doseCardsBuilder}
  </div>
  <hr style=""border:none;border-top:1px solid #DCE7E8;margin:0 32px;"">
  <div style=""padding:20px 32px 28px;font-size:12px;color:#5B7480;line-height:1.7;"">
    <p style=""font-weight:700;color:#0E2A38;font-size:13px;margin:0 0 4px;"">{child.Clinic.Name}</p>
    <p style=""margin:0;"">To reschedule or confirm this appointment, contact the clinic directly.</p>
    <p style=""margin-top:6px;"">&#128222; {child.Clinic.PhoneNumber}</p>
  </div>
</div>";

            string subject = dueDoses.Count == 1
                ? $"{child.Name} has a vaccine due today — {child.Clinic.Name}"
                : $"{child.Name} has {doseCount} due today — {child.Clinic.Name}";

            var sender = EmailSenderResolver.Resolve(child.Clinic?.Doctor, db);
            SendEmail(child.Email, body, subject, isHtml: true, sender: sender);
        }

        // milestoneNote: optional short line noting a real, currently-pending dose that falls
        // on/near this birthday — never fabricated. Callers decide what counts as "near" by
        // querying Schedule the same way the due-alert does (IsDone/IsSkip false, real Date
        // match); this method only renders whatever line it's given, or omits the row entirely
        // when milestoneNote is null so a birthday with no real milestone never shows one.
        public static void BirthdayEmail(Child child, int age, string ordinalSuffix, string milestoneNote, string contentRootPath, Context db)
        {
            string logoTag = BuildLogoImgTag(child.Clinic?.MonogramImage, contentRootPath);
            bool isVaccinePkBranded = child.Clinic?.Doctor?.Id == 1;
            string poweredByLine = isVaccinePkBranded
                ? @"<div style=""font-size:11.5px;color:#5B7480;"">Powered by Vaccine.pk</div>"
                : "";

            string milestoneBlock = string.IsNullOrWhiteSpace(milestoneNote) ? "" : $@"
    <div style=""border:1px solid #CDEBE3;background:#EEF9F6;border-radius:10px;padding:13px 16px;margin-top:18px;"">
      <div style=""font-size:13px;color:#0E2A38;line-height:1.6;"">{milestoneNote}</div>
    </div>";

            string body = $@"
<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:600px;margin:0 auto;background:#ffffff;border:1px solid #DCE7E8;border-radius:14px;overflow:hidden;"">
  <div style=""padding:26px 32px;border-bottom:1px solid #DCE7E8;display:flex;align-items:center;gap:14px;"">
    {logoTag}
    <div>
      <div style=""font-size:16px;font-weight:700;color:#0E2A38;"">{child.Clinic.Name}</div>
      {poweredByLine}
    </div>
  </div>
  <div style=""padding:32px 32px 8px;"">
    <p style=""font-size:12px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:#2E9FB5;margin:0 0 10px;"">&#127881; Happy birthday</p>
    <h1 style=""font-size:21px;line-height:1.4;margin:0 0 6px;font-weight:700;color:#0E2A38;"">Happy {age}{ordinalSuffix} birthday, {child.Name}!</h1>
    <p style=""font-size:14.5px;color:#5B7480;margin:0 0 4px;line-height:1.6;"">Wishing {child.Name} a day filled with joy, laughter, and wonderful memories — from all of us at {child.Clinic.Name}.</p>
    {milestoneBlock}
  </div>
  <hr style=""border:none;border-top:1px solid #DCE7E8;margin:24px 32px 0;"">
  <div style=""padding:20px 32px 28px;font-size:12px;color:#5B7480;line-height:1.7;"">
    <p style=""margin:0;"">This is an automated birthday wish. For any medical queries, please contact the clinic directly.</p>
    <p style=""margin-top:6px;"">&#128222; {child.Clinic.PhoneNumber}</p>
  </div>
</div>";

            string subject = $"Happy {age}{ordinalSuffix} Birthday, {child.Name}! \U0001F389";

            var sender = EmailSenderResolver.Resolve(child.Clinic?.Doctor, db);
            SendEmail(child.Email, body, subject, isHtml: true, sender: sender);
        }

        public static void DoctorForgotPassword(Doctor doctor, Context db)
        {
            string body = ""
                   + "Hi " + "<b>" + doctor.DisplayName + "</b>, <br />"
                   + "Your password is <b>" + doctor.User.Password + "</b>";

            var sender = EmailSenderResolver.Resolve(doctor, db);
            SendEmail(doctor.Email, body, sender: sender);
        }



        public static void ParentForgotPassword(Child child, Context db)
        {
            string body = ""
                   + "Hi " + "<b>" + child.Name + "</b>, <br />"
                   + "Your password is <b>" + child.User.Password + "</b>";

            var doctor = db.Clinics.Where(c => c.Id == child.ClinicId).Select(c => c.Doctor).FirstOrDefault();
            var sender = EmailSenderResolver.Resolve(doctor, db);
            SendEmail(child.Email, body, sender: sender);
        }


        public static void PaForgotPassword(PersonalAssistant pa, Context db)
        {
            string body = ""
                   + "Hi " + "<b>" + pa.Name + "</b>, <br />"
                   + "Your password is <b>" + pa.User.Password + "</b>";

            var doctor = db.Doctors.FirstOrDefault(d => d.Id == pa.DoctorId);
            var sender = EmailSenderResolver.Resolve(doctor, db);
            SendEmail(pa.Email, body, sender: sender);
        }

        public static void ManagerForgotPassword(Manager manager, Context db)
        {
            string body = ""
                   + "Hi " + "<b>" + manager.Name + "</b>, <br />"
                   + "Your password is <b>" + manager.User.Password + "</b>";

            var doctor = db.Doctors.FirstOrDefault(d => d.Id == manager.DoctorId);
            var sender = EmailSenderResolver.Resolve(doctor, db);
            SendEmail(manager.Email, body, sender: sender);
        }

        public static void PersonalAssistantLoginDetails(PersonalAssistant pa, string password, Context db)
        {
            string body = ""
                   + "Hello " + pa.Name + "\n\n"
                   + "You have been registered as a Personal Assistant in the Vaccination Centre system.\n\n"
                   + "Your login details are:\n"
                   + "Mobile Number: " + pa.User.MobileNumber + "\n"
                   + "Password: " + password + "\n\n"
                   + "Please login at: https://doctor.vaccinationcentre.com/loginpa\n\n"
                   + "Regards,\n"
                   + "Vaccination Centre Team";

            var doctor = db.Doctors.FirstOrDefault(d => d.Id == pa.DoctorId);
            var sender = EmailSenderResolver.Resolve(doctor, db);
            SendEmail(pa.Email, body, "Your Personal Assistant Account Details", sender: sender);
        }

        public static void AgentLoginDetails(Agent agent, string password, Context db)
        {
            string body = ""
                + "Hello " + agent.Name + ",\n\n"
                + "You have been registered as a Referring Agent at Vaccination Centre.\n\n"
                + "Your login details for the VacAgent app are:\n"
                + "Login ID (Phone): " + agent.PhoneNumber + "\n"
                + "Password: " + password + "\n"
                + "Agent Code: " + agent.AgentCode + "\n\n"
                + "Regards,\n"
                + "Vaccination Centre Team";

            var sender = EmailSenderResolver.Resolve(null, db);
            SendEmail(agent.Email, body, "Your VacAgent Login Details", sender: sender);
        }

        #endregion

        // Sender credentials resolved by the caller (via EmailSenderResolver) — either a
        // doctor's own SMTP settings (when AllowOwnEmail is on and configured) or the
        // app-wide default (info@vaccinationcentre.com). SendEmail itself does no DB work.
        public class SmtpSender
        {
            public string Host { get; set; } = "";
            public int Port { get; set; }
            public bool UseSsl { get; set; }
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string FromEmail { get; set; } = "";
            public string FromName { get; set; } = "";
        }

        // Returns a diagnostic string describing the outcome (null on success) — callers
        // that don't care can ignore the return value, same as before this was added.
        public static string SendEmail(string userEmail, string body, string subject = "vaccinationcentre.com", bool isHtml = false, SmtpSender sender = null)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return "SendEmail: userEmail was null/empty";
            }
            if (sender == null || string.IsNullOrWhiteSpace(sender.Host))
            {
                return "SendEmail: no SMTP sender configured";
            }

            try
            {
                using (var client = new System.Net.Mail.SmtpClient(sender.Host, sender.Port))
                {
                    client.Credentials = new System.Net.NetworkCredential(sender.Username, sender.Password);
                    client.EnableSsl = sender.UseSsl;
                    client.Timeout = 15000;

                    using (var message = new System.Net.Mail.MailMessage())
                    {
                        message.From = new System.Net.Mail.MailAddress(sender.FromEmail, string.IsNullOrWhiteSpace(sender.FromName) ? sender.FromEmail : sender.FromName);
                        message.To.Add(userEmail);
                        message.Subject = subject;
                        message.Body = body;
                        message.IsBodyHtml = isHtml;

                        client.Send(message);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending email: " + ex.Message);
                // Do not rethrow: email failures must not break API workflows.
                return "SendEmail: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}