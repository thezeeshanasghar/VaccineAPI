using AutoMapper;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;

namespace VaccineAPI.Tests.Infrastructure;

/// <summary>
/// Seeded world used by every stock test: one doctor, two clinics (A online, B), one brand,
/// one vaccine with two doses, one child with a schedule row per dose, and a BrandAmount
/// row per clinic. Controllers are the REAL production controllers on the production Context.
/// </summary>
public sealed class StockWorld : IDisposable
{
    public readonly TestDb Db = new TestDb();
    public long DoctorId, ClinicA, ClinicB, BrandId;
    public long ChildId, Dose1Id, Dose2Id, Schedule1Id, Schedule2Id;
    public static readonly DateTime Today = VaccineAPI.ClinicClock.TodayPkt();
    public static readonly DateTime Expiry = new DateTime(2035, 1, 1);

    public StockWorld()
    {
        using var db = Db.NewContext();
        db.BypassInventoryWriterCheck = true;   // fixture seeding
        var user = new User { MobileNumber = "3000000001", Password = "x", UserType = "DOCTOR", CountryCode = "92" };
        var childUser = new User { MobileNumber = "3000000002", Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.AddRange(user, childUser);
        db.SaveChanges();

        var doctor = new Doctor { FirstName = "Test", UserId = user.Id, AllowInventory = true };
        db.Doctors.Add(doctor);
        db.SaveChanges();
        DoctorId = doctor.Id;

        var a = new Clinic { Name = "A", DoctorId = DoctorId, IsOnline = true, MaintainInventory = true };
        var b = new Clinic { Name = "B", DoctorId = DoctorId, IsOnline = false, MaintainInventory = true };
        db.Clinics.AddRange(a, b);
        var brand = new Brand { Name = "BRANDX", Manufacturer = "M", Route = "IM" };
        db.Brands.Add(brand);
        var vaccine = new Vaccine { Name = "VAX", Validity = 12 };
        db.Vaccines.Add(vaccine);
        db.SaveChanges();
        ClinicA = a.Id; ClinicB = b.Id; BrandId = brand.Id;

        db.BrandAmounts.AddRange(
            new BrandAmount { BrandId = BrandId, DoctorId = DoctorId, ClinicId = ClinicA, SalePrice = 100 },
            new BrandAmount { BrandId = BrandId, DoctorId = DoctorId, ClinicId = ClinicB, SalePrice = 100 });

        var d1 = new Dose { Name = "Dose 1", VaccineId = vaccine.Id, DoseOrder = 1, MinAge = 0 };
        var d2 = new Dose { Name = "Dose 2", VaccineId = vaccine.Id, DoseOrder = 2, MinAge = 0 };
        db.Doses.AddRange(d1, d2);
        var child = new Child { Name = "Kid", ClinicId = ClinicA, UserId = childUser.Id, DOB = Today.AddYears(-2) };
        db.Childs.Add(child);
        db.SaveChanges();
        Dose1Id = d1.Id; Dose2Id = d2.Id; ChildId = child.Id;

        var s1 = new Schedule { ChildId = ChildId, DoseId = Dose1Id, Date = Today };
        var s2 = new Schedule { ChildId = ChildId, DoseId = Dose2Id, Date = Today };
        db.Schedules.AddRange(s1, s2);
        db.SaveChanges();
        Schedule1Id = s1.Id; Schedule2Id = s2.Id;
    }

    public Context NewContext() => Db.NewContext();

    // ---- controller factories (fresh Context per call = fresh change tracker, like a request)
    public BillController Bills(Context db) => new BillController(db, new InventoryTransactionService(db));
    public StockTransferController Transfers(Context db) => new StockTransferController(db, new InventoryTransactionService(db));
    public DirectSaleController Sales(Context db) => new DirectSaleController(db, new InventoryTransactionService(db));
    public AdjustStockController Adjusts(Context db) => new AdjustStockController(db, new InventoryTransactionService(db));
    public ScheduleController Schedules(Context db)
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<VaccineAPI.AutoMapperProfile>()).CreateMapper();
        var config = new ConfigurationBuilder().Build();
        return new ScheduleController(db, mapper, null!, new InventoryTransactionService(db), config);
    }

    // ---- scenario helpers (all go through production endpoints)
    public int PostBill(long clinicId, int qty, decimal unitPrice, string lot = "L1", decimal awt = 0, DateTime? date = null)
    {
        using var db = NewContext();
        var r = Bills(db).Create(new BillCreateDTO
        {
            BillDate = date ?? Today, DoctorId = DoctorId, ClinicId = clinicId, SupplierName = "Sup",
            AwtPercent = awt, BillNo = "B-" + Guid.NewGuid().ToString("N")[..6],
            Lines = { new BillLineDTO { BrandId = BrandId, BatchLot = lot, Expiry = Expiry, Quantity = qty, UnitPrice = unitPrice } }
        }).GetAwaiter().GetResult();
        var o = Result.Of(r);
        Assert.True(o.IsSuccess, "PostBill failed: " + o.Message);
        return (int)Result.Prop(r, "ResponseData", "Id")!;
    }

    /// <summary>One real dose through the same service call the give endpoint uses.</summary>
    public void GiveViaService(long clinicId, int doses)
    {
        for (int i = 0; i < doses; i++)
        {
            using var db = NewContext();
            var ba = db.BrandAmounts.First(x => x.BrandId == BrandId && x.ClinicId == clinicId);
            new InventoryTransactionService(db).AdministerSync(ba, clinicId, 9000 + i, Today, null, true, "NORMAL", out _);
            db.SaveChanges();
        }
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>Unwraps the controllers' anonymous Ok(new { IsSuccess, Message, ResponseData }) results.</summary>
public static class Result
{
    public record Outcome(bool IsSuccess, string? Message);

    public static Outcome Of(IActionResult r)
    {
        var ok = Assert.IsType<OkObjectResult>(r);
        var v = ok.Value!;
        if (v is Newtonsoft.Json.Linq.JObject jo)   // a replayed (duplicate) response
            return new Outcome(jo.Value<bool>("IsSuccess"), jo.Value<string>("Message"));
        var t = v.GetType();
        return new Outcome((bool)t.GetProperty("IsSuccess")!.GetValue(v)!, t.GetProperty("Message")?.GetValue(v) as string);
    }

    public static object? Prop(IActionResult r, params string[] path)
    {
        object? cur = Assert.IsType<OkObjectResult>(r).Value;
        foreach (var p in path) cur = cur?.GetType().GetProperty(p)?.GetValue(cur);
        return cur;
    }
}
