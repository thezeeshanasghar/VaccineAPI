using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.ModelDTO
{

    public class DoctorScheduleDTO
    {
        public long Id { get; set; }
        public long DoseId { get; set; }
        
        public virtual DoseDTO Dose { get; set; }
        public long DoctorId { get; set; }
        public DoctorDTO Doctor { get; set; }
        public int GapInDays { get; set; }
        public bool? IsActive { get; set; }
        
    }

}