using System;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Phase 2 identity stamping. Adds one financialaudit row to the change tracker so it commits in the SAME
    // SaveChanges as the action it describes. Does nothing unless the identity.stamp flag is on, and never
    // changes what the action does: the legacy columns are written exactly as before.
    public static class FinancialAudit
    {
        public const string Flag = "identity.stamp";

        public static void Stamp(Context db, string action, long? doctorId = null, long? clinicId = null, long? childId = null,
            long? scheduleId = null, long? invoiceSubmissionId = null, string? invoiceNumber = null, long? assignmentId = null,
            decimal? amount = null, long? claimedPaId = null, object? detail = null)
        {
            try
            {
                if (!FeatureFlags.IsOn(db, Flag)) return;
                var id = AuthContext.Current;
                var req = AuthContext.Accessor?.HttpContext?.Request;
                string source = id != null ? "token" : (claimedPaId.HasValue ? "body" : "none");
                db.FinancialAudit.Add(new FinancialAuditRow
                {
                    Action = action,
                    DoctorId = doctorId ?? id?.DoctorId,
                    ClinicId = clinicId,
                    ChildId = childId,
                    ScheduleId = scheduleId,
                    InvoiceSubmissionId = invoiceSubmissionId,
                    InvoiceNumber = invoiceNumber,
                    AssignmentId = assignmentId,
                    Amount = amount,
                    ActorUserId = id?.UserId,
                    ActorRole = id?.Role,
                    ActorSource = source,
                    ClaimedPaId = claimedPaId,
                    Endpoint = Trunc(req == null ? null : req.Method + " " + req.Path.Value, 120),
                    ClientVersion = Trunc(req?.Headers["X-Client-Version"].ToString(), 30),
                    Detail = detail == null ? null : JsonConvert.SerializeObject(detail)
                });
            }
            catch { /* auditing must never break the action it describes */ }
        }

        private static string? Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? null : (s.Length <= n ? s : s.Substring(0, n));
    }
}
