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
                body += ("Mr. " + child.Name + "</b>");

            if (child.Gender == "Girl")
                body += ("Miss. " + child.Name + "</b>");

            body += " has been registered at vaccine.pk ";


            body += "ID: <b>" + child.User.MobileNumber + "</b><br>Password: <b>" + child.User.Password + "</b><br/>"
                + "Clinic Phone Number <b>" + child.Clinic.PhoneNumber + "</b><br>";

            body += "Doctor Phone Number: <b>+92" + child.Clinic.Doctor.User.MobileNumber + "<b><br>";
            body += "Web Link: <a href=\"https://vaccine.pk\" target=\"_blank\" rel=\"noopener noreferrer\">https://vaccine.pk</a><br>";
            body += "<a href=\"http://fernflowers.com/api/child/" + child.Id + "/Download-Schedule-PDF\" target=\"_blank\" rel=\"noopener noreferrer\">Click here</a>" + " to view vaccination schedule";
            //TODO: website and android link
            SendEmail(child.Name, child.Email, body);
        }



        public static void ParentAlertEmail(string doseName, DateTime scheduleDate, Child child)
        {
            string body = "Reminder: Vaccination for " + child.Name + " is due on " + scheduleDate;
            body += " (" + doseName + ")";
            //TODO: website and android link
            SendEmail(child.Name, child.Email, body);
        }
        #endregion

        #region Child Email

        public static void DoctorEmail(DoctorDTO doctor)
        {
            string body = "Hi " + "<b>" + doctor.FirstName + " " + doctor.LastName + "</b>, <br />"
                + "You are succesfully registered in <b>Vaccs.io</b>.<br /><br />"
                + "Your account credentials are: <br />"
                + "ID/Mobile Number: " + doctor.MobileNumber + "<br />"
                + "Password: " + doctor.Password + "<br />"
                + "Web Link: <a href=\"https://vaccs.io\" target=\"_blank\" rel=\"noopener noreferrer\">https://vaccs.io</a>";
            SendEmail(doctor.FirstName, doctor.Email, body);
        }
        //send email to doctor when child change the doctor
        public static void DoctorEmail(ChildDTO child, string doctor)
        {
            string body = "";
            if (doctor == "old")
            {
                body = "Hi " + "<b>" + child.Clinic.Doctor.FirstName + " " + child.Clinic.Doctor.LastName + "</b>, <br />"
               + "Your patient: <b>" + child.Name + "</b> selected some other doctor";
            }
            else
            {
                body = "Hi " + "<b>" + child.Clinic.Doctor.FirstName + " " + child.Clinic.Doctor.LastName + "</b>, <br />"
                + "A new patient: <b>" + child.Name + "</b> has been registered to you";
            }

            SendEmail(child.Clinic.Doctor.FirstName, child.Clinic.Doctor.Email, body);
        }
        //Forgot Password Email
        public static void DoctorForgotPassword(Doctor doctor)
        {
            string body = ""
                   + "Hi " + "<b>" + doctor.DisplayName + "</b>, <br />"
                   + "Your password is <b>" + doctor.User.Password + "</b>";

            SendEmail(doctor.DisplayName, doctor.Email, body);
        }
        public static void ParentForgotPassword(Child child)
        {
            string body = ""
                   + "Hi " + "<b>" + child.Name + "</b>, <br />"
                   + "Your password is <b>" + child.User.Password + "</b>";

            SendEmail(child.Name, child.Email, body);
        }

        #endregion

        public static void SendEmail(string userName, string userEmail, string body)
        {
            using (System.Net.Mail.MailMessage mm = new System.Net.Mail.MailMessage("sender@skintechno.com", userEmail))
            {
                mm.Subject = "vaccine.pk";
                mm.Body = body;
                mm.IsBodyHtml = true;

                System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient();

               
                    smtp.Host = "skintechno.com";
                    smtp.EnableSsl = false;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential("sender@skintechno.com", "XDQ0@73GvLKJ");
                    smtp.Port = 587;

                try
                {
                    smtp.Send(mm);
                }
                catch (Exception ex)
                {
                    // Handle the exception here
                    Console.WriteLine("Error sending email: " + ex.Message);
                    throw;
                
                }
            }
        }
        public static void SendEmail2(string userEmail, string body)
        {
            using (System.Net.Mail.MailMessage mm = new System.Net.Mail.MailMessage("sender@skintechno.com", userEmail))
            {
                mm.Subject = "vaccine.pk";
                mm.Body = body;
                mm.IsBodyHtml = true;

                System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient();

                // // Check if running in local development environment
                // Console.WriteLine(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
                // if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                // {
                //     // Use Gmail SMTP settings for local development
                //     smtp.Host = "smtp.gmail.com";
                //     smtp.EnableSsl = true;
                //     smtp.UseDefaultCredentials = false;
                //     smtp.Credentials = new NetworkCredential("majliscom482@gmail.com", "123Pakistan@");
                //     smtp.Port = 587; // Gmail uses port 587 for TLS
                // }
                // else
                // {
                    // Use skintechno.com SMTP settings for production
                    smtp.Host = "skintechno.com";
                    smtp.EnableSsl = false;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential("sender@skintechno.com", "XDQ0@73GvLKJ");
                    smtp.Port = 587; // Adjust if different for skintechno.com
                // }

                try
                {
                    smtp.Send(mm);
                }
                catch (Exception ex)
                {
                    // Handle the exception here
                    Console.WriteLine("Error sending email: " + ex.Message);
                    throw;
                
                }
            }
        }

   public static void SendEmail3(string userEmail, string body)
        {
            using (System.Net.Mail.MailMessage mm = new System.Net.Mail.MailMessage("sender@skintechno.com", userEmail))
            {
                mm.Subject = "vaccine.pk";
                mm.Body = body;
                mm.IsBodyHtml = true;

                System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient();

                // // Check if running in local development environment
                // Console.WriteLine(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
                // if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                // {
                //     // Use Gmail SMTP settings for local development
                //     smtp.Host = "smtp.gmail.com";
                //     smtp.EnableSsl = true;
                //     smtp.UseDefaultCredentials = false;
                //     smtp.Credentials = new NetworkCredential("majliscom482@gmail.com", "123Pakistan@");
                //     smtp.Port = 587; // Gmail uses port 587 for TLS
                // }
                // else
                // {
                    // Use skintechno.com SMTP settings for production
                    smtp.Host = "skintechno.com";
                    smtp.EnableSsl = false;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential("sender@skintechno.com", "XDQ0@73GvLKJ");
                    smtp.Port = 587; // Adjust if different for skintechno.com
                // }

                try
                {
                    smtp.Send(mm);
                }
                catch (Exception ex)
                {
                    // Handle the exception here
                    Console.WriteLine("Error sending email: " + ex.Message);
                    throw;
                
                }
            }
        }

    }
}