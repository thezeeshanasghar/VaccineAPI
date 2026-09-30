using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Tests.Infrastructure;

/// <summary>
/// One SQLite in-memory database per test, using the production Context (same model, same
/// SaveChanges RowVersion bump). SQLite is not MySQL, so this proves logic and invariants,
/// not MySQL-specific behaviour (locking modes, collations).
/// </summary>
public sealed class TestDb : IDisposable
{
    static TestDb() { VaccineAPI.Services.InventoryTransactionService.StrictInvariants = true; VaccineAPI.Models.Context.EnforceSingleInventoryWriter = true; }

    private readonly SqliteConnection _conn;
    public DbContextOptions<Context> Options { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Options = new DbContextOptionsBuilder<Context>().UseSqlite(_conn).Options;
        using var db = new Context(Options);
        db.Database.EnsureCreated();
    }

    /// <summary>A fresh Context (fresh change tracker) on the same database.</summary>
    public Context NewContext() => new Context(Options);

    public void Dispose() => _conn.Dispose();
}
