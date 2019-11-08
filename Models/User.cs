using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class User
    {
        public long Id { get; set; }
        public int MobileNumber { get; set; }
        public int Password { get; set; }
        public string UserType { get; set; }
        public int? CountryCode { get; set; }
    }

}