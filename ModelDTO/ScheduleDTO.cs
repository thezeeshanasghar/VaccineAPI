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
        public bool IsDone { get; set; }
        public bool Due2EPI { get; set; }
        public bool? IsSkip { get; set; }
        public bool? IsDisease { get; set; }
        public string DiseaseYear { get; set; } = "";
        public DoseDTO Dose { get; set; }
        public virtual ChildDTO Child { get; set; }
        public List<BrandDTO> Brands { get; set; }
        public BrandDTO Brand { get; set; }
        public long? BrandId { get; set; }
        public int? Amount { get; set; }
        public List<ScheduleBrandDTO> ScheduleBrands { get; set; }
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
        public List<ClinicDTO> Clinics { get; set; }
    }

}