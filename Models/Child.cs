using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System ;

namespace VaccineAPI.Models
{

    public class Child
    {
         [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Child()
        {
            this.FollowUps = new HashSet<FollowUp>();
            this.Schedules = new HashSet<Schedule>();
        }
        public long Id { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string Email { get; set; }
        public System.DateTime DOB { get; set; }
       // public string DOB { get; set; }
        public string Gender  { get; set; }
        public string Type  { get; set; }
        public string City  { get; set; }
        public string CNIC  { get; set; }
        public int PreferredDayOfReminder  { get; set; }
        public string PreferredDayOfWeek  { get; set; }
        public string PreferredSchedule  { get; set; }
        //public string MobileNumber { get; set; }
        public bool? IsEPIDone  { get; set; }
        public bool? IsVerified  { get; set; }
        public bool? IsInactive  { get; set; }
        //public bool? IsDivertAlert  { get; set; }
        public long ClinicId  { get; set; }
        public virtual Clinic Clinic { get; set; }
       
        public long UserId  { get; set; }
        [JsonIgnore]
        public User User { get; set; }
        
        //  public virtual string CountryCode { get; set; }
        // public virtual string MobileNumber { get; set; }
        // public virtual  string Password { get; set; }
        // public virtual  string StreetAddress { get; set; }
        // public virtual bool IsBrand { get; set; }
        // public virtual bool IsConsultationFee { get; set; }
      //   [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
      //  public virtual ICollection<Vaccine> ChildVaccines {get; set; } 
       // public virtual System.DateTime InvoiceDate {get; set;}
       // public virtual string InvoiceDate {get; set;}
         
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<FollowUp> FollowUps { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Schedule> Schedules { get; set; }
        
    }

}