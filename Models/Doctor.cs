using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System ;

namespace VaccineAPI.Models
{

    public class Doctor
    {
        public long Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PMDC { get; set; }
        public bool IsApproved { get; set; }
        public bool ShowPhone { get; set; }
         public bool ShowMobile { get; set; }
        public string PhoneNo { get; set; }
        public DateTime? ValidUpto { get; set; }
        public int? InvoiceNumber { get; set; }
        public string ProfileImage { get; set; }
        public string SignatureImage { get; set; }
        public string DisplayName { get; set; }
        public bool AllowInvoice { get; set; }
        public bool AllowChart { get; set; }
        public bool AllowFollowUp { get; set; }
        public bool AllowInventory { get; set; }
        public int SMSLimit { get; set; }
        public string DoctorType { get; set; }
        public string Qualification { get; set; }
        public string AdditionalInfo { get; set; }
        public long UserId { get; set; }
        public virtual string Speciality { get; set; }
         public virtual ICollection<Clinic> Clinics { get; set; }
         public virtual ICollection<Child> Childs { get; set; }
         public virtual User User { get; set; }

        // public virtual Clinic Clinics { get; set; }
        //public virtual Child Childs { get; set; }
        
    }

}