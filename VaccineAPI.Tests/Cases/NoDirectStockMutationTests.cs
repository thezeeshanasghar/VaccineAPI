using System.Text.RegularExpressions;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// Architecture guard: outside the inventory service, no production code may write batch quantity,
/// closed status, the BrandAmount counter, the ledger, or create/remove batches or counters, and no
/// raw SQL may touch inventory tables. A new violation fails the build of the test suite.
/// </summary>
public class NoDirectStockMutationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VaccineAPI.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // file (relative, forward slashes) -> reason it may contain a pattern
    private static readonly string[] Allowed =
    {
        "Services/InventoryTransactionService.cs",   // THE writer
        "Services/BrandAmountProvisioner.cs",        // the only BrandAmount creator
    };

    private static readonly (string name, Regex rx)[] Forbidden =
    {
        ("batch quantity write",        new Regex(@"\b(?:stock|src|batch|dest\w*|source\w*|row|existing\w*)\w*\.Quantity\s*(?:\+=|-=|\+\+|--|=(?!=))", RegexOptions.IgnoreCase)),
        ("OriginalQuantity write",      new Regex(@"\.OriginalQuantity\s*(?:\+=|-=|\+\+|--|=(?!=))")),
        ("IsClosed write",              new Regex(@"\.IsClosed\s*=(?!=)")),
        ("BrandAmount counter write",   new Regex(@"\b(?:ba|brandAmount\w*|dbBrandInventory\w*|brandInventory\w*|sourceBa|destBa|counter|c)\.Quantity\s*(?:\+=|-=|\+\+|--|=(?!=))")),
        ("ledger insert",               new Regex(@"InventoryTransactions\.(?:Add|AddRange|Remove|RemoveRange|Update)\b")),
        ("batch create/remove",         new Regex(@"\bStocks\.(?:Add|AddRange|Remove|RemoveRange)\b")),
        ("BrandAmount create/remove",   new Regex(@"\bBrandAmounts\.(?:Add|AddRange|Remove|RemoveRange)\b")),
        ("raw SQL on inventory tables", new Regex(@"ExecuteSql(?:Raw|Interpolated)?(?:Async)?\s*\([^)]*(?:inventorytransactions|stocks|brandamounts|unbatcheduses)", RegexOptions.IgnoreCase)),
        ("ledger row edit",             new Regex(@"\.ReconciledByTransactionId\s*=(?!=)")),
    };

    // Deletes that intentionally remove BrandAmount/Stock ROWS together with their owner and are
    // protected by InventoryDeleteGuard (stock must be zero before they run).
    private static readonly HashSet<string> GuardedRowRemovals = new()
    {
        "Controllers/ClinicController.cs",   // BrandAmounts.RemoveRange of an emptied clinic
        "Controllers/BrandController.cs",    // BrandAmounts.RemoveRange of an emptied brand
    };

    [Fact]
    public void OnlyTheInventoryServiceMutatesInventoryState()
    {
        var root = RepoRoot();
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith("VaccineAPI.Tests/")
                || rel.StartsWith("Migrations/") || rel.StartsWith("Models/")) continue;
            if (Allowed.Contains(rel)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//")) continue;
                foreach (var (name, rx) in Forbidden)
                {
                    if (!rx.IsMatch(line)) continue;
                    if (name == "BrandAmount create/remove" && GuardedRowRemovals.Contains(rel) && line.Contains("RemoveRange")) continue;
                    violations.Add($"{rel}:{i + 1}  [{name}]  {trimmed}");
                }
            }
        }
        Assert.True(violations.Count == 0,
            "Inventory state is written outside the inventory service:\n  " + string.Join("\n  ", violations));
    }
}
