using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // A patient referred by an agent from VacAgent, waiting for clinic staff to register them
    // through the normal Add Patient form. Not a Child: no childs row, parent user or schedule
    // exists until conversion. Plain scalar columns, no navigation properties — agents is a MyISAM
    // table with no FK, and a nav property risks EF inventing a shadow column (see Child.AgentId).
    [Table("agentreferrals")]
    public class AgentReferral
    {
        public const string StatusNew = "New";
        public const string StatusConverted = "Converted";
        public const string StatusRejected = "Rejected";

        public long Id { get; set; }
        public int AgentId { get; set; }
        public long ClinicId { get; set; }
        public string Name { get; set; } = "";
        public string FatherName { get; set; } = "";
        public string Gender { get; set; } = "";
        public DateTime DOB { get; set; }
        public string CountryCode { get; set; } = "";
        public string MobileNumber { get; set; } = "";
        public string? City { get; set; }
        public string? Email { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = StatusNew;
        public long? ChildId { get; set; }
        public string? RejectReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedByRole { get; set; }
        public long? ResolvedById { get; set; }
    }
}
