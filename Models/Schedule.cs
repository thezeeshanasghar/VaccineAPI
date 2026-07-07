using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;

namespace VaccineAPI.Models
{
    public class Schedule
    {
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public float? Weight { get; set; }
        public float? Height { get; set; }
        public float? Circle { get; set; }
        public bool IsPAApprove{get; set;}
        public bool IsDone { get; set; }
        public bool? IsSkip { get; set; }
        public bool? IsDisease { get; set; }
        public bool Due2EPI { get; set; }
        public string DiseaseYear { get; set; } = "";
        public DateTime? GivenDate { get; set; }
        public DateTime? DoneAt { get; set; }
        public string PaymentMode { get; set; } = "Cash";
        public string? OnlineService { get; set; }
        public bool IsPaymentApproved { get; set; } = false;
        public long? BrandId { get; set; }
        public virtual Brand Brand { get; set; } = null!;
        public decimal? Amount { get; set; }
        public decimal? VaccineCost { get; set; }
        public string Manufacturer { get; set; } = "";
        // Route of administration (IM/SC/ID/Oral/Intranasal), copied from Brand.Route at give-time.
        // Part of the permanent CERTIFICATE snapshot alongside Manufacturer/Lot/Expiry — fixed
        // brand property, never a per-patient choice, no override at any role. Empty for OHF /
        // no-brand gives and for doses given before this column existed.
        public string Route { get; set; } = "";
        // Site of administration actually used (e.g. "R Thigh"/"L Deltoid"/"Oral"). Per-patient,
        // nurse-chosen at give-time (defaulted from Brand.SiteDefault / age, editable). Snapshotted
        // like Route. Nullable — empty for no-site gives and for doses given before this existed.
        public string? Site { get; set; }
        public string Lot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
        public long? GivenByPaId { get; set; }
        public long? SkippedByPaId { get; set; }
        public DateTime? SkippedAt { get; set; }
        public long? PaymentCollectorPaId { get; set; }
        public bool IsPaymentCollected { get; set; } = false;
        public DateTime? PaymentApprovedAt { get; set; }
        public long? PaymentApprovedByDoctorId { get; set; }
        public int GiveCount { get; set; }
        public int UngiveCount { get; set; }
        public int SkipCount { get; set; }
        public int UnskipCount { get; set; }
        public long ChildId { get; set; }
        public virtual Child Child { get; set; } = null!;
        public long DoseId { get; set; }
        public virtual Dose Dose { get; set; } = null!;
        public long? StockClinicId { get; set; }

        // v2: the exact Stock batch consumed at give-time. Enables precise ungive (restore the
        // same batch) and batch-level reconciliation. Null for OHF / non-consuming / historical
        // gives, and for doses given before this column existed.
        // NOTE: distinct from Lot/Manufacturer/Expiry above — those are the permanent CERTIFICATE
        // snapshot (never read from stock). StockId is an internal accounting link only.
        public int? StockId { get; set; }
        // public virtual DateTime FromDate { get; set; }
        // public virtual DateTime ToDate { get; set; }
    }
}