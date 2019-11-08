using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.Models
{

    public class DoctorSchedule
    {
        public long Id { get; set; }
        public long DoseId { get; set; }
        
         [JsonIgnore]
        public Dose Dose { get; set; }
       
        public long DoctorId { get; set; }
         [JsonIgnore]
        public Doctor Doctor { get; set; }
       
        public int GapInDays { get; set; }
        
    }

}