using System.Linq;
using Microsoft.AspNetCore.Http;
using VaccineAPI;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases
{
    // PA_REVERSAL_SPEC.md R11: the actor comes from the token, so a PA request without PaId (the Sidra shape)
    // is still attributed to that PA.
    [Collection("AuthStatics")]
    public class IdentityBindingTests
    {
        private static void Flags(Context db, bool bind)
        {
            db.FeatureFlags.Add(new FeatureFlag { FlagKey = IdentityBinding.Flag, IsOn = bind });
            db.FeatureFlags.Add(new FeatureFlag { FlagKey = FinancialAudit.Flag, IsOn = true });
            db.SaveChanges();
            FeatureFlags.ResetCache();
        }

        private static void As(AuthIdentity? id)
        {
            var ctx = new DefaultHttpContext();
            if (id != null) ctx.Items["auth.identity"] = id;
            AuthContext.Accessor = new HttpContextAccessor { HttpContext = ctx };
        }

        private static ScheduleDTO Dto(long? pa = null, long? mgr = null) =>
            new ScheduleDTO { Id = 5, ChildId = 9, DoctorId = 1, PaId = pa, ManagerId = mgr };

        [Fact]
        public void Pa_token_fills_missing_PaId_and_records_it()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = Dto();
            Assert.NotNull(IdentityBinding.Bind(db, d));
            db.SaveChanges();
            Assert.Equal(19, d.PaId);
            var row = db.FinancialAudit.Single(r => r.Action == "IdentityBind");
            Assert.Equal("token", row.ActorSource);
            Assert.Equal(5, row.ScheduleId);
        }

        [Fact]
        public void Pa_token_overrides_a_different_body_PaId_and_keeps_the_claim()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = Dto(pa: 22);
            IdentityBinding.Bind(db, d); db.SaveChanges();
            Assert.Equal(19, d.PaId);
            Assert.Equal(22, db.FinancialAudit.Single(r => r.Action == "IdentityBind").ClaimedPaId);
        }

        [Fact]
        public void Matching_PaId_changes_nothing_and_writes_nothing()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = Dto(pa: 19);
            Assert.Null(IdentityBinding.Bind(db, d)); db.SaveChanges();
            Assert.Empty(db.FinancialAudit);
        }

        [Fact]
        public void Pa_token_drops_a_forged_ManagerId()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = Dto(pa: 19, mgr: 3);
            IdentityBinding.Bind(db, d);
            Assert.Null(d.ManagerId);
            Assert.Equal(19, d.PaId);
        }

        [Fact]
        public void Manager_token_fills_ManagerId_and_drops_PaId()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 8, Role = "MANAGER", ManagerId = 3, DoctorId = 1 });
            var d = Dto(pa: 19);
            IdentityBinding.Bind(db, d);
            Assert.Equal(3, d.ManagerId);
            Assert.Null(d.PaId);
        }

        [Fact]
        public void Doctor_token_is_left_alone()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = 1 });
            var d = Dto(pa: 19);
            Assert.Null(IdentityBinding.Bind(db, d));
            Assert.Equal(19, d.PaId);
        }

        [Fact]
        public void No_token_is_left_alone()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(null);
            var d = Dto();
            Assert.Null(IdentityBinding.Bind(db, d));
            Assert.Null(d.PaId);
        }

        [Fact]
        public void Flag_off_changes_nothing()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, false); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = Dto();
            Assert.Null(IdentityBinding.Bind(db, d));
            Assert.Null(d.PaId);
        }

        [Fact]
        public void Bind_row_takes_ChildId_from_the_schedule_when_the_body_has_none()
        {
            using var w = new StockWorld(); using var db = w.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = new ScheduleDTO { Id = w.Schedule1Id, DoctorId = w.DoctorId };   // the app's body: no ChildId, no PaId
            IdentityBinding.Bind(db, d); db.SaveChanges();
            Assert.Equal(w.ChildId, db.FinancialAudit.Single(r => r.Action == "IdentityBind").ChildId);
        }

        [Fact]
        public void ClaimedPaId_keeps_the_original_body_claim_after_Bind()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flags(db, true); As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var d = Dto(pa: 22);
            IdentityBinding.Bind(db, d);
            Assert.Equal(19, d.PaId);
            Assert.Equal(22, IdentityBinding.ClaimedPaId(d));
        }
    }
}
