using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>update-bulk-invoice, the creation path the old suite never drove: flags off vs on must give the same result.</summary>
[Collection("AuthStatics")]
public class Phase4InvoiceTxTests
{
    private static (bool Ok, string Message, string Snap, List<string> Audit) Run(bool tx, bool stamp)
    {
        AuthContext.Accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        using var w = new StockWorld();
        using (var db = w.NewContext())
        {
            db.FeatureFlags.AddRange(new FeatureFlag { FlagKey = "integrity.tx", IsOn = tx }, new FeatureFlag { FlagKey = FinancialAudit.Flag, IsOn = stamp });
            db.SaveChanges();
        }
        FeatureFlags.ResetCache();
        w.PostBill(w.ClinicA, 5, 10);
        Assert.True(w.BulkGive(w.Schedule1Id, w.BrandId, w.Schedule1Id, w.Schedule2Id).IsSuccess);

        Response<object> r;
        using (var db = w.NewContext())
            r = w.Schedules(db).updateInvoice(new BulkInvoiceSubmitDTO
            {
                ChildId = w.ChildId, DoctorId = w.DoctorId, ClinicId = w.ClinicA, InvoiceDate = DateTime.UtcNow.Date, ConsultationFee = 0,
                Schedules = new List<ScheduleAmountItem> { new() { Id = w.Schedule1Id, Amount = 3900 }, new() { Id = w.Schedule2Id, Amount = 3900 } }
            });

        using var fin = w.NewContext();
        var snap = string.Join(";", fin.InvoiceSubmissions.OrderBy(i => i.Id).Select(i => $"I|{i.PaId}|{i.TotalAmount}|{i.InvoiceStatus}"))
                 + "|" + string.Join(";", fin.Schedules.OrderBy(s => s.Id).Select(s => $"S{s.Id}|{s.Amount}|{(s.InvoiceSubmissionId.HasValue ? "linked" : "none")}"));
        return (r.IsSuccess, r.Message ?? "", snap, fin.FinancialAudit.Select(a => a.Action).ToList());
    }

    [Fact]
    public void Invoice_creation_is_identical_with_the_tx_and_stamp_flags_on()
    {
        var off = Run(false, false);
        Assert.True(off.Ok, off.Message);
        Assert.Contains("linked", off.Snap);
        var on = Run(true, true);
        Assert.True(on.Ok, on.Message);
        Assert.Equal(off.Snap, on.Snap);
        Assert.Empty(off.Audit);
        Assert.Contains("InvoiceCreate", on.Audit);
    }
}
