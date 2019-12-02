using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.ModelDTO
{

    public class MessageDTO
    {
        
        public int Id { get; set; }
        public string MobileNumber { get; set; }
        public string SMS { get; set; }
        public string ApiResponse { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime Created { get; set; }
        public int UserId { get; set; }
        public UserDTO User { get; set; }
    }

}