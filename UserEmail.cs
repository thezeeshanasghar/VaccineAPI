using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Web;
//using System.Web.Configuration;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI
{
    public class UserEmail
    {
      //  public static string teamEmail = WebConfigurationManager.AppSettings["TeamEmail"];
        //public static string teamEmailPassword = WebConfigurationManager.AppSettings["TeamEmailPassword"];
        public static string userName { get; set; }
        public static string userEmail { get; set; }

        #region Parent Email

        public static void ParentEmail(Child child)
        {
            string body = "Mr. " + child.FatherName + ",<br>";
            if (child.Gender == "Boy")
                body += "Your Son <b>" + child.Name + "</b>";

            if (child.Gender == "Girl")
                body += "Your Daughter <b>" + child.Name + "</b>";

            body += " has been registered at ";
            body += "<b>" + child.Clinic.Name + "</b><br>";


            body += "ID: <b>" + child.User.MobileNumber + "</b><br>Password: <b>" + child.User.Password + "</b><br/>"
                + "Clinic Phone Number <b>" + child.Clinic.PhoneNumber + "</b><br>";

            body += "Doctor Phone Number: <b>" + child.Clinic.Doctor.PhoneNo + "<b><br>";
            body += "Web Link: <a href=\"https://vaccs.io\" target=\"_blank\" rel=\"noopener noreferrer\">https://vaccs.io</a>";
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

            //using (MailMessage mail = new MailMessage("admin@vaccs.io", userEmail))
            //{
            //    mail.Subject = "Registered into Vaccs.io";
            //    mail.Body = body;
            //    mail.IsBodyHtml = true;

            //    SmtpClient smtp = new SmtpClient("mail.vaccs.io");
            //    smtp.EnableSsl = false;
            //    smtp.UseDefaultCredentials = false;
            //    smtp.Credentials = new NetworkCredential("admin@vaccs.io", "wIm7d1@3");
            //    smtp.Port = 25;
            //    try
            //    {
            //        smtp.Send(mail);
            //    }
            //    catch (Exception ex)
            //    {
            //        throw ex;
            //    }
            //}


           // using (MailMessage mail = new MailMessage(teamEmail, userEmail))
            {
              //  mail.Subject = "Registered into Vaccs.io";
                //mail.Body = body;
                //mail.IsBodyHtml = true;
                SmtpClient smtp = new SmtpClient();
                smtp.Host = "smtp.gmail.com";
                smtp.EnableSsl = true;
              //  NetworkCredential NetworkCred = new NetworkCredential(teamEmail, teamEmailPassword);
                smtp.UseDefaultCredentials = true;
            //    smtp.Credentials = NetworkCred;
                smtp.Port = 587;
                // try
                // {
                //     smtp.Send(mail);
                // }
                // catch (Exception ex)
                // {
                //     throw ex;
                // }
            }

        }
    }
}