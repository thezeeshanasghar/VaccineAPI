using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.Models
{

    public class Messages
    {
        public long ChildId { get; set; }
        public string MobileNumber { get; set; }
        public string SMS { get; set; }
    }

}