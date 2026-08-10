using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{

    public class DoctorDTO
    {
        public long Id { get; set; }
        public string FirstName { get; set; } = "";
        // public string LastName { get; set; }
        public string Email { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public string MobileNumber { get; set; } = "";
        public string PhoneNo { get; set; } = "";
        public string Password { get; set; } = "";
        public string PMDC { get; set; } = "";
        public bool ShowPhone { get; set; }
        public bool ShowMobile { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime ValidUpto { get; set; }
        public int? InvoiceNumber { get; set; }
        public string ProfileImage { get; set; } = "";
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
        public string AdditionalInfo { get; set; } = "";
        // public long UserId { get; set; }

        // [JsonIgnore]
        public UserDTO User { get; set; } = null!;

        public string[] Speciality { get; set; } = Array.Empty<string>();

        public ClinicDTO ClinicDTO { get; set; } = null!;
        public List<ClinicDTO> Clinics { get; set; } = new List<ClinicDTO>();
        //public ClinicDTO Clinics { get; set; }

        //to show child info on change doctor page
        public ChildDTO ChildDTO { get; set; } = null!;
    }

}