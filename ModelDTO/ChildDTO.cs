using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System ;

namespace VaccineAPI.ModelDTO
{

    public class ChildDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } public string CountryCode { get; set; }
        public string MobileNumber { get; set; }
        public string Password { get; set; }
        public string StreetAddress { get; set; }
        public string FatherName { get; set; }
        public string Email { get; set; }
        public DateTime DOB { get; set; }

        public string Gender  { get; set; }
        public string City  { get; set; }
        public string PreferredDayOfReminder  { get; set; }
        public string PreferredDayOfWeek  { get; set; }
        public string PreferredSchedule  { get; set; }
        public int? IsEPIDone  { get; set; }
        public int? IsVerified  { get; set; }
        public long ClinicId  { get; set; }
         [JsonIgnore]
        public ClinicDTO Clinic { get; set; }
       
        public long UserId  { get; set; }
         [JsonIgnore]
        public UserDTO User { get; set; }
        public bool IsBrand { get; set; }
        public bool IsConsultationFee { get; set; }

        //To select Vaccine of the child on add-new-child page
        public List<VaccineDTO> ChildVaccines { get; set; }
        public DateTime InvoiceDate { get; set; }
        
    }

}