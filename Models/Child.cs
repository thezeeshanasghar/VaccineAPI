using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Child
    {
        public long Id { get; set; }
        public string FatherName { get; set; }
        public string Email { get; set; }
        public string DOB { get; set; }
        public string Gender  { get; set; }
        public string City  { get; set; }
        public string PreferredDayOfReminder  { get; set; }
        public string PreferredDayOfWeek  { get; set; }
        public string PreferredSchedule  { get; set; }
        public int? IsEPIDone  { get; set; }
        public int? IsVerified  { get; set; }
        public long ClinicId  { get; set; }
         [JsonIgnore]
        public Clinic Clinic { get; set; }
       
        public long UserId  { get; set; }
         [JsonIgnore]
        public User User { get; set; }
        
    }

}