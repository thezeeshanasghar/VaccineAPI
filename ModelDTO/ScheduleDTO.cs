using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Web;

namespace VaccineAPI.ModelDTO
{

    public class ScheduleDTO
    {
        public long Id { get; set; }
        public long ChildId { get; set; }
        public int DoseId { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime Date { get; set; }
        public float? Weight { get; set; }
        public float? Height { get; set; }
        public float? Circle { get; set; }
        public bool IsPAApprove { get; set; }
        public bool IsDone { get; set; }
        public bool Due2EPI { get; set; }
        public bool? IsSkip { get; set; }
        public bool? IsDisease { get; set; }
        public string DiseaseYear { get; set; } = "";
        public DoseDTO Dose { get; set; } = null!;
        public virtual ChildDTO Child { get; set; } = null!;
        public List<BrandDTO> Brands { get; set; } = new List<BrandDTO>();
        public BrandDTO Brand { get; set; } = null!;
        public long? BrandId { get; set; }
        public decimal? Amount { get; set; }
        public string Manufacturer { get; set; } = "";
        public string Lot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
        public List<ScheduleBrandDTO> ScheduleBrands { get; set; } = new List<ScheduleBrandDTO>();
        public long DoctorId { get; set; }
        public long? PaId { get; set; }
        public long? GivenByPaId { get; set; }
        public string GivenByPaName { get; set; } = "";
        public long? SkippedByPaId { get; set; }
        public long? PaymentCollectorPaId { get; set; }
        public string PaymentCollectorPaName { get; set; } = "";
        public bool IsPaymentCollected { get; set; }
        public int GiveCount { get; set; }
        public int UngiveCount { get; set; }
        public int SkipCount { get; set; }
        public int UnskipCount { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime GivenDate { get; set; }

        // v2 deduction-decision (§6.2a). Only consulted for a backdated, in-period, brand give —
        // the one ambiguous case where the frontend must have shown the prompt:
        //   null  = not answered (frontend must prompt first; backend rejects if it reaches a
        //           give that needs it — belt-and-suspenders so a bad client can't silently deduct)
        //   false = "from our stock"  → deduct (reason LATE_RECORDING)
        //   true  = "just recording"  → no deduct (reason HISTORICAL)
        // Ignored for OHF / today / pre-period gives (auto-decided, never prompt).
        public bool? ReRecordHistorical { get; set; }
        public DateTime? DoneAt { get; set; }
        public string PaymentMode { get; set; } = "Cash";
        public string? OnlineService { get; set; }
        public bool IsPaymentApproved { get; set; } = false;
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime FromDate { get; set; }
        [JsonConverter(typeof(OnlyDateConverter))]
        public DateTime ToDate { get; set; }
        // FOR INVOICE
        [JsonConverter(typeof(OnlyDateConverter))]
        public System.DateTime? InvoiceDate { get; set; }
        public List<ClinicDTO> Clinics { get; set; } = new List<ClinicDTO>();
        public bool IgnoreMinAgeAtGiveTime { get; set; } = false;
        public bool IgnoreMinGapAtGiveTime { get; set; } = false;
        public bool IgnoreMaxAgeAtGiveTime { get; set; } = false;
    }

}