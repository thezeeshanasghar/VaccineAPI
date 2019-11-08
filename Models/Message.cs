using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Message
    {
        public long Id { get; set; }
        public int MobileNumber { get; set; }
        public string Sms { get; set; }
        public string ApiResponse { get; set; }
        public string Created { get; set; }
        public int? UserId { get; set; }
        [JsonIgnore]
        public User User { get; set; }
    }

}