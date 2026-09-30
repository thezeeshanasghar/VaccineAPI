using System;
using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.Models
{
    public static class UnbatchedUseStatus
    {
        public const byte Pending = 0;    // dose recorded as given, no batch known yet. No stock effect.
        public const byte Claimed = 1;    // allocated to a real batch (batch reduced, ledger row written)
        public const byte Dismissed = 2;  // doctor: not from our stock. Stays on the record, no stock effect.
        public const byte Voided = 3;     // the dose was ungiven / reassigned / deleted
    }

    // A dose that was physically given while no usable batch was on the shelf (the purchase had
    // not been entered yet). It is a clinical fact, NOT a stock movement: it never touches
    // Stock, BrandAmount or the ledger while Pending, so the inventory invariants hold at all
    // times. A formal ClaimUnbatched operation later allocates it to a real batch.
    // ScheduleId deliberately has no foreign key (a deleted schedule must not delete history).
    public class UnbatchedUse
    {
        [Key]
        public long Id { get; set; }

        public long ScheduleId { get; set; }

        // Equals ScheduleId while the use is Pending or Claimed, NULL once Dismissed/Voided.
        // A unique index on this column gives "one live use per schedule" on MySQL, which has
        // no partial indexes.
        public long? ActiveScheduleKey { get; set; }

        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public long BrandId { get; set; }
        public DateTime GivenDate { get; set; }
        public long? GivenByPaId { get; set; }
        public string? DecisionReason { get; set; }

        public byte Status { get; set; } = UnbatchedUseStatus.Pending;

        // Set when Claimed.
        public int? ClaimStockId { get; set; }

        public string? DismissReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }

        [ConcurrencyCheck]
        public int RowVersion { get; set; }
    }
}
