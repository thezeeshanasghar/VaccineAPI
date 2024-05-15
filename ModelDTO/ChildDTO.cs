using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{

    public class ChildDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string MobileNumber { get; set; }
        public string Password { get; set; }
        public string StreetAddress { get; set; }
        public string Guardian { get; set; } = "";
        public string FatherName { get; set; }
        public string Email { get; set; }

        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime DOB { get; set; }

        public string CountryCode { get; set; }
        public string Gender { get; set; }
        public bool IsSkip { get; set; }
        public string City { get; set; } = "Rawalpindi";
        public string CNIC { get; set; } = "";
        public bool IsEPIDone { get; set; }
        public bool IsVerified { get; set; }
        public bool IsInactive { get; set; }
        public long ClinicId { get; set; }
        
        [JsonIgnore]
        public ClinicDTO Clinic { get; set; }

        public long UserId { get; set; }
        public UserDTO User { get; set; }
        public bool IsBrand { get; set; }
        public bool IsConsultationFee { get; set; }

        //To select Vaccine of the child on add-new-child page
        public List<VaccineDTO> ChildVaccines { get; set; }

        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime InvoiceDate { get; set; }

   }

}