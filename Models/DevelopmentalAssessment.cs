using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class DevelopmentalAssessment
    {
        public long Id { get; set; }
        public long ChildId { get; set; }
        [ForeignKey("ChildId")]
        public Child Child { get; set; } = null!;

        public DateTime VisitDate { get; set; }
        public string AgeBracket { get; set; } = "";   // e.g. "6m", "12m", "2y"
        public int AgeInMonths { get; set; }

        // Each flag: "normal" | "flag" | "na"
        public string Q1 { get; set; } = "na";
        public string Q2 { get; set; } = "na";
        public string Q3 { get; set; } = "na";
        public string Q4 { get; set; } = "na";
        public string Q5 { get; set; } = "na";
        public string Q6 { get; set; } = "na";
        public string Q7 { get; set; } = "na";
        public string Q8 { get; set; } = "na";
        public string Q9 { get; set; } = "na";
        public string Q10 { get; set; } = "na";

        public string Notes { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public long? PaId { get; set; }
        public long? DoctorId { get; set; }
    }
}
