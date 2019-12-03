using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Dose
    {
         [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Dose()
        {
            this.DoctorSchedules = new HashSet<DoctorSchedule>();
            this.Schedules = new HashSet<Schedule>();
        }
        public long Id { get; set; }
        public string Name { get; set; }
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int? MinGap { get; set; }
        public int? DoseOrder { get; set; }
         public long VaccineId { get; set; }
         [JsonIgnore]
      //  public Vaccine Vaccine { get; set; }
         [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<DoctorSchedule> DoctorSchedules { get; set; }
        public virtual Vaccine Vaccine { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Schedule> Schedules { get; set; }
    }

}