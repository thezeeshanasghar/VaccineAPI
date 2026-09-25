using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VaccineAPI.ModelDTO
{
    public class ScheduleBrandDTO
    {
        public int ScheduleId { get; set; }
        public long? BrandId { get; set; }
        public string? Manufacturer { get; set; }
        // Nurse-chosen site for this dose in a bulk give (per-dose so multi-dose visits record
        // which vaccine went where — R vs L thigh). Route is re-derived server-side from BrandId.
        public string? Site { get; set; }
        public string? Lot { get; set; }
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
        // Disease entry (Chicken Pox / Hepatitis A "had the disease, not the vaccine"), per row —
        // a bulk give can mix a disease row with real injected doses in the same batch (one shared
        // GivenDate applies to the real doses only; a disease row is excluded from it). See
        // UpdateBulkInjection's per-row IsDisease handling.
        public bool? IsDisease { get; set; }
        public string? DiseaseYear { get; set; }
    }
}