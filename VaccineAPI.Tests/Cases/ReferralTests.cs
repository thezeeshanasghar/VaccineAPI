using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

// Two-step agent registration (AGENT_REFERRAL_TWO_STEP_SPEC.md): agent files a referral, clinic staff
// register the patient through Add Patient and then convert it. These tests call the controller
// directly on the SQLite test database; [Owns]/[RolesOnly] are action filters and are covered by the
// RoleAccess matrix in AuthTests, while the in-controller practice checks (CallerGuard) are covered
// here by setting an identity.
public class ReferralTests
{
    sealed class World : IDisposable
    {
        public readonly TestDb Db = new TestDb();
        public long DoctorA, DoctorB, ClinicA1, ClinicA2, ClinicB;
        public int AgentOk, AgentOk2, AgentNoFlags, AgentNoClinic;
        public static readonly string Dob = "14-03-2025";

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
            ClinicA1 = c1.Id; ClinicA2 = c2.Id; ClinicB = c3.Id;

            Agent MakeAgent(string name, long? clinic, bool flag) => new Agent
            {
                Name = name, PhoneNumber = "0300" + Guid.NewGuid().ToString("N")[..7], Password = "x",
                AgentCode = "A-" + name, ClinicId = clinic, CanRegisterTravel = flag
            };
            var a1 = MakeAgent("Ali", ClinicA1, true);
            var a2 = MakeAgent("Noman", ClinicA1, true);
            var a3 = MakeAgent("Flagless", ClinicA1, false);
            var a4 = MakeAgent("Clinicless", null, true);
            db.Agents.AddRange(a1, a2, a3, a4); db.SaveChanges();
            AgentOk = a1.Id; AgentOk2 = a2.Id; AgentNoFlags = a3.Id; AgentNoClinic = a4.Id;
        }

        public Context Ctx() => Db.NewContext();
        public void Dispose() => Db.Dispose();

        public AgentReferralCreateDTO Dto(int agent, string mobile = "3337770199", string dob = null!, string name = "Hassan Raza") =>
            new AgentReferralCreateDTO
            {
                AgentId = agent, Name = name, FatherName = "Raza Abbas", Gender = "Boy",
                DOB = dob ?? Dob, CountryCode = "92", MobileNumber = mobile, City = "Lahore"
            };

