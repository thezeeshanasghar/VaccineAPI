using System;
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
        public string MobileNumber { get; set; } = "";
        public string Password { get; set; } = "";
        public string UserType { get; set; } = "";
        public string CountryCode { get; set; } = "";
        // Changed whenever the password changes; logged-in devices carrying a stale stamp are forced to log out.
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();
        public virtual ICollection<Child> Childs { get; set; } = new HashSet<Child>();
        public virtual ICollection<Doctor> Doctors { get; set; } = new HashSet<Doctor>();
        public virtual ICollection<Message> Messages { get; set; } = new HashSet<Message>();

    }

}