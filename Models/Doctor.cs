using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.Models
{

    public class Doctor
    {
        public Doctor()
        {
            this.BrandAmounts = new HashSet<BrandAmount>();
            // this.BrandInventories = new HashSet<BrandInventory>();
            this.DoctorSchedules = new HashSet<DoctorSchedule>();
            this.FollowUps = new HashSet<FollowUp>();
            this.Clinics = new HashSet<Clinic>();
        }
        public long Id { get; set; }
        public string FirstName { get; set; } = "";
        // public string LastName { get; set; }
        public string Email { get; set; } = "";
        public string PMDC { get; set; } = "";
        public bool ShowPhone { get; set; }
        public bool ShowMobile { get; set; }
        public string PhoneNo { get; set; } = "";
        public DateTime? ValidUpto { get; set; }
        public int? InvoiceNumber { get; set; }
        public string ProfileImage { get; set; } = "Resources/Images/avatar.png";
        public string DisplayName { get; set; } = "";
        public bool AllowInvoice { get; set; }
        public bool AllowChart { get; set; }
        public bool AllowFollowUp { get; set; }
        public bool AllowInventory { get; set; }
        public bool AllowSupplier { get; set; }
        public bool AllowFinancial { get; set; }
        public bool AllowSalesReport { get; set; }
        public bool AllowAgent { get; set; }
        public bool AllowTravel { get; set; }
        public bool AllowAdult { get; set; }
        public bool AllowDevelopmentalAssessment { get; set; }
        public bool AllowHomeBooking { get; set; }
        public bool AllowClinicBooking { get; set; }
        public bool AllowAnalytics { get; set; }
        public bool AllowAssistant { get; set; }
        public bool AllowColdChain { get; set; }
        public int SMSLimit { get; set; }
        public string DoctorType { get; set; } = "";
        public string? Qualification { get; set; }
        public string AdditionalInfo { get; set; } = string.Empty;
        public long UserId { get; set; }
        public User User { get; set; } = null!;
        public virtual ICollection<Clinic> Clinics { get; set; } = new HashSet<Clinic>();
        public virtual ICollection<Child> Childs { get; set; } = new HashSet<Child>();
        public ICollection<DoctorSchedule> DoctorSchedules { get; set; } = new HashSet<DoctorSchedule>();
        public virtual ICollection<BrandAmount> BrandAmounts { get; set; } = new HashSet<BrandAmount>();
        // public virtual ICollection<BrandInventory> BrandInventories { get; set; }
        public virtual ICollection<FollowUp> FollowUps { get; set; } = new HashSet<FollowUp>();
    }
}