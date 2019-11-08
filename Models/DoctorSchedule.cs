using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.Models
{

    public class DoctorSchedule
    {
        public long Id { get; set; }
        public int DoseId { get; set; }
        
         [JsonIgnore]
        public Dose Dose { get; set; }
       
        public int DoctorId { get; set; }
         [JsonIgnore]
        public Doctor Doctor { get; set; }
       
        public int GapInDays { get; set; }
        
    }

}