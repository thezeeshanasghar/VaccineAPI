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
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        public int OriginalQuantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal StockAmount { get; set; }

        public int? BillId { get; set; }

        public string? BatchLot { get; set; }

        public DateTime? Expiry { get; set; }

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;

        [ForeignKey("BillId")]
        public virtual Bill? Bill { get; set; }
    }
}
