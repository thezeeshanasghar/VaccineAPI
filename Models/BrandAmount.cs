using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace VaccineAPI.Models
{
    public class BrandAmount
    {
        public long Id { get; set; }
        // Sale price per unit (what a patient is charged for one dose of this brand).
        public decimal SalePrice { get; set; }
        // Live quantity on hand for this brand at this clinic — the running counter every
        // give/ungive/purchase/adjust/transfer/sale keeps in lockstep with the physical Stock
        // rows. See InventoryTransactionService — "the only code allowed to mutate Stock.Quantity/
        // OriginalQuantity or BrandAmount.Quantity."
        public int Quantity { get; set; }

        // v2: set true when a give drove stock to/below 0 (a physically-given dose is always
        // recordable — §6.3). Signals the owner to physically recount and post an AdjustIncrease.
        public bool NeedsReconcile { get; set; }

        // Optimistic concurrency token — see Stock.RowVersion for the reasoning (no native
        // rowversion type on MySQL, so this is an app-incremented counter).
        [ConcurrencyCheck]
        public int RowVersion { get; set; }

        // public string SupName { get; set; } // Supplier Name
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
