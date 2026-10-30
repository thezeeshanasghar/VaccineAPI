using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// PA_ORPHAN_INVOICE_FIX_SPEC.md acceptance A1-A5 for PaCashHandoverController.GetMyReconciliation.
/// </summary>
public class PaMyReconciliationTests
{
    // Test-only harness: SQLite cannot SUM a decimal column server-side (the controller sums
    // PaPayableAdjustments.Amount in SQL). Map that one column to double for SQLite only.
    private sealed class SqliteSumContext : Context
    {
        public SqliteSumContext(Microsoft.EntityFrameworkCore.DbContextOptions<Context> o) : base(o) { }
        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<PaPayableAdjustment>().Property(a => a.Amount).HasConversion<double>();
        }
    }

    private static long SeedPa(StockWorld w)
    {
        using var db = w.NewContext();
        var u = new User { MobileNumber = "33" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var pa = new PersonalAssistant { Name = "Omar", DoctorId = w.DoctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(pa); db.SaveChanges();
        return pa.Id;
    }

    private static long Inv(StockWorld w, long paId, decimal amt, string status = "Active", bool confirmed = false,
        bool pendingHandover = false, DateTime? doneAt = null, string? mode = null, long? clinic = null)
    {
        using var db = w.NewContext();
        var i = new InvoiceSubmission
        {
            ChildId = w.ChildId, DoctorId = w.DoctorId, PaId = paId, ClinicId = clinic ?? w.ClinicA,
            InvoiceDate = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow, TotalAmount = amt,
            InvoiceStatus = status, IsConfirmedByDoctor = confirmed, PendingHandover = pendingHandover,
            HandoverDoneAt = doneAt, PaymentMode = mode
        };
        db.InvoiceSubmissions.Add(i); db.SaveChanges();
        return i.Id;
    }

    private static void DirectSaleCash(StockWorld w, long paId, decimal amt, string bill)
    {
        using var db = w.NewContext();
        db.BypassInventoryWriterCheck = true;
        db.DirectSales.Add(new DirectSale
        {
            BrandId = w.BrandId, ClinicId = w.ClinicA, DoctorId = w.DoctorId, Quantity = 1,
            SalePricePerUnit = amt, TotalSaleValue = amt, PaymentMode = "Cash", IsPaymentApproved = true,
            IsPaymentCollected = true, PaymentCollectorPaId = paId, SaleBillNo = bill, SaleDate = DateTime.UtcNow,
            ClientName = "Walk-in"
        });
        db.SaveChanges();
    }

    private static JToken Call(StockWorld w, long paId, long? clinicId = null)
    {
        using var db = new SqliteSumContext(w.Db.Options);
        var res = (OkObjectResult)new PaCashHandoverController(db).GetMyReconciliation(paId, clinicId);
        return JObject.FromObject(res.Value!)["ResponseData"]!;
    }

    private static List<JToken> InvoiceRows(JToken d) =>
        d["Rows"]!.Where(r => (string)r["RowType"]! == "Invoice").ToList();

    // A1
    [Fact]
    public void A1_Cancelled_invoice_excluded_from_rows_and_totals()
    {
        using var w = new StockWorld();
        var pa = SeedPa(w);
        var live = Inv(w, pa, 1000);
        var cancelled = Inv(w, pa, 12504, status: "Cancelled");
        var d = Call(w, pa);
        var ids = InvoiceRows(d).Select(r => (long)r["InvoiceSubmissionId"]!).ToList();
        Assert.Contains(live, ids);
        Assert.DoesNotContain(cancelled, ids);
        Assert.Equal(1000m, (decimal)d["TotalCollected"]!);
        Assert.Equal(1000m, (decimal)d["TotalPending"]!);
    }

    // A2
    [Fact]
    public void A2_Unconfirmed_row_carries_handover_fields()
    {
        using var w = new StockWorld();
        var pa = SeedPa(w);
        var done = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);
        var a = Inv(w, pa, 500, pendingHandover: true, doneAt: done, mode: "Cash");
        var b = Inv(w, pa, 700);
        var rows = InvoiceRows(Call(w, pa));
        var ra = rows.Single(r => (long)r["InvoiceSubmissionId"]! == a);
        var rb = rows.Single(r => (long)r["InvoiceSubmissionId"]! == b);
        Assert.NotNull(ra["PendingHandover"]);
        Assert.True((bool)ra["PendingHandover"]!);
        Assert.NotNull(ra["HandoverDoneAt"]);
        Assert.Equal(Pkt.Iso(done), (string)ra["HandoverDoneAt"]!);
        Assert.Equal("Cash", (string)ra["PaymentMode"]!);
        Assert.NotNull(rb["PendingHandover"]);
        Assert.False((bool)rb["PendingHandover"]!);
        Assert.Equal(JTokenType.Null, rb["HandoverDoneAt"]!.Type);
        Assert.Equal("", (string)rb["PaymentMode"]!);   // null mode -> ""
    }

    // A5
    [Fact]
    public void A5_Confirmed_invoice_not_in_rows_but_in_totals()
    {
        using var w = new StockWorld();
        var pa = SeedPa(w);
        var conf = Inv(w, pa, 2000, confirmed: true);
        var open = Inv(w, pa, 300);
        var d = Call(w, pa);
        var ids = InvoiceRows(d).Select(r => (long)r["InvoiceSubmissionId"]!).ToList();
        Assert.Equal(new[] { open }, ids);
        Assert.DoesNotContain(conf, ids);
        Assert.Equal(2300m, (decimal)d["TotalCollected"]!);
        Assert.Equal(2000m, (decimal)d["TotalConfirmed"]!);
        Assert.Equal(300m, (decimal)d["TotalPending"]!);
    }

    // A4 golden numbers (no cancelled invoices): invoices + direct sale + adjustment
    [Fact]
    public void A4_TotalPending_golden_numbers_unchanged_without_cancelled()
    {
        using var w = new StockWorld();
        var pa = SeedPa(w);
        Inv(w, pa, 1000);
        Inv(w, pa, 2500, confirmed: true);
        Inv(w, pa, 400, pendingHandover: true, mode: "Cash");
        Inv(w, pa, 0);                       // TotalAmount > 0 filter still applies
        DirectSaleCash(w, pa, 150, "B1");
        using (var db = w.NewContext())
        {
            db.PaPayableAdjustments.Add(new PaPayableAdjustment { PaId = pa, DoctorId = w.DoctorId, Amount = -50, Reason = "t" });
            db.SaveChanges();
        }
        var d = Call(w, pa);
        Assert.Equal(4050m, (decimal)d["TotalCollected"]!);  // 1000+2500+400+150
        Assert.Equal(2500m, (decimal)d["TotalConfirmed"]!);
        Assert.Equal(1500m, (decimal)d["TotalPending"]!);    // 4050-2500-50
    }

    // direct-sale rows unchanged (exact field set)
    [Fact]
    public void DirectSale_row_shape_unchanged()
    {
        using var w = new StockWorld();
        var pa = SeedPa(w);
        DirectSaleCash(w, pa, 150, "B1");
        var row = Call(w, pa)["Rows"]!.Single(r => (string)r["RowType"]! == "DirectSale");
        var props = ((JObject)row).Properties().Select(p => p.Name).OrderBy(x => x).ToList();
        var expected = new[] { "RowType", "InvoiceSubmissionId", "ChildId", "Date", "PatientName", "Amount", "IsConfirmed", "DirectSaleBillNo" }.OrderBy(x => x).ToList();
        Assert.Equal(expected, props);
        Assert.Equal(150m, (decimal)row["Amount"]!);
        Assert.Equal("B1", (string)row["DirectSaleBillNo"]!);
    }

    // clinic filter
    [Fact]
    public void ClinicId_filter_still_works()
    {
        using var w = new StockWorld();
        var pa = SeedPa(w);
        var a = Inv(w, pa, 100, clinic: w.ClinicA);
        var b = Inv(w, pa, 200, clinic: w.ClinicB);
        var d = Call(w, pa, w.ClinicB);
        Assert.Equal(new[] { b }, InvoiceRows(d).Select(r => (long)r["InvoiceSubmissionId"]!).ToArray());
        Assert.Equal(200m, (decimal)d["TotalCollected"]!);
        Assert.Equal(300m, (decimal)Call(w, pa)["TotalCollected"]!);
    }
}
