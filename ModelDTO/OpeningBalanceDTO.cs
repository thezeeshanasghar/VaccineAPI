using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    // §Opening Balance: physical on-hand at a stock reset. Each line becomes a real Stock batch
    // and an OpeningBalance ledger row dated at the clinic's StockPeriodStart.
    public class OpeningBalanceDTO
    {
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public List<OpeningBalanceLine> Lines { get; set; } = new List<OpeningBalanceLine>();
    }

    public class OpeningBalanceLine
    {
        public long BrandId { get; set; }
        public int Quantity { get; set; }
        public decimal? UnitCost { get; set; }
        public string? BatchLot { get; set; }
        public DateTime? Expiry { get; set; }
    }
}
