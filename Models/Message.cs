using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.Models
{

    public class Message
    {
        public long Id { get; set; }
        public string MobileNumber { get; set; }
        public string SMS { get; set; }
        public string ApiResponse { get; set; }
        public DateTime Created { get; set; }
        public long? UserId { get; set; }
        public virtual User User { get; set; }
    }

}