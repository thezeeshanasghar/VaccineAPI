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
        public string Manufacturer { get; set; } = "";
        public string Lot { get; set; } = "";
        public DateTime? Expiry { get; set; }
        public int? Validity { get; set; }
        public long? GivenByPaId { get; set; }
        public long? SkippedByPaId { get; set; }
        public int GiveCount { get; set; }
        public int UngiveCount { get; set; }
        public int SkipCount { get; set; }
        public int UnskipCount { get; set; }
        public long ChildId { get; set; }
        public virtual Child Child { get; set; } = null!;
        public long DoseId { get; set; }
        public virtual Dose Dose { get; set; } = null!;
        // public virtual DateTime FromDate { get; set; }
        // public virtual DateTime ToDate { get; set; }
    }
}