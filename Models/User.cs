using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class User
    {
        public long Id { get; set; }
        public string MobileNumber { get; set; }
        public string Password { get; set; }
        public string UserType { get; set; }
        public string CountryCode { get; set; }
        public virtual long DoctorId { get; set; }
        public virtual bool AllowInventory { get; set; }
        public virtual bool AllowInvoice { get; set; }
        public virtual long ChildId { get; set; }
        public virtual string Email { get; set; }
        public virtual string ProfileImage { get; set; }
        public virtual string DoctorType { get; set; }
        
    }

}