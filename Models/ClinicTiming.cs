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
        public System.TimeSpan StartTime { get; set; }
        public System.TimeSpan EndTime { get; set; }
        public string Session { get; set; }
        public bool IsOpen { get; set; }
        public long ClinicId { get; set; }
        public virtual Clinic Clinic { get; set; }
       
    }

}