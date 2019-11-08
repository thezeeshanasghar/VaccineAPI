using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class ClinicTiming
    {
        public long Id { get; set; }
        public string Day { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public string Session { get; set; }
        public int IsOpen { get; set; }
        public int ClinicId { get; set; }
         [JsonIgnore]
        public Clinic Clinic { get; set; }
       
    }

}