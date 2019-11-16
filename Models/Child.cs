using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System ;

namespace VaccineAPI.Models
{

    public class Child
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string Email { get; set; }
        public string DOB { get; set; }
        public string Gender  { get; set; }
        public string City  { get; set; }
        public string PreferredDayOfReminder  { get; set; }
        public string PreferredDayOfWeek  { get; set; }
        public string PreferredSchedule  { get; set; }
        public bool? IsEPIDone  { get; set; }
        public bool? IsVerified  { get; set; }
        public long ClinicId  { get; set; }
         [JsonIgnore]
        public Clinic Clinic { get; set; }
       
        public long UserId  { get; set; }
         [JsonIgnore]
        public User User { get; set; }
        
         public virtual string CountryCode { get; set; }
        public virtual string MobileNumber { get; set; }
        public virtual  string Password { get; set; }
        public virtual  string StreetAddress { get; set; }
        public virtual bool IsBrand { get; set; }
        public virtual bool IsConsultationFee { get; set; }
        public virtual ICollection<Vaccine> ChildVaccines {get; set; } 
        public virtual DateTime InvoiceDate {get; set;}
        
    }

}