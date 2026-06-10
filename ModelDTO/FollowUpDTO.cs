using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{

    public class FollowUpDTO
    {
        public long Id { get; set; }
        public string? Disease { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime CurrentVisitDate { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime? NextVisitDate { get; set; }
        public float? Weight { get; set; }
        public float? Height { get; set; }
        public float? OFC { get; set; }
        public float? BloodPressure { get; set; }
        public float? BloodSugar { get; set; }
        public float? WeightMin { get; set; }
        public float? WeightMax { get; set; }
        public float? HeightMin { get; set; }
        public float? HeightMax { get; set; }
        public float? OfcMin { get; set; }
        public float? OfcMax { get; set; }
        public long ChildId { get; set; }
        public ChildDTO Child { get; set; } = null!;
        public long DoctorId { get; set; }
        public DoctorDTO Doctor { get; set; } = null!;
    }

}