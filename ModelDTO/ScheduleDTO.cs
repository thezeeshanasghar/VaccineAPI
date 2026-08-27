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
        // Route = server re-derived from BrandId at give-time (client value ignored). Site = the
        // nurse-chosen site sent from the give-UI. See Route/Site module plan.
        public string Route { get; set; } = "";
        public string? Site { get; set; }
        public string Lot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
        public List<ScheduleBrandDTO> ScheduleBrands { get; set; } = new List<ScheduleBrandDTO>();
        public long DoctorId { get; set; }
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? GivenByPaId { get; set; }
        public string GivenByPaName { get; set; } = "";
        public long? GivenByManagerId { get; set; }
        public string GivenByManagerName { get; set; } = "";
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

        // v2 §6.5a: doctor's explicit confirmation to ungive a PRE-RESET historical dose. The
        // first attempt returns a Warning ("history only, no stock"); the client re-submits with
        // this = true to proceed. PAs can never ungive a pre-reset dose regardless of this flag.
        public bool ConfirmPreResetUngive { get; set; }

        // v2: unbatched-give confirmation. Only consulted when the give would actually consume
        // stock (ConsumesStock=true per ResolveGiveDecision) and FEFO finds no batch to fill from:
        //   null  = not answered — backend rejects, frontend must show "add stock now" / "record
        //           as unbatched" and resubmit with this set.
        //   true  = "record as unbatched" — give proceeds, ledger logs it with no batch attached.
        // Ignored entirely for gives that don't consume stock (OHF/pre-period/historical) or that
        // already found a real batch — never a spurious prompt for a give that never needed one.
        public bool? ConfirmUnbatchedGive { get; set; }

        // §6.3a: acting PA when a batch correction is done by a PA (null = doctor did it).
        // Recorded on the audit ledger row's CreatedByPaId. Doctor + PA both permitted.
        public long? CorrectByPaId { get; set; }
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
        // Direct FK to the InvoiceSubmission this dose is billed on — the reliable way to
        // tell "is this dose invoiced" (see Schedule.InvoiceSubmissionId). Null = not invoiced,
        // or invoiced before this column existed (falls back to date-matching client-side).
        public long? InvoiceSubmissionId { get; set; }
        public List<ClinicDTO> Clinics { get; set; } = new List<ClinicDTO>();
        public bool IgnoreMinAgeAtGiveTime { get; set; } = false;
        public bool IgnoreMinGapAtGiveTime { get; set; } = false;
        public bool IgnoreMaxAgeAtGiveTime { get; set; } = false;
        public DateTime? AlertSentAt { get; set; }
    }

}