using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{
    public class BrandAmount
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public int Count { get; set; }

        // Optimistic concurrency token — see Stock.RowVersion for the reasoning (no native
        // rowversion type on MySQL, so this is an app-incremented counter).
        [ConcurrencyCheck]
        public int RowVersion { get; set; }

        // public string SupName { get; set; } // Supplier Name
        public decimal PurchasedAmt { get; set; } // Purchased Vaccine Amount
        // public bool IsPaid { get; set; } // Payment Status

        public long BrandId { get; set; }
        [JsonIgnore]
        public virtual Brand Brand { get; set; } = null!;

        public long DoctorId { get; set; }
        [JsonIgnore]
        public virtual Doctor Doctor { get; set; } = null!;
        public long ClinicId { get; set; }
        [JsonIgnore]
        public virtual Clinic Clinic { get; set; } = null!;
    } 
}
