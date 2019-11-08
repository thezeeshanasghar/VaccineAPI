using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Brand
    {
        public long Id { get; set; }
        public int? Name { get; set; }
        public long VaccineId { get; set; }
        [JsonIgnore]
        public Vaccine Vaccine { get; set; }
    }

}