using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.Models
{

    public class NormalRange
    {
        public int Id { get; set; }
        public int Age { get; set; }                    // in Months
        public int Gender {get; set;}                   // 1 for male 2 for female
        public float WeightMin { get; set; }
        public float WeightMax { get; set; }
        public float HeightMin { get; set; }
        public float HeightMax { get; set; }
        public float OfcMin { get; set; }
        public float OfcMax { get; set; }
        
    }

}