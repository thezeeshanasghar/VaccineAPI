using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VaccineAPI.ModelDTO
{
    public class ChangePasswordRequestDTO
    {
        public int UserId { get; set; }
        public string OldPassword { get; set; }
        public string NewPassword { get; set;  } 
    }
}