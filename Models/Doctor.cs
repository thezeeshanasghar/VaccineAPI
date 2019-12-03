using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System ;

namespace VaccineAPI.Models
{

    public class Doctor
    {
      [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Doctor()
        {
            this.BrandAmounts = new HashSet<BrandAmount>();
            this.BrandInventories = new HashSet<BrandInventory>();
            this.DoctorSchedules = new HashSet<DoctorSchedule>();
            this.FollowUps = new HashSet<FollowUp>();
            this.Clinics = new HashSet<Clinic>();
        }
        public long Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PMDC { get; set; }
        public bool IsApproved { get; set; }
        public bool ShowPhone { get; set; }
         public bool ShowMobile { get; set; }
        public string PhoneNo { get; set; }
        public DateTime? ValidUpto { get; set; }
        public int? InvoiceNumber { get; set; }
        public string ProfileImage { get; set; }
        public string SignatureImage { get; set; }
        public string DisplayName { get; set; }
        public bool AllowInvoice { get; set; }
        public bool AllowChart { get; set; }
        public bool AllowFollowUp { get; set; }
        public bool AllowInventory { get; set; }
        public int SMSLimit { get; set; }
        public string DoctorType { get; set; }
        public string Qualification { get; set; }
        public string AdditionalInfo { get; set; }
        public long UserId { get; set; }
        public User User { get; set; }
        // [JsonIgnore]
       // public virtual string Speciality { get; set; }
         public virtual ICollection<Clinic> Clinics { get; set; }
         public virtual ICollection<Child> Childs { get; set; }
         [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
      //   [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
          public ICollection<DoctorSchedule> DoctorSchedules { get; set; }
        public virtual ICollection<BrandAmount> BrandAmounts { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<BrandInventory> BrandInventories { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<FollowUp> FollowUps { get; set; }
         
        
         

        // public virtual Clinic Clinics { get; set; }
        //public virtual Child Childs { get; set; }
        
    }

}