using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using Xunit;

namespace VaccineAPI.Tests.Infrastructure;

/// <summary>Physical state of the shelf, comparable for "zero change" assertions.</summary>
public sealed record Snap(string Stocks, string BrandAmounts, int Ledger, string Uses, string Schedules, int Bills, int Sales, int Transfers, int Adjusts)
{
    public static Snap Take(Context db)
    {
        var st = string.Join("|", db.Stocks.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => $"{s.Id}:{s.Quantity}/{s.OriginalQuantity}/{s.IsClosed}/{s.BatchLot}/{s.Expiry}/{s.StockAmount}/{s.ClinicId}/{s.RowVersion}").ToList());
        var ba = string.Join("|", db.BrandAmounts.AsNoTracking().OrderBy(b => b.Id)
            .Select(b => $"{b.Id}:{b.Quantity}/{b.NeedsReconcile}/{b.RowVersion}").ToList());
        var uses = string.Join("|", db.UnbatchedUses.AsNoTracking().OrderBy(u => u.Id)
            .Select(u => $"{u.Id}:{u.ScheduleId}/{u.Status}").ToList());
        var sch = string.Join("|", db.Schedules.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => $"{s.Id}:{s.IsDone}/{s.BrandId}/{s.StockId}").ToList());
        return new Snap(st, ba, db.InventoryTransactions.Count(), uses, sch,
            db.Bills.Count(), db.DirectSales.Count(), db.StockTransfers.Count(), db.AdjustStocks.Count());
    }
}

public static class Kit
{
    // ---------------------------------------------------------------- reads
    public static Snap Snapshot(this StockWorld w) { using var db = w.NewContext(); return Snap.Take(db); }

    public static int BaQty(this StockWorld w, long clinic, long? brand = null)
    {
        using var db = w.NewContext();
        return db.BrandAmounts.Single(b => b.BrandId == (brand ?? w.BrandId) && b.ClinicId == clinic).Quantity;
    }

    public static List<Stock> Stocks(this StockWorld w, long? brand = null)
    {
        using var db = w.NewContext();
        return db.Stocks.AsNoTracking().Where(s => s.BrandId == (brand ?? w.BrandId)).OrderBy(s => s.Id).ToList();
    }

    public static int Shelf(this StockWorld w, long clinic, long? brand = null)
    {
        using var db = w.NewContext();
        return db.Stocks.AsNoTracking().Where(s => s.BrandId == (brand ?? w.BrandId) && s.ClinicId == clinic).Sum(s => s.Quantity);
    }

    public static List<InventoryTransaction> Ledger(this StockWorld w, InventoryTransactionType? type = null)
    {
        using var db = w.NewContext();
        return db.InventoryTransactions.AsNoTracking().Where(t => type == null || t.SourceType == type).OrderBy(t => t.Id).ToList();
    }

    public static void Clean(this StockWorld w, string when)
    {
        using var db = w.NewContext();
        InventoryInvariants.AssertClean(db, when);
    }

    // ---------------------------------------------------------------- world extension
    public static long AddBrand(this StockWorld w, string name)
    {
        using var db = w.NewContext();
        db.BypassInventoryWriterCheck = true;   // fixture seeding
        var b = new Brand { Name = name, Manufacturer = "M", Route = "IM" };
        db.Brands.Add(b); db.SaveChanges();
        db.BrandAmounts.AddRange(
            new BrandAmount { BrandId = b.Id, DoctorId = w.DoctorId, ClinicId = w.ClinicA, SalePrice = 50 },
            new BrandAmount { BrandId = b.Id, DoctorId = w.DoctorId, ClinicId = w.ClinicB, SalePrice = 50 });
        db.SaveChanges();
        return b.Id;
    }

