using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class User
    {
        public User()
        {
            this.Childs = new HashSet<Child>();
            this.Doctors = new HashSet<Doctor>();
            this.Messages = new HashSet<Message>();
        }
        public long Id { get; set; }
        public string MobileNumber { get; set; }
        public string Password { get; set; }
        public string UserType { get; set; }
        public string CountryCode { get; set; }
        public virtual ICollection<Child> Childs { get; set; }
        public virtual ICollection<Doctor> Doctors { get; set; }
        public virtual ICollection<Message> Messages { get; set; }
        
    }

}