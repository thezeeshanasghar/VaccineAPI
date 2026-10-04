using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // A PA's "patient refused at home" request. Status: "Pending" | "Approved" | "Rejected".
    // Approval is only ever written by PAAssignmentController.DeleteAssignment(mode=FullReset),
    // in the same transaction as the reversal, so an Approved row always means the visit was
    // actually reset. The row outlives the (deleted) PAAssignment and drives the doctor's
    // golden "refused at home" marker.
    [Table("vaccinerefusals")]
    public class VaccineRefusal
    {
        public long Id { get; set; }
        public long AssignmentId { get; set; }
        public long ChildId { get; set; }
        public long DoctorId { get; set; }
        public long PaId { get; set; }
        public string? Reason { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string? RejectionNote { get; set; }
    }
}
