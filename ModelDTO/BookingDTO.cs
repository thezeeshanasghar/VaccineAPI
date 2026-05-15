using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class BookingDTO
    {
        public long ChildId { get; set; }
        public long UserId { get; set; }
        public string ChildName { get; set; } = "";
        public string FatherName { get; set; } = "";
        public string DOB { get; set; } = "";
        public string Vaccines { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public string Location { get; set; } = "";
        public string Card { get; set; } = "";
        public string City { get; set; } = "";
        public string BookingDate { get; set; } = "";
        public string PreferredDate { get; set; } = "";
        public string Comments { get; set; } = "";
        public string DoctorComment { get; set; } = "";
        public string Status { get; set; } = "";
    }

}