    /// <summary>New child with one schedule row of Dose 1 (today). Returns (childId, scheduleId).</summary>
    public static (long child, long schedule) AddChild(this StockWorld w)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "31" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var c = new Child { Name = "K" + u.Id, ClinicId = w.ClinicA, UserId = u.Id, DOB = StockWorld.Today.AddYears(-2) };
        db.Childs.Add(c); db.SaveChanges();
        var s = new Schedule { ChildId = c.Id, DoseId = w.Dose1Id, Date = StockWorld.Today };
        db.Schedules.Add(s); db.SaveChanges();
        return (c.Id, s.Id);
    }

    /// <summary>A separate one-dose vaccine scheduled today for the child (for bulk gives).</summary>
    public static (long dose, long schedule) AddOneDoseVaccine(this StockWorld w, long childId, string name)
    {
        using var db = w.NewContext();
        var v = new Vaccine { Name = name, Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var d = new Dose { Name = name + " D1", VaccineId = v.Id, DoseOrder = 1, MinAge = 0 };
        db.Doses.Add(d); db.SaveChanges();
        var s = new Schedule { ChildId = childId, DoseId = d.Id, Date = StockWorld.Today };
        db.Schedules.Add(s); db.SaveChanges();
        return (d.Id, s.Id);
    }

    // ---------------------------------------------------------------- bills
    public static BillLineDTO Line(long brand, string lot, int qty, decimal price, int? stockId = null, DateTime? expiry = null)
        => new BillLineDTO { BrandId = brand, BatchLot = lot, Expiry = expiry ?? StockWorld.Expiry, Quantity = qty, UnitPrice = price, StockId = stockId };

    public static (Result.Outcome o, int billId) CreateBill(this StockWorld w, long clinic, decimal awt, params BillLineDTO[] lines)
    {
        using var db = w.NewContext();
        var dto = new BillCreateDTO { BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = clinic, SupplierName = "Sup",
            AwtPercent = awt, BillNo = "B-" + Guid.NewGuid().ToString("N")[..6] };
        dto.Lines.AddRange(lines);
        var r = w.Bills(db).Create(dto).GetAwaiter().GetResult();
        var o = Result.Of(r);
        return (o, o.IsSuccess ? (int)Result.Prop(r, "ResponseData", "Id")! : 0);
    }

    public static Result.Outcome EditBill(this StockWorld w, int billId, long clinic, decimal awt, params BillLineDTO[] lines)
    {
        using var db = w.NewContext();
        var dto = new BillUpdateDTO { BillNo = "E", BillDate = StockWorld.Today, DoctorId = w.DoctorId, ClinicId = clinic, SupplierName = "Sup", AwtPercent = awt };
        dto.Lines.AddRange(lines);
        return Result.Of(w.Bills(db).Update(billId, dto).GetAwaiter().GetResult());
    }

    public static Result.Outcome ReverseBill(this StockWorld w, int billId, bool force = false)
    {
        using var db = w.NewContext();
        return Result.Of(w.Bills(db).Reverse(billId, force).GetAwaiter().GetResult());
    }

    // ---------------------------------------------------------------- direct sales
    public static DirectSaleItemDTO SaleItem(long brand, string lot, int qty) =>
        new DirectSaleItemDTO { BrandId = brand, BatchLot = lot, ExpiryDate = StockWorld.Expiry, Quantity = qty, SalePricePerUnit = 20 };

    public static DirectSaleItemDTO SaleItemX(long brand, string lot, int qty, DateTime? expiry) =>
        new DirectSaleItemDTO { BrandId = brand, BatchLot = lot, ExpiryDate = expiry, Quantity = qty, SalePricePerUnit = 20 };

    public static Result.Outcome Sell(this StockWorld w, long clinic, params DirectSaleItemDTO[] items)
    {
        using var db = w.NewContext();
        var dto = new DirectSaleCreateDTO { DoctorId = w.DoctorId, ClinicId = clinic, ClientName = "C", SaleDate = StockWorld.Today };
        dto.Items.AddRange(items);
        return Result.Of(w.Sales(db).Create(dto).GetAwaiter().GetResult());
    }

    public static Result.Outcome DeleteSale(this StockWorld w, long saleId)
    {
        using var db = w.NewContext();
        return Result.Of(w.Sales(db).Delete(saleId).GetAwaiter().GetResult());
    }

    public static List<long> SaleIds(this StockWorld w) { using var db = w.NewContext(); return db.DirectSales.OrderBy(s => s.Id).Select(s => s.Id).ToList(); }

    // ---------------------------------------------------------------- transfers
    public static StockTransferItemDTO XItem(long brand, string lot, int qty) =>
        new StockTransferItemDTO { BrandId = brand, BatchLot = lot, ExpiryDate = StockWorld.Expiry, Quantity = qty, UnitPrice = 10 };

    public static Result.Outcome Transfer(this StockWorld w, long from, long to, params StockTransferItemDTO[] items)
    {
        using var db = w.NewContext();
        var dto = new StockTransferCreateDTO { DoctorId = w.DoctorId, FromClinicId = from, ToClinicId = to, TransferDate = StockWorld.Today, Reason = "t" };
        dto.Items.AddRange(items);
        return Result.Of(w.Transfers(db).Create(dto).GetAwaiter().GetResult());
    }

    public static Result.Outcome DeleteTransfer(this StockWorld w, long id)
    {
        using var db = w.NewContext();
        return Result.Of(w.Transfers(db).Delete(id).GetAwaiter().GetResult());
    }

    public static List<long> TransferIds(this StockWorld w) { using var db = w.NewContext(); return db.StockTransfers.OrderBy(s => s.Id).Select(s => s.Id).ToList(); }

    // ---------------------------------------------------------------- adjustments
    public static Result.Outcome Adjust(this StockWorld w, long clinic, string type, int qty, string lot, decimal price = 10, long? brand = null, DateTime? expiry = null, bool undated = false)
    {
        using var db = w.NewContext();
        return Result.Of(w.Adjusts(db).Create(new AdjustStockCreateDTO
        {
            DoctorId = w.DoctorId, ClinicId = clinic, BrandId = brand ?? w.BrandId, Quantity = qty, Type = type, Reason = "r",
            Price = type == "Increase" ? price : 0, BatchLot = lot, ExpiryDate = undated ? null : (expiry ?? StockWorld.Expiry), Date = StockWorld.Today,
            ClearUnbatchedBacklog = false
        }).GetAwaiter().GetResult());
    }

    public static Result.Outcome DeleteAdjust(this StockWorld w, long id)
    {
        using var db = w.NewContext();
        return Result.Of(w.Adjusts(db).Delete(id).GetAwaiter().GetResult());
    }

    public static List<long> AdjustIds(this StockWorld w) { using var db = w.NewContext(); return db.AdjustStocks.OrderBy(s => s.Id).Select(s => s.Id).ToList(); }

    // ---------------------------------------------------------------- gives (Doctor caller, real endpoint)
    public static Response<ScheduleDTO> Give(this StockWorld w, long scheduleId, long childId, int doseId, long? brand = null, DateTime? date = null, Context? ctx = null, bool? confirmUnbatched = true, bool? reRecord = null)
    {
        var db = ctx ?? w.NewContext();
        try
        {
            return w.Schedules(db).Update(new ScheduleDTO
            {
                Id = scheduleId, ChildId = childId, DoseId = doseId, DoctorId = w.DoctorId, IsDone = true,
                GivenDate = date ?? StockWorld.Today, BrandId = brand ?? w.BrandId, Date = StockWorld.Today,
                ConfirmUnbatchedGive = confirmUnbatched, ReRecordHistorical = reRecord, IsPAApprove = true
            });
        }
        finally { if (ctx == null) db.Dispose(); }
    }

    public static Response<ScheduleDTO> Ungive(this StockWorld w, long scheduleId, long childId, int doseId)
    {
        using var db = w.NewContext();
        return w.Schedules(db).Update(new ScheduleDTO
        {
            Id = scheduleId, ChildId = childId, DoseId = doseId, DoctorId = w.DoctorId, IsDone = false, IsSkip = false, Date = StockWorld.Today
        });
    }

    public static Response<ScheduleDTO> GiveDose1(this StockWorld w, long? brand = null) => w.Give(w.Schedule1Id, w.ChildId, (int)w.Dose1Id, brand);
    public static Response<ScheduleDTO> UngiveDose1(this StockWorld w) => w.Ungive(w.Schedule1Id, w.ChildId, (int)w.Dose1Id);

    public static Response<ScheduleDTO> BulkGive(this StockWorld w, long anyScheduleId, long brand, params long[] scheduleIds)
    {
        using var db = w.NewContext();
        var dto = new ScheduleDTO { Id = anyScheduleId, DoctorId = w.DoctorId, IsDone = true, GivenDate = StockWorld.Today, Date = StockWorld.Today, ConfirmUnbatchedGive = true, IsPAApprove = true };
        foreach (var id in scheduleIds) dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)id, BrandId = brand });
        return w.Schedules(db).UpdateBulkInjection(dto);
    }

    public static Response<ScheduleDTO> BulkUngive(this StockWorld w, long anyScheduleId, long brand, bool sendBrands, params long[] scheduleIds)
    {
        using var db = w.NewContext();
        var dto = new ScheduleDTO { Id = anyScheduleId, DoctorId = w.DoctorId, IsDone = false, Date = StockWorld.Today };
        if (sendBrands)
            foreach (var id in scheduleIds) dto.ScheduleBrands.Add(new ScheduleBrandDTO { ScheduleId = (int)id, BrandId = brand });
        return w.Schedules(db).UpdateBulkInjection(dto);
    }
}

/// <summary>File-backed SQLite (separate connection per context) for real concurrency tests.</summary>
public sealed class FileDb : IDisposable
{
    static FileDb() { InventoryTransactionService.StrictInvariants = true; VaccineAPI.Models.Context.EnforceSingleInventoryWriter = true; }
    public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stocktest_" + Guid.NewGuid().ToString("N") + ".db");
    public DbContextOptions<Context> Options { get; }
    /// <summary>Clones the in-memory seeded world into a real file so several connections can race.</summary>
    public FileDb(StockWorld seeded)
    {
        using (var src = seeded.NewContext())
        {
            var mem = (SqliteConnection)src.Database.GetDbConnection();
            using var dst = new SqliteConnection($"DataSource={Path};Pooling=False");
            dst.Open();
            mem.BackupDatabase(dst);
        }
        Options = new DbContextOptionsBuilder<Context>().UseSqlite($"DataSource={Path};Pooling=False;Default Timeout=5").Options;
    }
    public Context NewContext() => new Context(Options);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { Path, Path + "-wal", Path + "-shm", Path + "-journal" })
            try { System.IO.File.Delete(f); } catch { }
    }
}
