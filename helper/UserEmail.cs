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
                body += ("Mr. " + child.Name + "</b>");

            if (child.Gender == "Girl")
                body += ("Miss. " + child.Name + "</b>");

            body += " has been registered at vaccinationcentre.com ";


            body += "ID: <b>" + child.User.MobileNumber + "</b><br>Password: <b>" + child.User.Password + "</b><br/>"
                + "Clinic Phone Number <b>" + child.Clinic.PhoneNumber + "</b><br>";

            body += "Doctor Phone Number: <b>+92" + child.Clinic.Doctor.User.MobileNumber + "<b><br>";
            body += "Web Link: <a href=\"https://vaccinationcentre.com\" target=\"_blank\" rel=\"noopener noreferrer\">https://vaccinationcentre.com</a><br>";
            body += "<a href=\"http://myapi.vaccinationcentre.com/api/child/" + child.Id + "/Download-Schedule-PDF\" target=\"_blank\" rel=\"noopener noreferrer\">Click here</a>" + " to view vaccination schedule";
            //TODO: website and android link
            SendEmail(child.Email, body);
        }



        public static void ParentAlertEmail(string doseName, DateTime scheduleDate, Child child)
        {
            string body = "Reminder: Vaccination for " + child.Name + " is due on " + scheduleDate;
            body += " (" + doseName + ")";
            //TODO: website and android link
            SendEmail(child.Email, body);
        }
        #endregion

        #region Child Email
        //Forgot Password Email
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

        #endregion

        public static void SendEmail(string userEmail, string body, string subject = "vaccinationcentre.com")
        {
            using (var client = new HttpClient())
            {
                try
                {
                    var data = new
                    {
                        recipient_email = userEmail,
                        subject = subject,
                        body = body
                    };

                    var json = JsonSerializer.Serialize(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Send POST request to PHP endpoint
                    var response = client.PostAsync("https://vaccinationcentre.com/testmail.php", content).Result;
                    var result = response.Content.ReadAsStringAsync().Result;

                    // Optionally handle the JSON response
                    if (!result.Contains("\"status\":\"success\""))
                    {
                        throw new Exception("Failed to send email: " + result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error sending email: " + ex.Message);
                    throw;
                }
            }
        }
    }
}