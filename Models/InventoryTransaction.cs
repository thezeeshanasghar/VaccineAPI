using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public enum InventoryTransactionType
    {
        Purchase,
        BillEdit,
        BillReverse,
        SplitConsumed,
        AdjustIncrease,
        AdjustLoss,
        AdjustReverse,
        TransferOut,
        TransferIn,
        TransferReverse,
        DirectSale,
        DirectSaleReverse,
        Administer,
        Unadminister,
        MigrationBackfill,
        MigrationCorrection,
        // v2 (stock overhaul). Appended at the end so existing integer positions 0..15
        // are preserved — historical ledger rows are keyed by position; never reorder.
        OpeningBalance,   // = 16
        TwinCorrection,   // = 17
        // §6.3a: label-only correction of a given dose's batch/expiry/manufacturer.
        // Moves NO stock (QuantityDelta = 0, ConsumesStock = false). BatchLot/Expiry hold
        // the NEW values; the previous values are recoverable from the dose's prior
        // Administer row on the same SourceId (= ScheduleId). Pure audit trail.
        BatchCorrection   // = 18
    }

    // Why an Administer/Unadminister row did or did not move stock (§6.2a deduction model).
    public static class InventoryDecisionReason
    {
        public const string Normal        = "NORMAL";         // brand give, date = today → deducts
        public const string Ohf           = "OHF";            // no brand / external stock → no deduct
        public const string PrePeriod     = "PRE_PERIOD";     // dated before StockPeriodStart → no deduct
        public const string Historical    = "HISTORICAL";     // re-recording a past dose → no deduct
        public const string LateRecording = "LATE_RECORDING"; // home visit recorded late → deducts
    }

    // Append-only ledger row. One row per stock movement, ever — never updated or deleted
    // by application code. A reversal writes a new offsetting row referencing the same
    // SourceId; it never edits or removes the original row.
    public class InventoryTransaction
    {
        public long Id { get; set; }

        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public long BrandId { get; set; }

        // Null only for brand-level Increase adjustments with no batch row (matches
        // AdjustStockController's existing Increase behavior of not creating a Stock row).
        public int? StockId { get; set; }

        public string? BatchLot { get; set; }
        public DateTime? Expiry { get; set; }

        // Signed: +100 purchase, -20 sale, -1 give, +1 ungive, -30 transfer-out, +30 transfer-in.
        public int QuantityDelta { get; set; }

        // AWT-inclusive unit cost at time of movement, for purchase-cost history. Null for
        // movements with no cost basis (e.g. Administer/Unadminister).
        [Column(TypeName = "decimal(18,4)")]
        public decimal? UnitCost { get; set; }

        public InventoryTransactionType SourceType { get; set; }

        // BillId, AdjustStockId, StockTransferId, DirectSaleId, or ScheduleId — whichever
        // document caused this row.
        public long SourceId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // The business/logical date of the event (bill date, adjustment date, give date, etc.)
        // in local clinic time — always set from the source record's own date field, never from
        // UtcNow. Reports filter on EventDate so timezone offsets in CreatedAt don't skew numbers.
        public DateTime EventDate { get; set; } = DateTime.UtcNow.Date;

        public long? CreatedByPaId { get; set; }

        // v2: for Administer/Unadminister rows, whether this movement actually touched stock.
        // true  = stock was deducted/restored (QuantityDelta != 0).
        // false = recorded clinical fact only, no stock change (OHF / pre-period / historical).
        // Defaults true so all existing (purchase/adjust/transfer/sale) rows are unaffected.
        public bool ConsumesStock { get; set; } = true;

        // v2: why this give did/didn't move stock — one of InventoryDecisionReason.* for
        // give/ungive rows; null for all other source types.
        public string? DecisionReason { get; set; }

        // v2: set on an unbatched Administer row (StockId=null, ConsumesStock=true) when a
        // later purchase's backlog-clearing prompt claims credit for it. Null while
        // outstanding. Cleared back to null if the give is ever ungiven, so the purchase
        // that claimed it stops overclaiming a dose that no longer exists as given.
        public long? ReconciledByTransactionId { get; set; }
    }
}
