using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // [Table("stock")]
    public class Stock
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public long BrandId { get; set; }

        [Required]
        // v2: 0 is a valid live quantity — an emptied batch stays at 0 (IsClosed=1),
        // it is never hard-deleted. (Was Range(1,..), which rejected 0.)
        [Range(0, int.MaxValue)]
        public int Quantity { get; set; }

        public int OriginalQuantity { get; set; }

        // v2: the clinic that physically holds this batch. Authoritative for FEFO and
        // Stock Overview. Set on every insert (Purchase/TransferIn/AdjustIncrease/OpeningBalance).
        // Nullable for historical rows created before this column existed.
        public long? ClinicId { get; set; }

        // v2: true = zero-quantity batch, hidden from pickers/FEFO but kept for history.
        // A batch is closed, never hard-deleted.
        public bool IsClosed { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal StockAmount { get; set; }

        public int? BillId { get; set; }

        public string? BatchLot { get; set; }

        public DateTime? Expiry { get; set; }

        // Optimistic concurrency token. MySQL has no native rowversion type, so this is a
        // plain counter the app increments on every write; EF's [ConcurrencyCheck] makes any
        // UPDATE include "WHERE RowVersion = <old value>", so a concurrent writer that read
        // the same row first will get 0 rows affected and throw DbUpdateConcurrencyException
        // instead of silently overwriting the other writer's change.
        [ConcurrencyCheck]
        public int RowVersion { get; set; }

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;

        [ForeignKey("BillId")]
        public virtual Bill? Bill { get; set; }
    }
}
