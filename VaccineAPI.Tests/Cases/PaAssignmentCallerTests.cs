using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// On 2026-11-26 requests with no login token listed, cancelled and asked to cancel assignments on
/// the live API (Auth Mode = Log). Who may act on an assignment now comes from the token only:
/// no token is refused, and ids in the URL or body cannot claim to be someone else.
/// </summary>
[Collection("AuthStatics")]
public class PaAssignmentCallerTests
{
    // Real PA rows (activity-log rows reference the PA). Set by Seed.
    private long Pa, OtherPa;

    private static PAAssignmentController Ctl(Context db) =>
        new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

    private long AddPa(Context db, long doctorId, string name)
    {
        var u = new User { MobileNumber = "36" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PA", CountryCode = "92" };
        db.Users.Add(u); db.SaveChanges();
        var p = new PersonalAssistant { Name = name, DoctorId = doctorId, UserId = u.Id, IsActive = true };
        db.PersonalAssistant.Add(p); db.SaveChanges();
        return p.Id;
    }

    private long Seed(StockWorld w)
    {
        using var db = w.NewContext();
        Pa = AddPa(db, w.DoctorId, "Owner PA");
        OtherPa = AddPa(db, w.DoctorId, "Other PA");
        var a = new PAAssignment { DoctorId = w.DoctorId, ClinicId = w.ClinicA, ChildId = w.ChildId, PersonalAssistantId = Pa, AssignedAt = DateTime.UtcNow };
        db.PAAssignments.Add(a); db.SaveChanges();
        db.PAAssignmentSchedules.Add(new PAAssignmentSchedule { AssignmentId = a.Id, ScheduleId = w.Schedule1Id });
        db.SaveChanges();
        return a.Id;
    }

    private static bool Ok(IActionResult r) => (bool)JObject.FromObject(((OkObjectResult)r).Value!)["IsSuccess"]!;
    private static string Msg(IActionResult r) => (string)JObject.FromObject(((OkObjectResult)r).Value!)["Message"]!;

    private static bool Open(StockWorld w, long id)
    {
        using var db = w.NewContext();
        var a = db.PAAssignments.SingleOrDefault(x => x.Id == id);
        return a != null && !a.IsCancelled;
    }

    [Fact]
    public void Cancel_with_no_token_is_refused_even_when_the_body_claims_the_PA()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using var db = w.NewContext();
        var r = TestAuth.As(null, () => Ctl(db).Cancel(id, new CancelAssignmentDto { CallerType = "PA", CallerId = Pa })).GetAwaiter().GetResult();
        Assert.False(Ok(r));
        Assert.Contains("log in again", Msg(r));
        Assert.True(Open(w, id));
    }

