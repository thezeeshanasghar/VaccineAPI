using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class BrandInventoryDTO
    {
        public long Id { get; set; }
        public int Count { get; set; }
        public long BrandId { get; set; }
        
        [JsonIgnore]
        public BrandDTO Brand { get; set; }
      
        public long DoctorId { get; set; }
         [JsonIgnore]
        public DoctorDTO Doctor { get; set; }
        public string VaccineName { get; set; }
    }

}