using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Clinic
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int ConsultationFee { get; set; }
        public string OffDays { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public float Lat { get; set; }
        public float Long { get; set; }
        public string PhoneNumber { get; set; }
        public int IsOnline { get; set; }
        public string Address { get; set; }
        public long DoctorId { get; set; }
        [JsonIgnore]
        public Doctor Doctor { get; set; }
         
    }

}