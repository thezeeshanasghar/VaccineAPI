using Microsoft.AspNetCore.Http;
using VaccineAPI.Controllers;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

// Doctor-only patient move between the same doctor's clinics (+ target-clinic guard on update-clinic-id).
public class MoveClinicTests
{
    sealed class World : IDisposable
    {
        public readonly TestDb Db = new TestDb();
        public long DoctorA, DoctorB, A1, A2, B1, ChildId, DoseId;
        public World()
        {
            using var db = Db.NewContext();
            var ua = new User { MobileNumber = "3001110001", Password = "x", UserType = "DOCTOR", CountryCode = "92" };
            var ub = new User { MobileNumber = "3001110002", Password = "x", UserType = "DOCTOR", CountryCode = "92" };
            db.Users.AddRange(ua, ub); db.SaveChanges();
            var da = new Doctor { FirstName = "A", UserId = ua.Id };
            var dbb = new Doctor { FirstName = "B", UserId = ub.Id };
            db.Doctors.AddRange(da, dbb); db.SaveChanges();
            DoctorA = da.Id; DoctorB = dbb.Id;
            var c1 = new Clinic { Name = "A-Gulberg", DoctorId = DoctorA };
            var c2 = new Clinic { Name = "A-DHA", DoctorId = DoctorA };
            var c3 = new Clinic { Name = "B-Main", DoctorId = DoctorB };
            db.Clinics.AddRange(c1, c2, c3); db.SaveChanges();
            A1 = c1.Id; A2 = c2.Id; B1 = c3.Id;
            var pu = new User { MobileNumber = "3337770199", Password = "x", UserType = "PARENT", CountryCode = "92" };
            db.Users.Add(pu); db.SaveChanges();
            var ch = new Child { Name = "KID", ClinicId = A1, UserId = pu.Id, DOB = new DateTime(2025, 3, 14) };
            db.Childs.Add(ch); db.SaveChanges();
            ChildId = ch.Id;
            var v = new Vaccine { Name = "V", Validity = 12 }; db.Vaccines.Add(v); db.SaveChanges();
            var d = new Dose { Name = "D", VaccineId = v.Id, DoseOrder = 1, MinAge = 0, MaxAge = 3650 }; db.Doses.Add(d); db.SaveChanges();
            DoseId = d.Id;
        }
        public Context Ctx() => Db.NewContext();
        public void Dispose() => Db.Dispose();
    }

    static void As(AuthIdentity? id, Action body)
    {
        var saved = AuthContext.Accessor;
        var http = new DefaultHttpContext();
        if (id != null) http.Items["auth.identity"] = id;
        AuthContext.Accessor = new HttpContextAccessor { HttpContext = http };
        try { body(); } finally { AuthContext.Accessor = saved; }
    }

    static AuthIdentity DocA(World w) => new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = w.DoctorA };

    static Response<string> Move(World w, long to)
    {
        using var db = w.Ctx();
        return new ChildController(db, null!, null!, null!).MoveClinic(w.ChildId, to);
    }

    [Fact]
    public void Doctor_MovesCleanPatient_AndAuditRowWritten()
    {
        using var w = new World();
        As(DocA(w), () =>
        {
            var r = Move(w, w.A2);
            Assert.True(r.IsSuccess, r.Message);
        });
        using var db = w.Ctx();
        Assert.Equal(w.A2, db.Childs.Single(c => c.Id == w.ChildId).ClinicId);
        var log = db.PaActivityLogs.Single();
        Assert.Equal("PatientClinicMoved", log.ActionCode);
        Assert.Equal(w.ChildId, log.PatientId);
    }

    [Fact]
    public void OtherDoctorsClinic_Refused()
    {
        using var w = new World();
        As(DocA(w), () => Assert.False(Move(w, w.B1).IsSuccess));
        using var db = w.Ctx();
        Assert.Equal(w.A1, db.Childs.Single().ClinicId);
    }

    [Fact]
    public void SameClinic_Refused()
    {
        using var w = new World();
        As(DocA(w), () => Assert.False(Move(w, w.A1).IsSuccess));
    }

    [Fact]
    public void BilledDoseAndPaAssignment_NoLongerBlock()
    {
        using var w = new World();
        using (var db = w.Ctx())
        {
            db.Schedules.Add(new Schedule { ChildId = w.ChildId, DoseId = w.DoseId, Date = new DateTime(2026, 11, 1), IsDone = true, Amount = 3500 });
            db.PAAssignments.Add(new PAAssignment { ChildId = w.ChildId, DoctorId = w.DoctorA, ClinicId = w.A1 });
            db.SaveChanges();
        }
        As(DocA(w), () => Assert.True(Move(w, w.A2).IsSuccess));
        using var db2 = w.Ctx();
        Assert.Equal(w.A2, db2.Childs.Single().ClinicId);
    }

    [Fact]
    public void ForeignDoctorCaller_Refused()
    {
        using var w = new World();
        As(new AuthIdentity { UserId = 2, Role = "DOCTOR", DoctorId = w.DoctorB }, () => Assert.False(Move(w, w.A2).IsSuccess));
        using var db = w.Ctx();
        Assert.Equal(w.A1, db.Childs.Single().ClinicId);
    }

    [Fact]
    public async Task UpdateClinicId_StaffCannotTargetForeignClinic()
    {
        using var w = new World();
        As(DocA(w), () =>
        {
            using var db = w.Ctx();
            var ctl = new DoctorController(db, null!, null!);
            var res = ctl.UpdateClinicIdForChild(null, w.ChildId, w.B1).GetAwaiter().GetResult();
            var obj = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(res.Result);
            Assert.Equal(403, obj.StatusCode);
        });
        using var db2 = w.Ctx();
        Assert.Equal(w.A1, db2.Childs.Single().ClinicId);
        await Task.CompletedTask;
    }
}
