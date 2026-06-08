using System;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{

    public class Booking
    {
        public long Id { get; set; }

        public long ChildId { get; set; }
        [JsonIgnore]
        public virtual Child Child { get; set; } = null!;

        public long ParentUserId { get; set; }
        public long ClinicId { get; set; }
        [JsonIgnore]
        public virtual Clinic Clinic { get; set; } = null!;
        public long DoctorId { get; set; }

        public string ChildName { get; set; } = "";
        public string FatherName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public DateTime DOB { get; set; }
        public string Vaccines { get; set; } = "";
        public string Type { get; set; } = "ClinicBooked";
        public string Status { get; set; } = "Pending";
        public string Address { get; set; } = "";
        public string Location { get; set; } = "";
        public string City { get; set; } = "";
        public string Card { get; set; } = "";
        public DateTime? PreferredDate { get; set; }
        public string Comments { get; set; } = "";
        public string DoctorComment { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public long? UpdatedByDoctorId { get; set; }
    }

}
