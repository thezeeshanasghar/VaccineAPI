using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Shared server-side permission gate for the stock-mutating controllers (AdjustStock,
    // Bill, StockTransfer, DirectSale, Stock opening balance).
    //
    // Who is acting comes from the session token, never from the request body: the body's
    // PaId/ManagerId/CallerUserId are only used while the API is still in Log mode and an older
    // app sends no token. With a token:
    //   DOCTOR   always allowed, but only for their own DoctorId.
    //   PA       allowed when the matching PaPermission flag is on, for their own doctor.
    //   MANAGER  blocked outright (no ManagerPermission flag or PA-in-the-loop exists for stock).
    //   others   (parent, agent) blocked.
    public static class StockActionGuard
    {
        public static (bool allowed, string? error) CheckStockAction(
            Context db, long? paId, long? managerId, long? callerUserId, string? securityStamp,
            System.Func<PaPermission, bool> paFlagSelector, string actionLabel, long? doctorId = null)
        {
            var tok = AuthContext.Current;
            if (tok != null)
            {
                if (tok.Role != "DOCTOR" && tok.Role != "PA" && tok.Role != "MANAGER" && tok.Role != "SUPERADMIN")
                    return (false, "You do not have access to this.");
                if (doctorId.HasValue && !CallerGuard.OwnsDoctor(doctorId.Value))
                    return (false, "This record belongs to another practice.");

                // Identity comes from the token; whatever the body claims is ignored.
                paId = tok.PaId;
                managerId = tok.ManagerId;
                callerUserId = tok.UserId;
            }
            else if (AuthContext.Enforcing)
            {
                return (false, "Session could not be verified. Please sign in again.");
            }

            if (!paId.HasValue && !managerId.HasValue)
            {
                // No token (Log mode, older app): a missing PaId/ManagerId still reads as "doctor".
                // With a token this means the caller really is a doctor (or super admin).
                return (true, null);
            }

            if (managerId.HasValue)
                return (false, $"Managers cannot {actionLabel}. Ask the doctor.");

            if (!CallerGuard.VerifyCaller(db, callerUserId, securityStamp))
                return (false, "Session could not be verified. Please sign in again.");

            var pa = db.PersonalAssistant.Find(paId!.Value);
            if (pa == null || pa.UserId != callerUserId)
                return (false, "Caller does not match the assigned PA.");

            var perm = db.PaPermissions.FirstOrDefault(p => p.PaId == paId.Value);
            if (perm == null || !paFlagSelector(perm))
                return (false, $"You do not have permission to {actionLabel}. Ask the doctor to enable this.");

            return (true, null);
        }
    }
}
