using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using VaccineAPI;
using VaccineAPI.Controllers;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases
{
    // PA_REVERSAL_SPEC.md R5 (PA blocks), R11 (rejected attempts are events, bind rows carry the child).
    [Collection("AuthStatics")]
    public class ReversalGuardTests
    {
        private static void Flag(Context db, string key, bool on)
        {
            db.FeatureFlags.Add(new FeatureFlag { FlagKey = key, IsOn = on });
            db.SaveChanges();
            FeatureFlags.ResetCache();
        }

        private static void As(AuthIdentity? id)
        {
            var ctx = new DefaultHttpContext();
            if (id != null) ctx.Items["auth.identity"] = id;
            AuthContext.Accessor = new HttpContextAccessor { HttpContext = ctx };
        }

        private static PAAssignmentController Ctl(Context db) =>
            new PAAssignmentController(db, new InventoryTransactionService(db), new ConfigurationBuilder().Build());

        [Theory]
        [InlineData(true, true, false, true)]    // flag on, invoice -> blocked
        [InlineData(true, false, true, true)]    // flag on, payment collected -> blocked
        [InlineData(true, false, false, false)]  // flag on, nothing paid -> a PA may still ungive
        [InlineData(false, true, true, false)]   // flag off -> today's behaviour
        public void Pa_ungive_block_follows_flag_and_payment(bool flagOn, bool invoice, bool paid, bool blocked)
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flag(db, VisitReversal.FlagV2, flagOn);
            var dose = new Schedule { InvoiceSubmissionId = invoice ? 5 : (long?)null, IsPaymentCollected = paid };
            Assert.Equal(blocked, VisitReversal.PaUngiveBlocked(db, dose));
        }

        [Fact]
        public void Pa_token_cannot_delete_or_reverse_an_assignment_when_flag_on()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flag(db, VisitReversal.FlagV2, true);
            As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            var r = Result.Of(Ctl(db).DeleteAssignment(999, 1, "FullReset").GetAwaiter().GetResult());
            Assert.False(r.IsSuccess);
            Assert.Equal(VisitReversal.PaCannotReverseMsg, r.Message);
        }

        [Fact]
        public void Doctor_token_and_flag_off_are_not_blocked_by_the_pa_guard()
        {
            using var t = new TestDb(); using var db = t.NewContext();
            Flag(db, VisitReversal.FlagV2, true);
            As(new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = 1 });
            Assert.Equal("Assignment not found", Result.Of(Ctl(db).DeleteAssignment(999, 1, "FullReset").GetAwaiter().GetResult()).Message);

            db.FeatureFlags.First(f => f.FlagKey == VisitReversal.FlagV2).IsOn = false; db.SaveChanges(); FeatureFlags.ResetCache();
            As(new AuthIdentity { UserId = 7, Role = "PA", PaId = 19, DoctorId = 1 });
            Assert.Equal("Assignment not found", Result.Of(Ctl(db).DeleteAssignment(999, 1, "FullReset").GetAwaiter().GetResult()).Message);
        }

        [Fact]
        public void A_rejected_give_still_leaves_a_Rejected_event_after_the_rollback()
        {
            using var w = new StockWorld();
            using (var f = w.NewContext())
            {
                f.FeatureFlags.Add(new FeatureFlag { FlagKey = FinancialAudit.Flag, IsOn = true }); f.SaveChanges();
            }
            FeatureFlags.ResetCache();
            try
            {
                As(null);
                var r = w.Give(987654, w.ChildId, (int)w.Dose1Id);        // no such schedule -> refused, transaction rolled back
                Assert.False(r.IsSuccess);
                using var db = w.NewContext();
                var row = Assert.Single(db.FinancialAudit.Where(x => x.Action == "Rejected"));
                Assert.Equal(987654, row.ScheduleId);
                Assert.Contains("Schedule not found", row.Detail);
            }
            finally { FeatureFlags.ResetCache(); }
        }

        [Fact]
        public void A_successful_request_writes_no_Rejected_event()
        {
            using var w = new StockWorld();
            using (var f = w.NewContext())
            {
                f.FeatureFlags.Add(new FeatureFlag { FlagKey = FinancialAudit.Flag, IsOn = true }); f.SaveChanges();
            }
            FeatureFlags.ResetCache();
            try
            {
                As(null);
                Assert.True(w.GiveDose1().IsSuccess);
                using var db = w.NewContext();
                Assert.Empty(db.FinancialAudit.Where(x => x.Action == "Rejected"));
            }
            finally { FeatureFlags.ResetCache(); }
        }
    }
}
