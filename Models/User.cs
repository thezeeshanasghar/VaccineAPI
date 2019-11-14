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
        public int? CountryCode { get; set; }
        public virtual ICollection<Doctor> DoctorId { get; set; }
        public virtual ICollection<Doctor> AllowInventory { get; set; }
        public virtual ICollection<Doctor> AllowInvoice { get; set; }
        public virtual ICollection<Child> ChildId { get; set; }
        public virtual ICollection<Child> Email { get; set; }
        public virtual ICollection<Doctor> ProfileImage { get; set; }
        public virtual ICollection<Doctor> DoctorType { get; set; }
    }

}