        public long Child(long clinic, string mobile = "3337770199", int agentId = 0)
        {
            using var db = Ctx();
            var u = new User { MobileNumber = mobile, Password = "x", UserType = "PARENT", CountryCode = "92" };
            db.Users.Add(u); db.SaveChanges();
            var c = new Child { Name = "KID", ClinicId = clinic, UserId = u.Id, DOB = new DateTime(2025, 3, 14), AgentId = agentId == 0 ? null : agentId };
            db.Childs.Add(c); db.SaveChanges();
            return c.Id;
        }
    }

    static Response<object> Send(World w, AgentReferralCreateDTO dto)
    {
        using var db = w.Ctx();
        return new ReferralController(db).PostReferral(dto);
    }

    static long NewId(Response<object> r) => (long)r.ResponseData!.GetType().GetProperty("Id")!.GetValue(r.ResponseData)!;

    static void As(AuthIdentity? id, Action body)
    {
        var saved = AuthContext.Accessor;
        var http = new DefaultHttpContext();
        if (id != null) http.Items["auth.identity"] = id;
        AuthContext.Accessor = new HttpContextAccessor { HttpContext = http };
        try { body(); } finally { AuthContext.Accessor = saved; }
    }

    static object P(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o)!;
    static IEnumerable<object> Rows(object o) => ((System.Collections.IEnumerable)o).Cast<object>();

    // ------------------------------------------------------------ agent: filing a referral

    [Fact]
    public void Post_Valid_CreatesReferralInAgentsClinic_AndNotifiesDoctor_ButCreatesNoChildOrUser()
    {
        using var w = new World();
        int usersBefore, childsBefore;
        using (var db = w.Ctx()) { usersBefore = db.Users.Count(); childsBefore = db.Childs.Count(); }

        var r = Send(w, w.Dto(w.AgentOk));
        Assert.True(r.IsSuccess, r.Message);

        using var db2 = w.Ctx();
        var row = db2.AgentReferrals.Single();
        Assert.Equal(AgentReferral.StatusNew, row.Status);
        Assert.Equal(w.ClinicA1, row.ClinicId);                      // from the agent, never the client
        Assert.Equal("HASSAN RAZA", row.Name);
        Assert.Equal("3337770199", row.MobileNumber);
        Assert.Equal(new DateTime(2025, 3, 14), row.DOB);
        Assert.Equal(usersBefore, db2.Users.Count());                // no parent user yet
        Assert.Equal(childsBefore, db2.Childs.Count());              // no child, no schedule
        Assert.Empty(db2.Schedules);
        var n = db2.Notifications.Single();
        Assert.Equal("AgentReferral", n.Type);
        Assert.Equal(w.DoctorA, n.RecipientId);
    }

    [Theory]
    [InlineData("14-03-2025")]
    [InlineData("2025-03-14")]
    public void Post_AcceptsBothDobFormats(string dob)
    {
        using var w = new World();
        Assert.True(Send(w, w.Dto(w.AgentOk, dob: dob)).IsSuccess);
        using var db = w.Ctx();
        Assert.Equal(new DateTime(2025, 3, 14), db.AgentReferrals.Single().DOB);
    }

    [Fact]
    public void Post_Rejects_AgentWithoutAnyPermissionFlag_AndAgentWithoutClinic()
    {
        using var w = new World();
        Assert.False(Send(w, w.Dto(w.AgentNoFlags)).IsSuccess);
        Assert.False(Send(w, w.Dto(w.AgentNoClinic)).IsSuccess);
        Assert.False(Send(w, w.Dto(9999)).IsSuccess);                // unknown agent
        using var db = w.Ctx();
        Assert.Empty(db.AgentReferrals);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public void Post_Rejects_BadInput_WithoutSavingAnything()
    {
        using var w = new World();
        var cases = new List<AgentReferralCreateDTO>();
        AgentReferralCreateDTO D(Action<AgentReferralCreateDTO> edit) { var d = w.Dto(w.AgentOk); edit(d); cases.Add(d); return d; }
        D(d => d.Name = "  ");
        D(d => d.FatherName = "");
        D(d => d.Gender = "");
        D(d => d.Gender = "Other");
        D(d => d.DOB = "not a date");
        D(d => d.DOB = "31-02-2025");
        D(d => d.DOB = "14-03-2999");                                // future
        D(d => d.MobileNumber = "123");                              // fails PK phone rule
        D(d => d.Email = "no-at-sign");
        foreach (var d in cases) Assert.False(Send(w, d).IsSuccess, $"should reject: {d.Name}|{d.Gender}|{d.DOB}|{d.MobileNumber}|{d.Email}");
        using var db = w.Ctx();
        Assert.Empty(db.AgentReferrals);
    }

    [Fact]
    public void Post_EmailAndNoteAreOptional_NoteIsTrimmedTo200_BlankEmailStoredAsNull()
    {
        using var w = new World();
        var d = w.Dto(w.AgentOk);
        d.Email = "  "; d.Note = new string('x', 500);
        Assert.True(Send(w, d).IsSuccess);
        using var db = w.Ctx();
        var row = db.AgentReferrals.Single();
        Assert.Null(row.Email);
        Assert.Equal(200, row.Note!.Length);
    }

    [Fact]
    public void Post_BlocksPatientAlreadyRegisteredAtThisDoctor_ButNotAtAnotherDoctorsPractice()
    {
        using var w = new World();
        w.Child(w.ClinicA2);                                         // same doctor, other clinic: still blocked
        var blocked = Send(w, w.Dto(w.AgentOk));
        Assert.False(blocked.IsSuccess);
        Assert.Contains("already registered", blocked.Message);

        using var w2 = new World();
        w2.Child(w2.ClinicB);                                        // a different doctor's patient: no leak, no block
        Assert.True(Send(w2, w2.Dto(w2.AgentOk)).IsSuccess);
    }

    [Fact]
    public void Post_SameAgentOpenDuplicateBlocked_OtherAgentAllowed_ResubmitAfterRejectAllowed()
    {
        using var w = new World();
        var first = Send(w, w.Dto(w.AgentOk));
        Assert.True(first.IsSuccess);
        Assert.False(Send(w, w.Dto(w.AgentOk)).IsSuccess);           // same agent, same patient, still open
        Assert.True(Send(w, w.Dto(w.AgentOk2)).IsSuccess);           // another agent may also refer

        using (var db = w.Ctx())
        {
            var r = db.AgentReferrals.Single(x => x.Id == NewId(first));
            r.Status = AgentReferral.StatusRejected; r.RejectReason = "Wrong number";
            db.SaveChanges();
        }
        Assert.True(Send(w, w.Dto(w.AgentOk)).IsSuccess);            // "please resubmit" works
    }

    // ------------------------------------------------------------ staff: queue and match

    [Fact]
    public void Queue_ListsEveryClinicOfTheDoctor_OldestFirst_AndNeverAnotherDoctorsReferrals()
    {
        using var w = new World();
        Send(w, w.Dto(w.AgentOk, "3337770001", name: "First"));
        Send(w, w.Dto(w.AgentOk, "3337770002", name: "Second"));
        using (var db = w.Ctx())
        {
            // a referral in doctor A's second clinic, and one in doctor B's clinic
            db.AgentReferrals.Add(new AgentReferral { AgentId = w.AgentOk, ClinicId = w.ClinicA2, Name = "OTHERCLINIC", FatherName = "F", Gender = "Boy", DOB = new DateTime(2025, 1, 1), CountryCode = "92", MobileNumber = "3337770003", CreatedAt = DateTime.UtcNow });
            db.AgentReferrals.Add(new AgentReferral { AgentId = w.AgentOk, ClinicId = w.ClinicB, Name = "FOREIGN", FatherName = "F", Gender = "Boy", DOB = new DateTime(2025, 1, 1), CountryCode = "92", MobileNumber = "3337770004", CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }
        using var db2 = w.Ctx();
        var res = new ReferralController(db2).GetDoctorReferrals(w.DoctorA, "New");
        Assert.True(res.IsSuccess);
        var names = Rows(P(res.ResponseData!, "Items")).Select(r => (string)P(r, "Name")).ToList();
        Assert.Equal(new[] { "FIRST", "SECOND", "OTHERCLINIC" }, names.Select(n => n.ToUpper()).ToArray());
        Assert.DoesNotContain("FOREIGN", names);
        Assert.Equal(2, Rows(P(res.ResponseData!, "Clinics")).Count());      // only doctor A's clinics offered
    }

    [Fact]
    public void Queue_FlagsDuplicatesBetweenAgents_FirstReferrerMarked()
    {
        using var w = new World();
        Send(w, w.Dto(w.AgentOk));
        Send(w, w.Dto(w.AgentOk2));
        using var db = w.Ctx();
        var rows = Rows(P(new ReferralController(db).GetDoctorReferrals(w.DoctorA, "New").ResponseData!, "Items")).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True((bool)P(r, "IsDuplicate")));
        Assert.Single(rows, r => (bool)P(r, "IsFirstReferrer"));
    }

    [Fact]
    public void Match_ReturnsEarliestOpenReferral_NullForOtherDoctorBadPhoneOrClosed()
    {
        using var w = new World();
        var first = Send(w, w.Dto(w.AgentOk));
        Send(w, w.Dto(w.AgentOk2));
        using var db = w.Ctx();
        var c = new ReferralController(db);

        var hit = c.Match(w.DoctorA, "92", "3337770199", "2025-03-14").ResponseData!;
        Assert.Equal(NewId(first), (long)P(hit, "Id"));              // earliest wins

        Assert.Null(c.Match(w.DoctorB, "92", "3337770199", "2025-03-14").ResponseData);   // other practice
        Assert.Null(c.Match(w.DoctorA, "92", "123", "2025-03-14").ResponseData);          // invalid phone
        Assert.Null(c.Match(w.DoctorA, "92", "3337770199", "garbage").ResponseData);      // invalid dob
        Assert.Null(c.Match(w.DoctorA, "92", "3337770199", "2025-03-15").ResponseData);   // other dob
    }

    // ------------------------------------------------------------ staff: convert

    [Fact]
    public void Convert_MarksConverted_LinksChild_StampsAgent_AndIsIdempotent()
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        var child = w.Child(w.ClinicA1);                             // what Add Patient saved (AgentId not stamped here)

        using (var db = w.Ctx())
        {
            var r = new ReferralController(db).Convert(id, new ReferralConvertDTO { ChildId = child });
            Assert.True(r.IsSuccess, r.Message);
        }
        using (var db = w.Ctx())
        {
            var row = db.AgentReferrals.Single();
            Assert.Equal(AgentReferral.StatusConverted, row.Status);
            Assert.Equal(child, row.ChildId);
            Assert.NotNull(row.ResolvedAt);
            Assert.Equal(w.AgentOk, db.Childs.Single(c => c.Id == child).AgentId);
        }
        using (var db = w.Ctx())                                     // second call: success, nothing changes
        {
            var again = new ReferralController(db).Convert(id, new ReferralConvertDTO { ChildId = child });
            Assert.True(again.IsSuccess);
        }
        using var db3 = w.Ctx();
        Assert.Equal(1, db3.AgentReferrals.Count(x => x.Status == AgentReferral.StatusConverted));
    }

    [Fact]
    public void Convert_FirstReferrerWins_EvenWhenStaffClickedTheLaterOne()
    {
        using var w = new World();
        var early = NewId(Send(w, w.Dto(w.AgentOk)));
        var late = NewId(Send(w, w.Dto(w.AgentOk2)));
        var child = w.Child(w.ClinicA1, agentId: w.AgentOk2);        // Add Patient stamped the LATER agent

        using (var db = w.Ctx())
            Assert.True(new ReferralController(db).Convert(late, new ReferralConvertDTO { ChildId = child }).IsSuccess);

        using var db2 = w.Ctx();
        Assert.Equal(AgentReferral.StatusConverted, db2.AgentReferrals.Single(x => x.Id == early).Status);
        var loser = db2.AgentReferrals.Single(x => x.Id == late);
        Assert.Equal(AgentReferral.StatusRejected, loser.Status);
        Assert.StartsWith("Duplicate", loser.RejectReason);
        Assert.Equal(w.AgentOk, db2.Childs.Single(c => c.Id == child).AgentId);   // credit corrected to the first
    }

    [Fact]
    public void Convert_DoesNotTakeAChildAwayFromAnUnrelatedAgent()
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        var child = w.Child(w.ClinicA1, agentId: w.AgentNoFlags);    // already someone else's referral by another path
        using (var db = w.Ctx())
            Assert.True(new ReferralController(db).Convert(id, new ReferralConvertDTO { ChildId = child }).IsSuccess);
        using var db2 = w.Ctx();
        Assert.Equal(w.AgentNoFlags, db2.Childs.Single(c => c.Id == child).AgentId);
    }

    [Fact]
    public void Convert_Rejects_RejectedReferral_MissingChild_UnknownReferral()
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        var child = w.Child(w.ClinicA1);
        using (var db = w.Ctx())
        {
            var c = new ReferralController(db);
            Assert.False(c.Convert(id, new ReferralConvertDTO { ChildId = 0 }).IsSuccess);
            Assert.False(c.Convert(id, new ReferralConvertDTO { ChildId = 99999 }).IsSuccess);
            Assert.False(c.Convert(99999, new ReferralConvertDTO { ChildId = child }).IsSuccess);
            Assert.True(c.Reject(id, new ReferralRejectDTO { Reason = "Did not come" }).IsSuccess);
            Assert.False(c.Convert(id, new ReferralConvertDTO { ChildId = child }).IsSuccess);
        }
        using var db2 = w.Ctx();
        Assert.Equal(AgentReferral.StatusRejected, db2.AgentReferrals.Single().Status);
        Assert.Null(db2.Childs.Single(c => c.Id == child).AgentId);
    }

    // ------------------------------------------------------------ staff: reject / move

    [Fact]
    public void Reject_NeedsReason_AndOnlyOpenReferrals()
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        using var db = w.Ctx();
        var c = new ReferralController(db);
        Assert.False(c.Reject(id, new ReferralRejectDTO { Reason = "  " }).IsSuccess);
        Assert.True(c.Reject(id, new ReferralRejectDTO { Reason = "Wrong number" }).IsSuccess);
        Assert.False(c.Reject(id, new ReferralRejectDTO { Reason = "Did not come" }).IsSuccess);   // already closed
        Assert.Equal("Wrong number", db.AgentReferrals.Single().RejectReason);
    }

    [Fact]
    public void ChangeClinic_WorksWithinTheSameDoctor_RefusesAnotherDoctorsClinic()
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        using (var db = w.Ctx())
        {
            var c = new ReferralController(db);
            Assert.False(c.ChangeClinic(id, new ReferralClinicDTO { ClinicId = w.ClinicB }).IsSuccess);
            Assert.False(c.ChangeClinic(id, new ReferralClinicDTO { ClinicId = 99999 }).IsSuccess);
            Assert.True(c.ChangeClinic(id, new ReferralClinicDTO { ClinicId = w.ClinicA2 }).IsSuccess);
        }
        using var db2 = w.Ctx();
        Assert.Equal(w.ClinicA2, db2.AgentReferrals.Single().ClinicId);
    }

    // ------------------------------------------------------------ practice isolation (identity set)

    [Fact]
    public void AnotherDoctorsStaff_CannotReadConvertRejectOrMoveAReferral()
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        var child = w.Child(w.ClinicA1);
        var foreignDoctor = new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = w.DoctorB };
        As(foreignDoctor, () =>
        {
            using var db = w.Ctx();
            var c = new ReferralController(db);
            Assert.False(c.GetOne(id).IsSuccess);
            Assert.False(c.Convert(id, new ReferralConvertDTO { ChildId = child }).IsSuccess);
            Assert.False(c.Reject(id, new ReferralRejectDTO { Reason = "Other: x" }).IsSuccess);
            Assert.False(c.ChangeClinic(id, new ReferralClinicDTO { ClinicId = w.ClinicB }).IsSuccess);
        });
        using var db2 = w.Ctx();
        Assert.Equal(AgentReferral.StatusNew, db2.AgentReferrals.Single().Status);
        Assert.Equal(w.ClinicA1, db2.AgentReferrals.Single().ClinicId);
    }

    [Theory]
    [InlineData("DOCTOR")]
    [InlineData("PA")]
    [InlineData("MANAGER")]
    public void OwnPracticeStaff_Doctor_Pa_Manager_CanWorkTheReferral(string role)
    {
        using var w = new World();
        var id = NewId(Send(w, w.Dto(w.AgentOk)));
        var child = w.Child(w.ClinicA1);
        var staff = new AuthIdentity { UserId = 7, Role = role, DoctorId = w.DoctorA, PaId = role == "PA" ? 3 : null, ManagerId = role == "MANAGER" ? 4 : null };
        As(staff, () =>
        {
            using var db = w.Ctx();
            var c = new ReferralController(db);
            Assert.True(c.GetOne(id).IsSuccess);
            Assert.True(c.Convert(id, new ReferralConvertDTO { ChildId = child }).IsSuccess);
        });
        using var db2 = w.Ctx();
        var row = db2.AgentReferrals.Single();
        Assert.Equal(role, row.ResolvedByRole);
    }

    // ------------------------------------------------------------ agent: My Patients

    [Fact]
    public void AgentPatients_ShowsStages_PlainRejectReasons_ConvertedOnce_AndLegacyChildren()
    {
        using var w = new World();
        var referred = NewId(Send(w, w.Dto(w.AgentOk, "3330000001", name: "Referred One")));
        var registered = NewId(Send(w, w.Dto(w.AgentOk, "3330000002", name: "Registered Two")));
        var billed = NewId(Send(w, w.Dto(w.AgentOk, "3330000003", name: "Billed Three")));
        var rejectedWrong = NewId(Send(w, w.Dto(w.AgentOk, "3330000004", name: "Wrong Four")));
        var rejectedDup = NewId(Send(w, w.Dto(w.AgentOk, "3330000005", name: "Dup Five")));
        var legacy = w.Child(w.ClinicA1, "3330000006", w.AgentOk);   // registered under the agent without a referral

        var childReg = w.Child(w.ClinicA1, "3330000002");
        var childBill = w.Child(w.ClinicA1, "3330000003");
        using (var db = w.Ctx())
        {
            var c = new ReferralController(db);
            Assert.True(c.Convert(registered, new ReferralConvertDTO { ChildId = childReg }).IsSuccess);
            Assert.True(c.Convert(billed, new ReferralConvertDTO { ChildId = childBill }).IsSuccess);
            Assert.True(c.Reject(rejectedWrong, new ReferralRejectDTO { Reason = "Wrong number" }).IsSuccess);
            var dup = db.AgentReferrals.Single(x => x.Id == rejectedDup);
            dup.Status = AgentReferral.StatusRejected; dup.RejectReason = "Duplicate — first referrer applies";
            db.SaveChanges();

            var vaccine = new Vaccine { Name = "V", Validity = 12 }; db.Vaccines.Add(vaccine); db.SaveChanges();
            var dose = new Dose { Name = "D", VaccineId = vaccine.Id, DoseOrder = 1, MinAge = 0 }; db.Doses.Add(dose); db.SaveChanges();
            var inv = new InvoiceSubmission { ChildId = childBill, DoctorId = w.DoctorA, InvoiceDate = DateTime.UtcNow.Date, SubmittedAt = DateTime.UtcNow, TotalAmount = 100 };
            db.InvoiceSubmissions.Add(inv); db.SaveChanges();
            db.Schedules.Add(new Schedule { ChildId = childBill, DoseId = dose.Id, Date = DateTime.UtcNow.Date, IsDone = true, InvoiceSubmissionId = inv.Id });
            db.SaveChanges();
        }

        using var db2 = w.Ctx();
        var rows = Rows(new ReferralController(db2).GetAgentPatients(w.AgentOk, null).ResponseData!).ToList();
        string Stage(string name) => (string)P(rows.Single(r => ((string)P(r, "Name")).StartsWith(name, StringComparison.OrdinalIgnoreCase)), "Stage");

        Assert.Equal("Referred", Stage("Referred One"));
        Assert.Equal("Registered", Stage("Registered Two"));
        Assert.Equal("Billed", Stage("Billed Three"));
        Assert.Equal("Not registered", Stage("Wrong Four"));
        Assert.Equal("Not registered", Stage("Dup Five"));
        Assert.Equal("Referred", (string)P(rows.First(), "Stage"));                        // pending is listed first

        // a converted referral appears once, as its referral row, never twice
        Assert.Equal(1, rows.Count(r => ((string)P(r, "Name")).StartsWith("Registered Two", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, rows.Count(r => ((string)P(r, "Name")).StartsWith("Billed Three", StringComparison.OrdinalIgnoreCase)));
        // the legacy child (no referral) is still listed
        Assert.Contains(rows, r => (string)P(r, "Source") == "child" && (long)P(r, "ChildId") == legacy);

        string Reason(string name) => (string)P(rows.Single(r => ((string)P(r, "Name")).StartsWith(name, StringComparison.OrdinalIgnoreCase)), "Reason");
        Assert.Equal("Wrong number. Please resubmit with the correct one.", Reason("Wrong Four"));
        var dupText = Reason("Dup Five");
        Assert.DoesNotContain("first referrer", dupText, StringComparison.OrdinalIgnoreCase);   // never reveal the other agent
        Assert.DoesNotContain("another agent", dupText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentPatients_OnlyShowsThatAgentsOwnReferralsAndChildren()
    {
        using var w = new World();
        Send(w, w.Dto(w.AgentOk, "3330000011", name: "Mine"));
        Send(w, w.Dto(w.AgentOk2, "3330000012", name: "Theirs"));
        w.Child(w.ClinicA1, "3330000013", w.AgentOk2);
        using var db = w.Ctx();
        var rows = Rows(new ReferralController(db).GetAgentPatients(w.AgentOk, null).ResponseData!).ToList();
        Assert.Single(rows);
        Assert.Equal("MINE", ((string)P(rows[0], "Name")).ToUpper());
    }

    [Fact]
    public void AgentPatients_SearchFiltersByNameGuardianOrMobile()
    {
        using var w = new World();
        Send(w, w.Dto(w.AgentOk, "3330000021", name: "Alpha"));
        Send(w, w.Dto(w.AgentOk, "3330000022", name: "Beta"));
        using var db = w.Ctx();
        var c = new ReferralController(db);
        Assert.Single(Rows(c.GetAgentPatients(w.AgentOk, "alph").ResponseData!));
        Assert.Single(Rows(c.GetAgentPatients(w.AgentOk, "3330000022").ResponseData!));
        Assert.Equal(2, Rows(c.GetAgentPatients(w.AgentOk, "raza").ResponseData!).Count());     // guardian "RAZA ABBAS"
        Assert.Empty(Rows(c.GetAgentPatients(w.AgentOk, "nomatch").ResponseData!));
    }

    // ------------------------------------------------------------ regression: old flow untouched

    [Fact]
    public void OldAgentRegisterEndpointAndItsPermissionFlags_AreStillPresent()
    {
        // older VacAgent builds still post here until they are retired
        Assert.NotNull(typeof(ChildController).GetMethod("PostAgentChild"));
        Assert.NotNull(typeof(Agent).GetProperty("CanRegisterRegular"));
        Assert.NotNull(typeof(Agent).GetProperty("CanRegisterTravel"));
    }

    // ------------------------------------------------------------ the [Owns]/[RolesOnly] filters themselves
    // Controller methods are called directly above, which skips action filters. These run the real
    // attributes in Enforce mode so an agent cannot act as, or read the patients of, another agent.

    static bool Denied(World w, ActionFilterAttribute filter, AuthIdentity id, IDictionary<string, object?> args)
    {
        var savedMode = AuthContext.Mode; var savedAcc = AuthContext.Accessor;
        AuthContext.Mode = "Enforce";
        try
        {
            using var db = w.Ctx();
            var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services };
            http.Items["auth.identity"] = id;
            AuthContext.Accessor = new HttpContextAccessor { HttpContext = http };
            var actionCtx = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var ctx = new ActionExecutingContext(actionCtx, new List<IFilterMetadata>(), args, new object());
            filter.OnActionExecuting(ctx);
            return ctx.Result != null;
        }
        finally { AuthContext.Mode = savedMode; AuthContext.Accessor = savedAcc; }
    }

    [Fact]
    public void Filter_AnAgentCanOnlyFileReferralsAsThemselves()
    {
        using var w = new World();
        var me = new AuthIdentity { UserId = w.AgentOk, Role = "AGENT" };
        var owns = new OwnsAttribute(OwnerKind.Agent, "AgentId");
        Assert.False(Denied(w, owns, me, new Dictionary<string, object?> { ["dto"] = w.Dto(w.AgentOk) }));
        Assert.True(Denied(w, owns, me, new Dictionary<string, object?> { ["dto"] = w.Dto(w.AgentOk2) }));   // posting as someone else
    }

    [Fact]
    public void Filter_AnAgentCanOnlyReadTheirOwnPatientList()
    {
        using var w = new World();
        var me = new AuthIdentity { UserId = w.AgentOk, Role = "AGENT" };
        var owns = new OwnsAttribute(OwnerKind.Agent, "agentId");
        Assert.False(Denied(w, owns, me, new Dictionary<string, object?> { ["agentId"] = w.AgentOk }));
        Assert.True(Denied(w, owns, me, new Dictionary<string, object?> { ["agentId"] = w.AgentOk2 }));
    }

    [Fact]
    public void Filter_StaffQueueAndMatchAreScopedToTheCallersOwnDoctor()
    {
        using var w = new World();
        var docA = new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = w.DoctorA };
        var paA = new AuthIdentity { UserId = 2, Role = "PA", DoctorId = w.DoctorA, PaId = 5 };
        var owns = new OwnsAttribute(OwnerKind.Doctor, "doctorId");
        Assert.False(Denied(w, owns, docA, new Dictionary<string, object?> { ["doctorId"] = w.DoctorA }));
        Assert.False(Denied(w, owns, paA, new Dictionary<string, object?> { ["doctorId"] = w.DoctorA }));
        Assert.True(Denied(w, owns, docA, new Dictionary<string, object?> { ["doctorId"] = w.DoctorB }));
        Assert.True(Denied(w, owns, paA, new Dictionary<string, object?> { ["doctorId"] = w.DoctorB }));
    }

    [Theory]
    [InlineData("AGENT", true)]
    [InlineData("PARENT", true)]
    [InlineData("DOCTOR", false)]
    [InlineData("PA", false)]
    [InlineData("MANAGER", false)]
    public void Filter_StaffOnlyEndpointsRefuseAgentsAndParents(string role, bool expectedDenied)
    {
        using var w = new World();
        var id = new AuthIdentity { UserId = 3, Role = role, DoctorId = role == "AGENT" || role == "PARENT" ? null : w.DoctorA };
        Assert.Equal(expectedDenied, Denied(w, new RolesOnlyAttribute("DOCTOR", "PA", "MANAGER", "SUPERADMIN"), id, new Dictionary<string, object?>()));
    }

    [Fact]
    public void Filter_AgentOnlyEndpointsRefuseParentsAndStaffCannotFileReferrals()
    {
        using var w = new World();
        var filter = new RolesOnlyAttribute("AGENT", "SUPERADMIN");
        Assert.False(Denied(w, filter, new AuthIdentity { UserId = 1, Role = "AGENT" }, new Dictionary<string, object?>()));
        Assert.True(Denied(w, filter, new AuthIdentity { UserId = 1, Role = "PARENT" }, new Dictionary<string, object?>()));
        Assert.True(Denied(w, filter, new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = w.DoctorA }, new Dictionary<string, object?>()));
    }
}
