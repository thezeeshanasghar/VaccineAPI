using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VaccineAPI.ModelDTO
{
    public class ScheduleBrandDTO
    {
        public int ScheduleId { get; set; }
        public long? BrandId { get; set; }
        public string? Manufacturer { get; set; }
        public string? Lot { get; set; }
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
    }
}