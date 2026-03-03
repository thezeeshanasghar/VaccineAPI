using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.ModelDTO
{

    public class BrandAmountDTO
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public int Count { get; set; }
        // public string SupName { get; set; } // Supplier Name
        public decimal PurchasedAmt { get; set; } // Purchased Vaccine Amount
        // public bool IsPaid { get; set; } // Payment Status
        public long BrandId { get; set; }
        [JsonIgnore]
        public BrandDTO Brand { get; set; } = null!;

        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public ClinicDTO Clinic { get; set; } = null!;
        [JsonIgnore]
        public DoctorDTO Doctor { get; set; } = null!;
        public string VaccineName { get; set; } = "";
        public string BrandName { get; set; } = "";
    }

}