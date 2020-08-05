using System;
using System.Globalization;
using System.Linq;
using System.Web;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using Microsoft.EntityFrameworkCore;
namespace VaccineAPI
{
    public  class UserSMS
    {
       public Context _db;
         

        public UserSMS(Context context)
        {
            _db = context;
        
        }
        static TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
        public string DoctorSMS(DoctorDTO doctor)
        {
            string body = "Hi " + textInfo.ToTitleCase(doctor.FirstName) + " " + textInfo.ToTitleCase(doctor.LastName) + " \n"
                + "You are Succesfully registered in registered in vaccine.pk\n\n"
                + "Id: " + doctor.MobileNumber + "\n"
                + "Password: " + doctor.Password + "\n"
                + "Weblink: https://vaccine.pk";
            var response = SendSMS(doctor.CountryCode, doctor.MobileNumber, doctor.Email, body);
            addMessageToDB(doctor.MobileNumber, response, body, 1);
            return response;
        }
        public string ParentSMS(Child child)
        {

            string sms1 = "Mr. " + child.FatherName + "\n";
            if (child.Gender == "Boy")
                sms1 += "Your Son " + textInfo.ToTitleCase(child.Name);

            if (child.Gender == "Girl")
                sms1 += "Your Daughter " + textInfo.ToTitleCase(child.Name);

            sms1 += " has been registered with Dr. " + textInfo.ToTitleCase(child.Clinic.Doctor.FirstName) + " " + textInfo.ToTitleCase(child.Clinic.Doctor.LastName);
            sms1 += " at " + child.Clinic.Name.Replace("&", "and") + "\n";

            var response1 = SendSMS(child.User.CountryCode, child.User.MobileNumber, child.Email, sms1);

            string sms2 = "Id: " + child.User.MobileNumber + "\nPassword: " + child.User.Password
                  + "\nClinic: " + child.Clinic.PhoneNumber + "\nhttps://vaccs.io/";

            var response2 = SendSMS(child.User.CountryCode, child.User.MobileNumber, child.Email, sms2);
             addMessageToDB(child.User.MobileNumber, response1, sms1, child.Clinic.Doctor.User.Id);
             addMessageToDB(child.User.MobileNumber, response2, sms2, child.Clinic.Doctor.User.Id);
             minusDoctorSMSCount(child.Clinic.Doctor);
             minusDoctorSMSCount(child.Clinic.Doctor);
            return response1 + response2;

        }
        public string ParentSMSAlert(string doseName, DateTime scheduleDate, Child child)
        {

            string sms1 = "Mr. " + child.FatherName + "\n";
            sms1 += doseName + " Vaccine for ";
            if (child.Gender == "Boy")
                sms1 += "your son " + textInfo.ToTitleCase(child.Name);

            if (child.Gender == "Girl")
                sms1 += "your daughter " + textInfo.ToTitleCase(child.Name);

            sms1 += " is due ";
            if (scheduleDate.Date == DateTime.UtcNow.AddHours(5).Date)
                sms1 += "Today ";
            else
                sms1 += scheduleDate.Date.ToString("MM-dd-yyyy");

            sms1 += " at " + textInfo.ToTitleCase(child.Clinic.Name) + "\n";
            sms1 += "Plz confirm your appointment with Dr. " + textInfo.ToTitleCase(child.Clinic.Doctor.FirstName) + " " + textInfo.ToTitleCase(child.Clinic.Doctor.LastName);
            sms1 += " @ " + child.Clinic.Doctor.PhoneNo + " OR " + child.Clinic.PhoneNumber;
            var response1 = SendSMS(child.User.CountryCode, child.User.MobileNumber, child.Email, sms1);
            addMessageToDB(child.User.MobileNumber, response1, sms1, child.Clinic.Doctor.User.Id);
            minusDoctorSMSCount(child.Clinic.Doctor);
            return response1;
        }

