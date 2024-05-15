using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class DoseDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int? MinGap { get; set; }
        public int? DoseOrder { get; set; }
         public long VaccineId { get; set; }
        public VaccineDTO Vaccine { get; set; }
    }

}