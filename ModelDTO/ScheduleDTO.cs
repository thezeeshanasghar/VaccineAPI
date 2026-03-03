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
        public long ChildId { get; set; }
        public int DoseId { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime Date { get; set; }
        public float? Weight { get; set; }
        public float? Height { get; set; }
        public float? Circle { get; set; }
        public bool IsPAApprove { get; set; }
        public bool IsDone { get; set; }
        public bool Due2EPI { get; set; }
        public bool? IsSkip { get; set; }
        public bool? IsDisease { get; set; }
        public string DiseaseYear { get; set; } = "";
        public DoseDTO Dose { get; set; } = null!;
        public virtual ChildDTO Child { get; set; } = null!;
        public List<BrandDTO> Brands { get; set; } = new List<BrandDTO>();
        public BrandDTO Brand { get; set; } = null!;
        public long? BrandId { get; set; }
        public decimal? Amount { get; set; }
        public string Manufacturer { get; set; } = "";
        public string Lot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
        public List<ScheduleBrandDTO> ScheduleBrands { get; set; } = new List<ScheduleBrandDTO>();
        public long DoctorId { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime GivenDate { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime FromDate { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime ToDate { get; set; }
        // FOR INVOICE
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime? InvoiceDate { get; set; }
        public List<ClinicDTO> Clinics { get; set; } = new List<ClinicDTO>();
    }

}