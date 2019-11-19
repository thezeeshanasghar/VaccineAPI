using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Web;

namespace VaccineAPI.ModelDTO
{

    public class ScheduleDTO
    {
        public long Id { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime Date { get; set; }
        public float? Weight { get; set; }
        public float? Height { get; set; }
        public float? Circle { get; set; }
        public bool IsDone { get; set; }
        public bool Due2EPI { get; set; }
         public long? BrandId { get; set; }
        [JsonIgnore]
        public List <BrandDTO> Brand { get; set; }
        
        public long ChildId { get; set; }
        [JsonIgnore]
        public ChildDTO Child { get; set; }
       
        public int DoseId { get; set; }
        [JsonIgnore]
        public DoseDTO Dose { get; set; }
       [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime GivenDate { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime FromDate { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime ToDate { get; set; }
        public List <ClinicDTO> Clinics { get; set; }
    }

}