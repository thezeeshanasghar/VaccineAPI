using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class UserDTO
    {
        public long Id { get; set; }
        public string MobileNumber { get; set; }
        public string Password { get; set; }
        public string UserType { get; set; }
        public int DoctorID { get; set; }
        public bool AllowInventory { get; set; }
        public bool AllowInvoice { get; set; }
        public int ChildID { get; set; }
        public int? CountryCode { get; set; }
        public string Email { get; set; }
        public string ProfileImage { get; set; }
        public string DoctorType { get; set; }
    }

}