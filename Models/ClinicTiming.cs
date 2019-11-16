using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System ;

namespace VaccineAPI.Models
{

    public class ClinicTiming
    {
        public long Id { get; set; }
        public string Day { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Session { get; set; }
        public bool IsOpen { get; set; }
        public long ClinicId { get; set; }
         [JsonIgnore]
        public Clinic Clinic { get; set; }
       
    }

}