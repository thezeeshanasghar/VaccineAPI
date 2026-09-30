using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Tests.Infrastructure;

/// <summary>
/// Executable statement of the non-negotiable invariants. Returns human-readable violations
/// (empty list = clean). Used after every scenario step in every test.
/// </summary>
public static class InventoryInvariants
{
    /// INV1: per batch, Stock.Quantity == sum of ledger movements booked to that batch.
    /// INV2: per (brand, clinic), BrandAmount.Quantity == sum of Stock.Quantity of that clinic's batches.
    /// INV3: no negative quantities; a batch with Quantity>0 is never IsClosed.
    /// INV4: per (brand, clinic), sum of ALL ledger movements == sum of Stock.Quantity
    ///       (nothing may exist only in the ledger, e.g. an unbatched -1).
    public static List<string> Check(Context db)
    {
        var v = new List<string>();
        var stocks = db.Stocks.AsNoTracking().Include(s => s.Bill).ToList();
        var ledger = db.InventoryTransactions.AsNoTracking().ToList();
        var bas = db.BrandAmounts.AsNoTracking().ToList();

        foreach (var s in stocks)
        {
            var sum = ledger.Where(l => l.StockId == s.Id).Sum(l => l.QuantityDelta);
            if (sum != s.Quantity)
                v.Add($"INV1 batch {s.Id} ({s.BatchLot}): Stock.Quantity={s.Quantity} but ledger for this batch sums to {sum}");
            if (s.Quantity < 0) v.Add($"INV3 batch {s.Id}: negative quantity {s.Quantity}");
            // INV5: units on a batch never exceed what was purchased, and purchased is never negative
            // (otherwise "consumed = purchased - on hand" and every bill payable would be wrong).
            if (s.OriginalQuantity < s.Quantity) v.Add($"INV5 batch {s.Id}: Quantity {s.Quantity} exceeds OriginalQuantity {s.OriginalQuantity}");
            if (s.OriginalQuantity < 0) v.Add($"INV5 batch {s.Id}: negative OriginalQuantity {s.OriginalQuantity}");
            if (s.Quantity > 0 && s.IsClosed) v.Add($"INV3 batch {s.Id}: Quantity={s.Quantity} but IsClosed=true (unusable by FEFO)");
        }

        foreach (var ba in bas)
        {
            long ClinicOf(Stock s) => s.ClinicId ?? s.Bill?.ClinicId ?? 0;
            var stockSum = stocks.Where(s => s.BrandId == ba.BrandId && ClinicOf(s) == ba.ClinicId).Sum(s => s.Quantity);
            if (ba.Quantity != stockSum)
                v.Add($"INV2 brand {ba.BrandId} clinic {ba.ClinicId}: BrandAmount={ba.Quantity} but batches sum to {stockSum}");
            var ledgerSum = ledger.Where(l => l.BrandId == ba.BrandId && l.ClinicId == ba.ClinicId).Sum(l => l.QuantityDelta);
            if (ledgerSum != stockSum)
                v.Add($"INV4 brand {ba.BrandId} clinic {ba.ClinicId}: ledger sums to {ledgerSum} but batches sum to {stockSum}");
            if (ba.Quantity < 0) v.Add($"INV3 BrandAmount {ba.Id}: negative {ba.Quantity}");
        }
        return v;
    }

    public static void AssertClean(Context db, string when)
    {
        var violations = Check(db);
        Xunit.Assert.True(violations.Count == 0,
            $"Inventory invariants violated {when}:\n  - " + string.Join("\n  - ", violations));
    }
}
