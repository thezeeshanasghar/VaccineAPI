using System;

namespace VaccineAPI.ModelDTO
{
    public class AdjustStockCreateDTO
    {
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public long BrandId { get; set; }
        public int Quantity { get; set; }
        public string Type { get; set; } = "";      // "Increase" | "Loss"
        public string Reason { get; set; } = "";
        public decimal Price { get; set; }           // mandatory for Increase, 0 for Loss
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public DateTime Date { get; set; }

        // v2: purchase-time unbatched-backlog prompt. Only consulted for Type=="Increase":
        //   null  = not answered — backend rejects with the outstanding count if a backlog
        //           exists, frontend must show "clear backlog" / "skip" and resubmit.
        //   true  = "clear backlog" — outstanding unbatched Administer rows for this brand
        //           are marked reconciled against this purchase (see ReconciledByTransactionId).
        //   false = "skip" — purchase proceeds normally, backlog stays open.
        public bool? ClearUnbatchedBacklog { get; set; }
    }

    public class AdjustStockListDTO
    {
        public long Id { get; set; }
        public string BrandName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public int Adjustment { get; set; }          // raw signed value (+increase / -loss)
        public string Type { get; set; } = "";       // "Increase" | "Loss"
        public string Reason { get; set; } = "";
        public decimal Price { get; set; }
        public string BatchLot { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public DateTime Date { get; set; }
        public string ClinicName { get; set; } = "";
    }
}
