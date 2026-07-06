using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class UserDTO
    {
        public long Id { get; set; }
        public string MobileNumber { get; set; } = "";
        public string Password { get; set; } = "";
        public string UserType { get; set; } = "";
        public long DoctorId { get; set; }
        public bool AllowInventory { get; set; }
        public bool AllowSupplier { get; set; }
        public bool AllowFinancial { get; set; }
        public bool AllowSalesReport { get; set; }
        public bool AllowAgent { get; set; }
        public bool AllowInvoice { get; set; }
        public long ChildId { get; set; }
        public string CountryCode { get; set; } = "";
        public string Email { get; set; } = "";
        public string ProfileImage { get; set; } = "";
        public string DoctorType { get; set; } = "";
        public long PAId { get; set; }
        public bool IsVerified { get; set; }
        public string Name { get; set; } = "";
        public string SecurityStamp { get; set; } = "";
    }

}