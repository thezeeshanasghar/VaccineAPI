using System.Linq;
using Microsoft.AspNetCore.Http;
using VaccineAPI;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases
{
    [Collection("AuthStatics")]
    public class FinancialAuditStampTests
    {
        private static void SetFlag(Context db, bool on)
        {
            db.FeatureFlags.Add(new FeatureFlag { FlagKey = FinancialAudit.Flag, IsOn = on });
            db.SaveChanges();
            FeatureFlags.ResetCache();
        }

        private static void AsCaller(AuthIdentity? id)
        {
            var ctx = new DefaultHttpContext();
            if (id != null) ctx.Items["auth.identity"] = id;
            AuthContext.Accessor = new HttpContextAccessor { HttpContext = ctx };
        }

        [Fact]
        public void Flag_off_writes_nothing()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            SetFlag(db, false); AsCaller(null);
            FinancialAudit.Stamp(db, "Give", childId: 1);
            db.SaveChanges();
            Assert.Empty(db.FinancialAudit);
        }

        [Fact]
        public void Missing_flag_row_means_off()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            FeatureFlags.ResetCache(); AsCaller(null);
            FinancialAudit.Stamp(db, "Give", childId: 1);
            db.SaveChanges();
            Assert.Empty(db.FinancialAudit);
        }

        [Fact]
        public void Token_identity_is_stamped_and_body_claim_is_kept_separately()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            SetFlag(db, true);
            AsCaller(new AuthIdentity { UserId = 77, Role = "PA", DoctorId = 1, PaId = 19 });
            FinancialAudit.Stamp(db, "Give", doctorId: 1, childId: 5, scheduleId: 9, claimedPaId: 22);
            db.SaveChanges();
            var r = db.FinancialAudit.Single();
            Assert.Equal("token", r.ActorSource);
            Assert.Equal(77, r.ActorUserId);
            Assert.Equal("PA", r.ActorRole);
            Assert.Equal(22, r.ClaimedPaId);   // what the body said is not trusted, only recorded
        }

        [Fact]
        public void No_token_is_never_recorded_as_doctor()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            SetFlag(db, true); AsCaller(null);
            FinancialAudit.Stamp(db, "Give", childId: 5, claimedPaId: 22);
            FinancialAudit.Stamp(db, "Ungive", childId: 5);
            db.SaveChanges();
            var rows = db.FinancialAudit.OrderBy(x => x.Id).ToList();
            Assert.Equal("body", rows[0].ActorSource);
            Assert.Equal("none", rows[1].ActorSource);
            Assert.All(rows, r => { Assert.Null(r.ActorUserId); Assert.Null(r.ActorRole); });
        }
    }

    [CollectionDefinition("AuthStatics", DisableParallelization = true)]
    public class AuthStaticsCollection { }
}
