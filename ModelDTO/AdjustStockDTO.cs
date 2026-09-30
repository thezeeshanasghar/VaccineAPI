using System;

namespace VaccineAPI.ModelDTO
{
    public class AdjustStockCreateDTO
    {
        // Optional. Send the same value when retrying: a repeated request is answered from the first
        // result and never posts twice.
        public string? ClientRequestId { get; set; }

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

        // Explicit list of pending doses (UnbatchedUse ids) to allocate to the batch this
        // Increase creates. Takes precedence over ClearUnbatchedBacklog=true (which means "all").
        public List<long>? ClaimUnbatchedUseIds { get; set; }

        // Caller identity for StockActionGuard — null/null means a Doctor caller. See
        // project_give_ungive_permission_enforcement for why CallerUserId/SecurityStamp are
        // required whenever PaId/ManagerId is set (a client-supplied id alone proves nothing).
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
    }

    // Write-off of units from ONE batch: "Wastage" (broken, spoiled, cold-chain) or "Expiry".
    public class WriteOffDTO
    {
        public string? ClientRequestId { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public int StockId { get; set; }
        public int Quantity { get; set; }
        public string Kind { get; set; } = "";       // "Wastage" | "Expiry"
        public string Reason { get; set; } = "";
        public DateTime Date { get; set; }
        public long? PaId { get; set; }
        public long? ManagerId { get; set; }
        public long? CallerUserId { get; set; }
        public string? SecurityStamp { get; set; }
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
