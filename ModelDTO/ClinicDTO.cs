using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class ClinicDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public int ConsultationFee { get; set; }
          public double Lat { get; set; }
        public double Long { get; set; }
        public long DoctorId { get; set; }
        public bool IsOnline { get; set; }
      //  public DoctorDTO Doctor { get; set; }
        // public string OffDays { get; set; }
        // public string StartTime { get; set; }
        // public string EndTime { get; set; } 
        
        
      //  [JsonIgnore]
        public int childrenCount { get; set; }
        public List<ClinicTimingDTO> ClinicTimings { get; set; }
         
    }

}