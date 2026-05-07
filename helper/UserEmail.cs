using System;
using System.Net;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;


namespace VaccineAPI
{
    public class UserEmail
    {
        #region Parent Email

        public static void ParentEmail(Child child)
        {
            string body = "";
            if (child.Gender == "Boy")
                body += "Mr. " + child.Name;

            if (child.Gender == "Girl")
                body += "Miss. " + child.Name;

            body += " has been registered at vaccinationcentre.com.\n";

            body += "ID: " + child.User.MobileNumber + "\nPassword: " + child.User.Password + "\n"
                + "Clinic Phone Number: " + child.Clinic.PhoneNumber + "\n";

            body += "Doctor Phone Number: +92" + child.Clinic.Doctor.User.MobileNumber + "\n";
            body += "Web Link: https://vaccinationcentre.com\n";
            body += "Visit http://myapi.vaccinationcentre.com/api/child/" + child.Id + "/Download-Schedule-PDF to view vaccination schedule.\n";
            //TODO: website and android link
            // SendEmail(child.Email, body,);
            SendEmail(child.Email, body);
        }



        public static void ParentAlertEmail(string doseName, DateTime scheduleDate, Child child)
        {
            string body = "Reminder: Vaccination for " + child.Name + " is due on " + scheduleDate;
            body += " (" + doseName + ")";
            //TODO: website and android link
            SendEmail(child.Email, body);
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
        public static void SendEmail(string userEmail, string body, string subject = "vaccinationcentre.com")
        {
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return;
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
                        body = body
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
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error sending email: " + ex.Message);
                    // Do not rethrow: email failures must not break API workflows.
                }
            }
        }
    }
}