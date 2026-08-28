using System;
using System.Net;
using System.Collections.Generic;
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

        public static string ParentEmail(Child child, string contentRootPath)
        {
            string honorific = child.Gender == "Girl" ? "Miss." : "Mr.";
            string doctorMobile = child.Clinic?.Doctor?.User?.MobileNumber ?? "";
            string doctorPhone = doctorMobile.StartsWith("92") || doctorMobile.StartsWith("+92")
                ? doctorMobile.TrimStart('+')
                : "92" + doctorMobile.TrimStart('0');
            string schedulePdfUrl = "http://myapi.vaccinationcentre.com/api/child/" + child.Id + "/Download-Schedule-PDF";
            string logoTag = BuildLogoImgTag(child.Clinic?.MonogramImage, contentRootPath);
            bool isVaccinePkBranded = child.Clinic?.Doctor?.Id == 1;
            string poweredByLine = isVaccinePkBranded
                ? @"<div style=""font-size:11.5px;color:#5B7480;"">Powered by Vaccine.pk</div>"
                : "";
            string webLinkLine = isVaccinePkBranded
                ? @"<p style=""text-align:center;margin:14px 32px 32px;font-size:13px;color:#5B7480;"">or <a href=""https://vaccinationcentre.com"" style=""color:#2E9FB5;font-weight:600;text-decoration:none;"">open Vaccine.pk in your browser</a></p>"
                : "";

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
    <p style=""font-size:12px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:#2E9FB5;margin:0 0 10px;"">Registration confirmed</p>
    <h1 style=""font-size:22px;line-height:1.35;margin:0 0 6px;font-weight:700;color:#0E2A38;"">{honorific} {child.Name} is registered at {child.Clinic.Name}</h1>
    <p style=""font-size:14.5px;color:#5B7480;margin:0 0 26px;line-height:1.6;"">Use the details below to sign in to the parent app and track upcoming doses.</p>
  </div>
  <div style=""border:1px solid #DCE7E8;border-radius:10px;margin:0 32px 20px;overflow:hidden;"">
    <div style=""font-size:11.5px;font-weight:700;letter-spacing:0.07em;text-transform:uppercase;color:#5B7480;padding:14px 18px 6px;"">Sign-in details</div>
    <div style=""display:flex;justify-content:space-between;padding:9px 18px;"">
      <span style=""font-size:13.5px;color:#5B7480;"">Mobile Number (ID)</span>
      <span style=""font-size:14.5px;font-weight:600;font-family:ui-monospace,Menlo,Consolas,monospace;"">{child.User.MobileNumber}</span>
    </div>
    <div style=""display:flex;justify-content:space-between;padding:9px 18px;border-top:1px solid #DCE7E8;"">
      <span style=""font-size:13.5px;color:#5B7480;"">Password</span>
      <span style=""font-size:14.5px;font-weight:600;font-family:ui-monospace,Menlo,Consolas,monospace;"">{child.User.Password}</span>
    </div>
  </div>
  <div style=""display:flex;gap:8px;margin:0 32px 24px;padding:11px 14px;background:#FBF1EF;border:1px solid #E7C9C3;border-radius:8px;font-size:12.5px;color:#0E2A38;line-height:1.5;"">
    Keep this password private. You can change it any time from the app's account settings.
  </div>
  <div style=""border:1px solid #DCE7E8;border-radius:10px;margin:0 32px 28px;overflow:hidden;"">
    <div style=""font-size:11.5px;font-weight:700;letter-spacing:0.07em;text-transform:uppercase;color:#5B7480;padding:14px 18px 6px;"">Clinic &amp; doctor contact</div>
    <div style=""display:flex;justify-content:space-between;padding:9px 18px;"">
      <span style=""font-size:13.5px;color:#5B7480;"">Clinic</span>
      <span style=""font-size:14.5px;font-weight:600;"">{child.Clinic.Name}</span>
    </div>
    <div style=""display:flex;justify-content:space-between;padding:9px 18px;border-top:1px solid #DCE7E8;"">
      <span style=""font-size:13.5px;color:#5B7480;"">Address</span>
      <span style=""font-size:14.5px;font-weight:600;"">{child.Clinic.Address}</span>
    </div>
    <div style=""display:flex;justify-content:space-between;padding:9px 18px;border-top:1px solid #DCE7E8;"">
      <span style=""font-size:13.5px;color:#5B7480;"">Clinic Phone</span>
      <span style=""font-size:14.5px;font-weight:600;font-family:ui-monospace,Menlo,Consolas,monospace;"">{child.Clinic.PhoneNumber}</span>
    </div>
    <div style=""display:flex;justify-content:space-between;padding:9px 18px;border-top:1px solid #DCE7E8;"">
      <span style=""font-size:13.5px;color:#5B7480;"">Doctor Phone</span>
      <span style=""font-size:14.5px;font-weight:600;font-family:ui-monospace,Menlo,Consolas,monospace;"">+{doctorPhone}</span>
    </div>
  </div>
  <div style=""margin:0 32px 12px;text-align:center;"">
    <a href=""{schedulePdfUrl}"" style=""display:block;width:100%;box-sizing:border-box;text-align:center;background:#2E9FB5;color:#ffffff;font-weight:700;font-size:15px;padding:14px 20px;border-radius:9px;text-decoration:none;"">Download Vaccination Schedule</a>
  </div>
  {webLinkLine}
  <hr style=""border:none;border-top:1px solid #DCE7E8;margin:0 32px;"">
  <div style=""padding:22px 32px 30px;font-size:12px;color:#5B7480;line-height:1.7;"">
    <p style=""font-weight:700;color:#0E2A38;font-size:13px;margin:0 0 4px;"">{child.Clinic.Name}</p>
    <p style=""margin:0;"">This email confirms registration for your child's vaccination record. If you did not request this, please contact the clinic directly using the number above.</p>
  </div>
</div>";

            return SendEmail(child.Email, body, child.Clinic.Name + " — Registration Confirmed", isHtml: true);
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



        public static void ParentAlertEmail(List<(string DoseName, DateTime Date)> dueDoses, Child child, string linkToken, string contentRootPath)
        {
            if (dueDoses.Count == 0) return;

            DateTime today = DateTime.UtcNow.AddHours(5).Date;
            string recordLink = "https://client.vaccinationcentre.com/child/vaccine/" + child.Id + "?t=" + Uri.EscapeDataString(linkToken);
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

            string doseCount = dueDoses.Count == 1 ? "1 dose" : dueDoses.Count + " doses";

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
    <h1 style=""font-size:21px;line-height:1.4;margin:0 0 6px;font-weight:700;color:#0E2A38;"">{doseCount} due for {child.Name}</h1>
    <p style=""font-size:14.5px;color:#5B7480;margin:0 0 24px;line-height:1.6;"">Please confirm your appointment with the clinic to keep the schedule on track.</p>
  </div>
  <div style=""margin:0 32px 24px;"">
    {doseCardsBuilder}
  </div>
  <div style=""margin:0 32px 12px;text-align:center;"">
    <a href=""{recordLink}"" style=""display:block;width:100%;box-sizing:border-box;text-align:center;background:#2E9FB5;color:#ffffff;font-weight:700;font-size:15px;padding:14px 20px;border-radius:9px;text-decoration:none;"">View Vaccination Record</a>
  </div>
  <p style=""text-align:center;margin:14px 32px 28px;font-size:12.5px;color:#5B7480;line-height:1.6;"">Opens your child's record — no separate login needed.</p>
  <div style=""margin:0 32px 28px;padding:16px 18px;border-radius:10px;background:#EEF9F6;border:1px solid #CDEBE3;text-align:center;"">
    <p style=""font-size:13px;font-weight:700;color:#0E2A38;margin:0 0 4px;"">Manage this from our app</p>
    <p style=""font-size:12.5px;color:#5B7480;line-height:1.55;margin:0 0 10px;"">Log in to see your child's full vaccine details, book your next appointment, and learn more about each vaccine.</p>
    <a href=""{recordLink}"" style=""display:inline-block;font-size:13px;font-weight:700;color:#2E9FB5;text-decoration:none;"">Log in &rarr;</a>
  </div>
  <hr style=""border:none;border-top:1px solid #DCE7E8;margin:0 32px;"">
  <div style=""padding:20px 32px 28px;font-size:12px;color:#5B7480;line-height:1.7;"">
    <p style=""font-weight:700;color:#0E2A38;font-size:13px;margin:0 0 4px;"">{child.Clinic.Name}</p>
    <p style=""margin:0;"">To reschedule or confirm this appointment, contact the clinic directly.</p>
    <p style=""margin-top:6px;"">&#128222; {child.Clinic.PhoneNumber}</p>
  </div>
</div>";

            SendEmail(child.Email, body, child.Clinic.Name + " — Vaccination Reminder", isHtml: true);
        }

        public static void DoctorForgotPassword(Doctor doctor)
        {
            string body = ""
                   + "Hi " + "<b>" + doctor.DisplayName + "</b>, <br />"
                   + "Your password is <b>" + doctor.User.Password + "</b>";

            SendEmail(doctor.Email, body);
        }
      
      
      
        public static void ParentForgotPassword(Child child)
        {
            string body = ""
                   + "Hi " + "<b>" + child.Name + "</b>, <br />"
                   + "Your password is <b>" + child.User.Password + "</b>";

            SendEmail(child.Email, body);
        }

        
        public static void PaForgotPassword(PersonalAssistant pa)
        {
            string body = ""
                   + "Hi " + "<b>" + pa.Name + "</b>, <br />"
                   + "Your password is <b>" + pa.User.Password + "</b>";

            SendEmail(pa.Email, body);
        }

        public static void ManagerForgotPassword(Manager manager)
        {
            string body = ""
                   + "Hi " + "<b>" + manager.Name + "</b>, <br />"
                   + "Your password is <b>" + manager.User.Password + "</b>";

            SendEmail(manager.Email, body);
        }

        public static void PersonalAssistantLoginDetails(PersonalAssistant pa, string password)
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

            SendEmail(pa.Email, body, "Your Personal Assistant Account Details");
        }
        
        public static void AgentLoginDetails(Agent agent, string password)
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

            SendEmail(agent.Email, body, "Your VacAgent Login Details");
        }

        #endregion
        // Returns a diagnostic string describing the outcome (null on success) — callers
        // that don't care can ignore the return value, same as before this was added.
        public static string SendEmail(string userEmail, string body, string subject = "vaccinationcentre.com", bool isHtml = false)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return "SendEmail: userEmail was null/empty";
            }

            using (var client = new HttpClient())
            {
                try
                {
                    client.Timeout = TimeSpan.FromSeconds(15);

                    var data = new
                    {
                        recipient_email = userEmail,
                        subject = subject,
                        body = body,
                        is_html = isHtml
                    };

                    var json = JsonSerializer.Serialize(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Send POST request to PHP endpoint
                    var response = client.PostAsync("https://trade.kplex.pk/testmail.php", content).Result;
                    var result = response.Content.ReadAsStringAsync().Result;

                    // Keep business flow non-blocking if email provider rejects request.
                    if (!result.Contains("\"status\":\"success\""))
                    {
                        Console.WriteLine("Failed to send email: " + result);
                        return "SendEmail: provider responded without success — HTTP " + (int)response.StatusCode + ": " + result;
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
}