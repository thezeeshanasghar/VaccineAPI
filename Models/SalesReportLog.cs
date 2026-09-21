using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // One row per Sales & Collection PDF actually downloaded (written by
    // StockController.GetSalesCollectionReport right before it streams the file).
    // Re-download from the Recent Reports list re-runs that same endpoint with the
    // stored ClinicId/DoctorId/FromDate/ToDate — the PDF is regenerated from live
    // data, never stored as bytes, so a re-download always reflects current records.
    [Table("salesreportlogs")]
    public class SalesReportLog
    {
        public long Id { get; set; }
        public long ClinicId { get; set; }
        public long DoctorId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public int TotalPatients { get; set; }
        public decimal TotalVaxFee { get; set; }
        public decimal TotalItemsPrice { get; set; }
        public decimal GrandTotal { get; set; }

        [ForeignKey("ClinicId")]
        public Clinic Clinic { get; set; } = null!;
    }
}
