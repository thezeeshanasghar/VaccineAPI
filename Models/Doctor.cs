using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Doctor
    {
        public long Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PMDC { get; set; }
        public int IsApproved { get; set; }
        public int ShowPhone { get; set; }
        public int ShowMobile { get; set; }
        public string PhoneNo { get; set; }
        public string ValidUpto { get; set; }
        public int? InvoiceNumber { get; set; }
        public string ProfileImage { get; set; }
        public string SignatureImage { get; set; }
        public string DisplayName { get; set; }
        public int AllowInvoice { get; set; }
        public int AllowChart { get; set; }
        public int AllowFollowUp { get; set; }
        public int AllowInventory { get; set; }
        public int SmsLimit { get; set; }
        public string DoctorType { get; set; }
        public string Qualification { get; set; }
        public string AdditionalInfo { get; set; }
        public long UserId { get; set; }
         [JsonIgnore]
        public User User { get; set; }
    }

}