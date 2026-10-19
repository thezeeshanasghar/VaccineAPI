using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// One child, two vaccine groups, two independent PA assignments. Ownership of a dose / invoice
/// must always be resolved through the pins, never by "the child's newest open assignment"
/// (PA_MULTI_ASSIGNMENT_SPEC.md I1-I5), and PaVisitGuard must keep old given doses out.
/// </summary>
public class MultiAssignmentResolverTests
{
    private sealed class Fx
    {
        public long ChildId, AsgA, AsgB;
        public long A1, A2, B1, B2;
    }

    private static Fx Seed(StockWorld w, long paA, long paB)
    {
        using var db = w.NewContext();
        var parent = new User { MobileNumber = "31" + Guid.NewGuid().ToString("N")[..8], Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        var child = new Child { Name = "Zahra", ClinicId = w.ClinicA, UserId = parent.Id, DOB = new DateTime(2026, 7, 13) };
        db.Childs.Add(child); db.SaveChanges();
        var v = new Vaccine { Name = "V", Validity = 12 };
        db.Vaccines.Add(v); db.SaveChanges();
        var d = new Dose { Name = "D", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 3650 };
        db.Doses.Add(d); db.SaveChanges();
        var dateA = new DateTime(2026, 10, 19);
        var dateB = new DateTime(2026, 11, 16);
        Schedule S(DateTime dt) => new Schedule { ChildId = child.Id, DoseId = d.Id, Date = dt };
        var a1 = S(dateA); var a2 = S(dateA); var b1 = S(dateB); var b2 = S(dateB);
        db.Schedules.AddRange(a1, a2, b1, b2); db.SaveChanges();
        var asgA = new PAAssignment { DoctorId = w.DoctorId, ChildId = child.Id, PersonalAssistantId = paA, AssignedAt = DateTime.UtcNow.AddMinutes(-10) };
        var asgB = new PAAssignment { DoctorId = w.DoctorId, ChildId = child.Id, PersonalAssistantId = paB, AssignedAt = DateTime.UtcNow };
        db.PAAssignments.AddRange(asgA, asgB); db.SaveChanges();
        db.PAAssignmentSchedules.AddRange(
            new PAAssignmentSchedule { AssignmentId = asgA.Id, ScheduleId = a1.Id },
            new PAAssignmentSchedule { AssignmentId = asgA.Id, ScheduleId = a2.Id },
            new PAAssignmentSchedule { AssignmentId = asgB.Id, ScheduleId = b1.Id },
            new PAAssignmentSchedule { AssignmentId = asgB.Id, ScheduleId = b2.Id });
        db.SaveChanges();
        return new Fx { ChildId = child.Id, AsgA = asgA.Id, AsgB = asgB.Id, A1 = a1.Id, A2 = a2.Id, B1 = b1.Id, B2 = b2.Id };
    }

    [Fact]
    public void Each_dose_resolves_to_its_own_assignment_not_the_newest()
    {
        using var w = new StockWorld();
        var f = Seed(w, paA: 17, paB: 19);
        using var db = w.NewContext();
        Assert.Equal(f.AsgA, AssignmentResolver.OpenAssignmentForDose(db, f.A1)!.Id);
        Assert.Equal(f.AsgB, AssignmentResolver.OpenAssignmentForDose(db, f.B2)!.Id);
        // Group A's collector is PA 17 even though PA 19's assignment is the newest for the child.
        Assert.Equal(17, AssignmentResolver.OwnerPaIdForDose(db, f.ChildId, f.A1));
        Assert.Equal(19, AssignmentResolver.OwnerPaIdForDose(db, f.ChildId, f.B1));
    }

    [Fact]
    public void Unpinned_dose_is_stamped_only_when_every_open_assignment_has_the_same_PA()
    {
        using var w = new StockWorld();
        var f = Seed(w, paA: 17, paB: 19);
        long extra;
        using (var db = w.NewContext())
        {
            var s = db.Schedules.First(x => x.Id == f.A1);
            var n = new Schedule { ChildId = f.ChildId, DoseId = s.DoseId, Date = new DateTime(2027, 1, 1) };
            db.Schedules.Add(n); db.SaveChanges(); extra = n.Id;
        }
        using (var db = w.NewContext())
            Assert.Null(AssignmentResolver.OwnerPaIdForDose(db, f.ChildId, extra));   // two different PAs: never guess

        using (var db = w.NewContext())
        {
            db.PAAssignments.Single(a => a.Id == f.AsgB).PersonalAssistantId = 17;   // now both open assignments are PA 17
            db.SaveChanges();
        }
        using (var db = w.NewContext())
            Assert.Equal(17, AssignmentResolver.OwnerPaIdForDose(db, f.ChildId, extra));
    }

    [Fact]
    public void Invoice_owner_is_the_assignment_of_its_own_group()
    {
        using var w = new StockWorld();
        var f = Seed(w, paA: 17, paB: 17);                                           // SAME PA, still separate
        using var db = w.NewContext();
        var groupA = db.Schedules.Where(s => s.Id == f.A1 || s.Id == f.A2).ToList();
        var owners = AssignmentResolver.OpenOwnersOfDoses(db, f.ChildId, groupA);
        Assert.Single(owners);
        Assert.Equal(f.AsgA, owners[0].Id);
    }

    [Fact]
    public void OwnersOfInvoice_follows_pins_and_ignores_the_sibling()
    {
        using var w = new StockWorld();
        var f = Seed(w, paA: 17, paB: 17);
        long invA;
        using (var db = w.NewContext())
        {
            var inv = new InvoiceSubmission { ChildId = f.ChildId, DoctorId = w.DoctorId, InvoiceDate = new DateTime(2026, 10, 19), SubmittedAt = DateTime.UtcNow };
            db.InvoiceSubmissions.Add(inv); db.SaveChanges(); invA = inv.Id;
            foreach (var id in new[] { f.A1, f.A2 }) db.Schedules.Single(s => s.Id == id).InvoiceSubmissionId = inv.Id;
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        var owners = AssignmentResolver.OwnersOfInvoice(db2, invA);
        Assert.Single(owners);
        Assert.Equal(f.AsgA, owners[0].Id);
    }

    [Fact]
    public void Cancelled_or_cash_confirmed_assignments_do_not_own_doses()
    {
        using var w = new StockWorld();
        var f = Seed(w, paA: 17, paB: 19);
        using (var db = w.NewContext())
        {
            db.PAAssignments.Single(a => a.Id == f.AsgA).IsCashConfirmedByDoctor = true;
            db.PAAssignments.Single(a => a.Id == f.AsgB).IsCancelled = true;
            db.SaveChanges();
        }
        using var db2 = w.NewContext();
        Assert.Null(AssignmentResolver.OpenAssignmentForDose(db2, f.A1));
        Assert.Null(AssignmentResolver.OpenAssignmentForDose(db2, f.B1));
    }

    [Fact]
    public void PaVisitGuard_keeps_old_confirmed_given_doses_out_but_keeps_this_visit()
    {
        var assignedAt = new DateTime(2026, 10, 19, 14, 0, 0, DateTimeKind.Utc);
        var confirmedInv = new InvoiceSubmission { IsConfirmedByDoctor = true };
        var openInv = new InvoiceSubmission { IsConfirmedByDoctor = false };
        var oldGiven = new Schedule { Id = 1, IsDone = true, DoneAt = new DateTime(2026, 3, 15), Amount = 5000 };
        Assert.False(PaVisitGuard.DoseBelongsToVisit(oldGiven, confirmedInv, assignedAt, 17));

        var givenToday = new Schedule { Id = 2, IsDone = true, DoneAt = assignedAt.AddHours(-1), Amount = 5000 };
        Assert.True(PaVisitGuard.DoseBelongsToVisit(givenToday, openInv, assignedAt, 17));

        var givenByPa = new Schedule { Id = 3, IsDone = true, DoneAt = new DateTime(2026, 3, 15), GivenByPaId = 17 };
        Assert.True(PaVisitGuard.DoseBelongsToVisit(givenByPa, confirmedInv, assignedAt, 17));

        var undone = new Schedule { Id = 4, IsDone = false };
        Assert.True(PaVisitGuard.DoseBelongsToVisit(undone, confirmedInv, assignedAt, 17));

        var recentUnpaid = new Schedule { Id = 5, IsDone = true, DoneAt = assignedAt.AddDays(-4), Amount = 4000, IsPaymentCollected = false };
        Assert.True(PaVisitGuard.DoseBelongsToVisit(recentUnpaid, openInv, assignedAt, 17));
        Assert.False(PaVisitGuard.DoseBelongsToVisit(recentUnpaid, confirmedInv, assignedAt, 17));
    }
}
