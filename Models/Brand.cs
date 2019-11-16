using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Brand
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public long VaccineId { get; set; }
        public virtual Vaccine Vaccine { get; set; }
    }

}