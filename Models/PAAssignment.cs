using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("paassignments")]
    public class PAAssignment
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long? ClinicId { get; set; }
        public long PersonalAssistantId { get; set; }
        public long ChildId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public bool IsCompleted { get; set; } = false;
        public DateTime? CompletedAt { get; set; }
        public string? Notes { get; set; }
        public bool IsCancelled { get; set; } = false;
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }
        public long? ReassignedFromAssignmentId { get; set; }
        public bool IsAutoCreated { get; set; } = false;

        // When this task should actually be done — distinct from AssignedAt (when the
        // doctor handed it to the PA). Auto-filled from Booking.PreferredDate when
        // assigning off a booking; editable by the doctor either way. Null means no
        // target date was given (manual assignment, doctor skipped it).
        public DateTime? TargetDate { get; set; }

        // The Booking this assignment originated from, if any — replaces the old
        // "BookingId:{id}" string convention that used to live inside Notes.
        public long? BookingId { get; set; }

        // "Active" | "PendingHandover" | "PendingCancellation" — the only values ever written
        // (PAAssignmentController). Completion is NOT tracked here: it uses the separate
        // IsCompleted/CompletedAt fields below instead, so don't add a "Completed" string value
        // without migrating those over — two overlapping ways to represent "done" on one row
        // is exactly the kind of landmine a future query against this field would hit.
        public string AssignmentStatus { get; set; } = "Active";
        public DateTime? HandoverDoneAt { get; set; }
        public long? InvoiceSubmissionId { get; set; }

        // Set when the doctor confirms they've physically received the cash for this
        // assignment's invoice (ScheduleController.ConfirmInvoice). Distinct from
        // IsCompleted, which the PA sets when they finish the clinical/dosing work —
        // an assignment can be IsCompleted long before the doctor gets around to
        // confirming the cash handover.
        public bool IsCashConfirmedByDoctor { get; set; } = false;
        public DateTime? CashConfirmedAt { get; set; }

        // PA's cancellation request — set on RequestCancel, cleared/finalized on doctor approve/reject
        public DateTime? CancelRequestedAt { get; set; }
        public string? CancelRequestReason { get; set; }
        public string? RejectionNote { get; set; }
    }
}
