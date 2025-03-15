using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("adjuststocks")]
    public class AdjustStock
    {
        // [Key]
        // [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        // [Required]
        public long BrandId { get; set; }

        // [Required]
        // [Range(-10000, 10000)]
        public int Adjustment { get; set; }

        // [Required]
        // [StringLength(200)]
        public string Reason { get; set; }
               
        public DateTime Date { get; set; }

        // Navigation property
        // [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; }
    }
}