        public string DoctorForgotPasswordSMS(Doctor doctor)
        {
            string body = "";
            body += "Hi " + textInfo.ToTitleCase(doctor.DisplayName);
            body += ",Your password is " + doctor.User.Password;
            var response = SendSMS(doctor.User.CountryCode, doctor.User.MobileNumber, doctor.Email, body);
            addMessageToDB(doctor.User.MobileNumber, response, body, 1);
            return response;
        }
        public string ParentForgotPasswordSMS(Child child)
        {
            string body = "";
            body += "Hi " + textInfo.ToTitleCase(child.FatherName);
            body += ",Your password is " + child.User.Password;
            var response = SendSMS(child.User.CountryCode, child.User.MobileNumber, child.Email, body);
            addMessageToDB(child.User.MobileNumber, response, body, 1);
            return response;
        }

        public string ParentFollowUpSMSAlert(FollowUp followUp)
        {
            string sms1 = "Followup visit of ";
            if (followUp.Child.Gender == "Boy")
                sms1 += "son " + textInfo.ToTitleCase(followUp.Child.Name);

            if (followUp.Child.Gender == "Girl")
                sms1 += "daughter " + textInfo.ToTitleCase(followUp.Child.Name);

            sms1 += " is due ";
            if (followUp.NextVisitDate == DateTime.UtcNow.AddHours(5).Date)
                sms1 += "Today";
            else
                sms1 += followUp.NextVisitDate;

            sms1 += " with Dr. " + textInfo.ToTitleCase(followUp.Doctor.FirstName) + " " + textInfo.ToTitleCase(followUp.Doctor.LastName) + " at " + textInfo.ToTitleCase(followUp.Child.Clinic.Name) + ". ";
            sms1 += "Kindly confirm your appointment at " + followUp.Doctor.PhoneNo;

            var response1 = SendSMS(followUp.Child.User.CountryCode, followUp.Child.User.MobileNumber, followUp.Child.Email, sms1);
           addMessageToDB(followUp.Child.User.MobileNumber, response1, sms1, followUp.Child.Clinic.Doctor.User.Id);
            minusDoctorSMSCount(followUp.Child.Clinic.Doctor);

            return response1;
        }

         public void addMessageToDB(string mobileNumber, string apiResponse, string sms, long userId)
        {
           // using (VDEntities entities = new VDEntities())
        
        //  using (VDEntities entities = new VDEntities())
            {
               
                Message m = new Message();
                m.MobileNumber = mobileNumber;
                m.ApiResponse = apiResponse;
                m.SMS = sms;
                m.UserId = userId;
                _db.Messages.Add(m);
                _db.SaveChanges();
            }
        }
        public void minusDoctorSMSCount(Doctor doctor)
        {
           // using (VDEntities entities = new VDEntities())
            {
                Doctor dbDoctor = _db.Doctors.Where(x=>x.Id == doctor.Id).FirstOrDefault();
                dbDoctor.SMSLimit--; 
                _db.SaveChanges();
            }
        }
        
        public string SendSMS(string CountryCode, string MobileNumber, string Email, string text)
        {

        //     //string webTarget = "http://icworldsms.com:82/Service.asmx/SendSMS?SessionId=Ud1vaibfSexGvkohsFVVVEzoWrhUKfpylFZqOFVy9EB7CaifKP&CompaignName=text&MobileNo={0}&MaskName=VACCS+IO&Message={1}&MessageType=English";
        //     //string url = String.Format(webTarget, "0" + MobileNumber, HttpUtility.0UrlEncode(text));

          //  string webTarget = "http://58.65.138.38:8181/sc/smsApi/sendSms?username=vccsio&password=123456&mobileNumber={0}&message={1}&mask=VACCS%20IO";
          string webTarget = "https://brandyourtext.com/sms/api/send?username=vaccsio&password=123456&mask=VACCS%20IO&mobile={0}&message={1}";
         // view-source:https://brandyourtext.com/sms/api/send?username=vaccsio&password=123456&mask=VACCS%20IO&mobile=3143041544&message=Test%20Message
            string url = String.Format(webTarget, "92" + MobileNumber, HttpUtility.UrlEncode(text));

          return Controllers.VaccineController.sendRequest(url);
        
           //return ("temprorarily");
        }

        
    }
}