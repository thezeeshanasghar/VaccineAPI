using VaccineAPI.Services;
using Xunit;
using static VaccineAPI.Services.AssignmentStage;

namespace VaccineAPI.Tests.Cases;

/// <summary>The stage the doctor sees for an assignment is computed in one place (PA_TRACKING_MERGE_SPEC.md).</summary>
public class AssignmentStageTests
{
    private static readonly DateTime Assigned = new DateTime(2026, 10, 19, 9, 0, 0);

    private static DoseFacts Dose(bool done = false, decimal amount = 0, bool paid = false, long? inv = null,
                                  DateTime? doneAt = null, DateTime? invAt = null)
        => new DoseFacts { IsDone = done, Amount = amount, IsPaymentCollected = paid, InvoiceSubmissionId = inv, DoneAt = doneAt, InvoiceSubmittedAt = invAt };

    [Fact]
    public void No_pins_or_nothing_given_is_Assigned_since_the_assignment_time()
    {
        var none = Compute(Assigned, false, null, new List<DoseFacts>());
        Assert.Equal(1, none.Stage);
        Assert.Equal(Assigned, none.Since);

        var undone = Compute(Assigned, false, null, new List<DoseFacts> { Dose(), Dose() });
        Assert.Equal(1, undone.Stage);
    }

    [Fact]
    public void Partly_given_is_still_Assigned()
        => Assert.Equal(1, Compute(Assigned, false, null, new List<DoseFacts> { Dose(done: true), Dose() }).Stage);

    [Fact]
    public void All_given_without_an_invoice_is_Given_since_the_last_give()
    {
        var t1 = Assigned.AddHours(2); var t2 = Assigned.AddHours(5);
        var r = Compute(Assigned, false, null, new List<DoseFacts> { Dose(true, 4000, doneAt: t1), Dose(true, 3000, doneAt: t2) });
        Assert.Equal(2, r.Stage);
        Assert.Equal(t2, r.Since);
    }

    [Fact]
    public void An_invoice_stamp_makes_it_Invoiced_until_every_billable_dose_is_paid()
    {
        var inv = Assigned.AddHours(6);
        var r = Compute(Assigned, false, null, new List<DoseFacts> { Dose(true, 4000, false, 9, invAt: inv), Dose(true, 3000, true, 9, invAt: inv) });
        Assert.Equal(3, r.Stage);
        Assert.Equal(inv, r.Since);
    }

    [Fact]
    public void Every_billable_dose_paid_is_Paid_and_free_doses_do_not_block_it()
    {
        var r = Compute(Assigned, false, null, new List<DoseFacts> { Dose(true, 4000, true, 9), Dose(true, 0, false, 9) });
        Assert.Equal(4, r.Stage);
    }

    [Fact]
    public void Nothing_billable_never_counts_as_Paid()
        => Assert.Equal(3, Compute(Assigned, false, null, new List<DoseFacts> { Dose(true, 0, false, 9) }).Stage);

    [Fact]
    public void Cash_confirmation_wins_over_everything_else()
    {
        var at = Assigned.AddDays(2);
        var r = Compute(Assigned, true, at, new List<DoseFacts> { Dose() });
        Assert.Equal(5, r.Stage);
        Assert.Equal(at, r.Since);
    }

    [Fact]
    public void Age_in_whole_days_never_goes_negative()
    {
        var now = new DateTime(2026, 10, 22, 9, 30, 0);
        Assert.Equal(2, AgeDays(new DateTime(2026, 10, 20, 9, 0, 0), now));
        Assert.Equal(1, AgeDays(new DateTime(2026, 10, 20, 10, 0, 0), now));   // 1 day 23.5 h is still 1 whole day
        Assert.Equal(0, AgeDays(now.AddHours(3), now));
    }
}
