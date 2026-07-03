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
        MigrationCorrection
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
    }
}
