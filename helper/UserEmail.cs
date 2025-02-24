using System;
using System.Net;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using Microsoft.AspNetCore.Mvc;

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

            body += " has been registered at vaccinationcentre.com.\n\n";

            body += "ID: " + child.User.MobileNumber + "\n";
            body += "Password: " + child.User.Password + "\n";
            body += "Clinic Phone Number: " + child.Clinic.PhoneNumber + "\n";
            body += "Doctor Phone Number: +92" + child.Clinic.Doctor.User.MobileNumber + "\n\n";

            body += "Website: https://vaccinationcentre.com\n";
            body += "View vaccination schedule: http://myapi.vaccinationcentre.com/api/child/" + child.Id + "/Download-Schedule-PDF\n";

            // TODO: Add website and Android app link
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
            using (var client = new WebClient())
            {
                try
                {
                    var data = new System.Collections.Specialized.NameValueCollection
                    {
                        ["recipient_email"] = userEmail,
                        ["subject"] = subject,
                        ["body"] = body
                    };

                    // Send POST request to PHP endpoint
                    byte[] response = client.UploadValues("https://vaccinationcentre.com/testmail.php", data);
                    string result = System.Text.Encoding.UTF8.GetString(response);

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