    [Fact]
    public void Another_PA_cannot_cancel_by_putting_the_owner_id_in_the_body()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using var db = w.NewContext();
        var r = TestAuth.As(TestAuth.Pa(OtherPa, w.DoctorId), () => Ctl(db).Cancel(id, new CancelAssignmentDto { CallerType = "PA", CallerId = Pa })).GetAwaiter().GetResult();
        Assert.False(Ok(r));
        Assert.True(Open(w, id));
    }

    [Fact]
    public void The_owning_PA_cancels_and_the_log_names_the_PA_from_the_token()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using (var db = w.NewContext())
        {
            // The body says DOCTOR; the token says PA 17. The token wins.
            var r = TestAuth.As(TestAuth.Pa(Pa, w.DoctorId), () => Ctl(db).Cancel(id, new CancelAssignmentDto { CallerType = "DOCTOR", CallerId = w.DoctorId })).GetAwaiter().GetResult();
            Assert.True(Ok(r), Msg(r));
        }
        Assert.False(Open(w, id));
        using var chk = w.NewContext();
        Assert.Equal(Pa, chk.PaActivityLogs.Single(l => l.ActionCode == "AsgCancelled").PaId);
    }

    [Fact]
    public void A_doctor_of_another_practice_cannot_cancel()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using var db = w.NewContext();
        var r = TestAuth.As(TestAuth.Doctor(w.DoctorId + 999), () => Ctl(db).Cancel(id, new CancelAssignmentDto { CallerType = "DOCTOR", CallerId = w.DoctorId })).GetAwaiter().GetResult();
        Assert.False(Ok(r));
        Assert.True(Open(w, id));
    }

    [Fact]
    public void Full_reset_with_no_token_is_refused_and_nothing_is_removed()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using var db = w.NewContext();
        var r = TestAuth.As(null, () => Ctl(db).DeleteAssignment(id, w.DoctorId, "FullReset", "probe")).GetAwaiter().GetResult();
        Assert.False(Ok(r));
        Assert.True(Open(w, id));
    }

    [Fact]
    public void A_manager_cannot_remove_or_reverse_an_assignment()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using var db = w.NewContext();
        var r = TestAuth.As(TestAuth.Manager(5, w.DoctorId), () => Ctl(db).DeleteAssignment(id, w.DoctorId, "UnassignOnly")).GetAwaiter().GetResult();
        Assert.False(Ok(r));
        Assert.True(Open(w, id));
    }

    [Fact]
    public void Cancel_request_and_refusal_with_no_token_are_refused()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using var db = w.NewContext();
        Assert.False(Ok(TestAuth.As(null, () => Ctl(db).RequestCancel(id, new CancelAssignmentDto { CallerType = "PA", CallerId = Pa })).GetAwaiter().GetResult()));
        Assert.False(Ok(TestAuth.As(null, () => Ctl(db).RequestRefusal(id, new CancelAssignmentDto { CallerId = Pa })).GetAwaiter().GetResult()));
        using var chk = w.NewContext();
        Assert.Equal("Active", chk.PAAssignments.Single(a => a.Id == id).AssignmentStatus ?? "Active");
        Assert.Empty(chk.VaccineRefusals);
    }

    [Fact]
    public void Approving_a_refusal_needs_the_doctor_not_a_PA_or_no_token()
    {
        using var w = new StockWorld();
        var id = Seed(w);
        using (var db = w.NewContext())
            Assert.True(Ok(TestAuth.As(TestAuth.Pa(Pa, w.DoctorId), () => Ctl(db).RequestRefusal(id, new CancelAssignmentDto { CallerId = Pa })).GetAwaiter().GetResult()));
        using (var db = w.NewContext())
        {
            Assert.False(Ok(TestAuth.As(null, () => Ctl(db).ApproveRefusal(id, w.DoctorId)).GetAwaiter().GetResult()));
            Assert.False(Ok(TestAuth.As(TestAuth.Pa(Pa, w.DoctorId), () => Ctl(db).ApproveRefusal(id, w.DoctorId)).GetAwaiter().GetResult()));
        }
        Assert.True(Open(w, id));
    }

    [Fact]
    public void Lists_need_a_signed_in_member_of_the_practice()
    {
        using var w = new StockWorld();
        Seed(w);
        using var db = w.NewContext();
        Assert.False(Ok(TestAuth.As(null, () => Ctl(db).GetActiveForDoctor(w.DoctorId)).GetAwaiter().GetResult()));
        Assert.False(Ok(TestAuth.As(TestAuth.Doctor(w.DoctorId + 999), () => Ctl(db).GetActiveForDoctor(w.DoctorId)).GetAwaiter().GetResult()));
        Assert.True(Ok(TestAuth.As(TestAuth.Doctor(w.DoctorId), () => Ctl(db).GetActiveForDoctor(w.DoctorId)).GetAwaiter().GetResult()));
        Assert.False(Ok(TestAuth.As(null, () => Ctl(db).GetPAsForClinic(w.ClinicA)).GetAwaiter().GetResult()));
        Assert.False(Ok(TestAuth.As(null, () => Ctl(db).GetRefusalFlag(w.ChildId)).GetAwaiter().GetResult()));
    }

    [Fact]
    public void A_manager_id_in_the_url_must_be_the_caller()
    {
        using var w = new StockWorld();
        Seed(w);
        using var db = w.NewContext();
        // Signed in as manager 5, asking as manager 6.
        var r = TestAuth.As(TestAuth.Manager(5, w.DoctorId), () => Ctl(db).GetForDoctor(w.DoctorId, requestingManagerId: 6)).GetAwaiter().GetResult();
        Assert.False(Ok(r));
    }
}
