using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class BrandDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public long VaccineId { get; set; }
        [JsonIgnore]
        public VaccineDTO Vaccine { get; set; }
    }

}