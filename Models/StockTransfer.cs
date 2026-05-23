using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class StockTransfer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long DoctorId { get; set; }

        public long FromClinicId { get; set; }

        public long ToClinicId { get; set; }

        public long BrandId { get; set; }

        public string BatchLot { get; set; } = "";

        public DateTime? ExpiryDate { get; set; }

        public int Quantity { get; set; }

        public string Reason { get; set; } = "";

        public DateTime TransferDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("BrandId")]
        public virtual Brand Brand { get; set; } = null!;

        [ForeignKey("FromClinicId")]
        public virtual Clinic FromClinic { get; set; } = null!;

        [ForeignKey("ToClinicId")]
        public virtual Clinic ToClinic { get; set; } = null!;
    }
}
