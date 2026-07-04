using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Clinic
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Clinic()
        {
            this.Childs = new HashSet<Child>();
            this.ClinicTimings = new HashSet<ClinicTiming>();
        }
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public decimal ConsultationFee { get; set; }
        // public string StartTime { get; set; }
        // public string EndTime { get; set; }
        public double Lat { get; set; }
        public double Long { get; set; }
        public string PhoneNumber { get; set; } = "";
        public bool IsOnline { get; set; }
        public string Address { get; set; } = "";
        public string MonogramImage { get; set; } = "";
        public long DoctorId { get; set; }
        public string RegNo { get; set; } = "";
        // Per-clinic inventory switch. When false, this clinic runs on brand pricing/names only
        // (no batch/lot/expiry, no stock deduction) even if the owning doctor has AllowInventory.
        // A clinic maintains full stock only when Doctor.AllowInventory && MaintainInventory.
        public bool MaintainInventory { get; set; } = true;
        // [JsonIgnore]
        public virtual Doctor Doctor { get; set; } = null!;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Child> Childs { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<ClinicTiming> ClinicTimings { get; set; }

    }

}