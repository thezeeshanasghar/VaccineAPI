using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace VaccineAPI.Models
{

    public class Vaccine
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int MinAge { get; set; }
        public int MaxAge { get; set; }
    }

}