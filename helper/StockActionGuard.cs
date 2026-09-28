using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Shared server-side permission gate for the stock-mutating controllers (AdjustStock,
    // Bill, StockTransfer, DirectSale). These previously had zero identity or permission
    // check at all — any caller could POST/DELETE against them for any DoctorId/ClinicId
    // supplied in the body, and the PA flags meant to gate them (StockAdjust,
    // StockPurchaseBills, StockTransfer, StockDirectSale) were written by the settings
    // screen but read nowhere else. See the 2026-09-28 stock audit and
    // project_give_ungive_permission_enforcement for the matching fix on give/ungive.
    //
    // A Doctor caller (paId/managerId both null) is always allowed — doctors have no
    // permission flags to check. Manager is blocked outright for all 4 of these actions:
    // unlike give/ungive (which requires an assigned PA to carry cash-reconciliation
    // responsibility), there is no ManagerPermission flag and no PA-in-the-loop mechanism
    // for stock adjustment/purchasing/transfers/direct-sale, so Manager stays fully out —
    // consistent with the "Manager never touches cash" principle for everything except the
    // PA-gated give/ungive exception (see feedback_manager_give_vaccine_intentional).
    public static class StockActionGuard
    {
        public static (bool allowed, string? error) CheckStockAction(
            Context db, long? paId, long? managerId, long? callerUserId, string? securityStamp,
            System.Func<PaPermission, bool> paFlagSelector, string actionLabel)
        {
            if (!paId.HasValue && !managerId.HasValue)
                return (true, null); // doctor actor — no flags apply

            if (managerId.HasValue)
                return (false, $"Managers cannot {actionLabel}. Ask the doctor.");

            if (!callerUserId.HasValue || string.IsNullOrEmpty(securityStamp))
                return (false, "Session could not be verified. Please sign in again.");

            var user = db.Users.Find(callerUserId.Value);
            if (user == null || user.SecurityStamp != securityStamp)
                return (false, "Session could not be verified. Please sign in again.");

            var pa = db.PersonalAssistant.Find(paId!.Value);
            if (pa == null || pa.UserId != user.Id)
                return (false, "Caller does not match the assigned PA.");

            var perm = db.PaPermissions.FirstOrDefault(p => p.PaId == paId.Value);
            if (perm == null || !paFlagSelector(perm))
                return (false, $"You do not have permission to {actionLabel}. Ask the doctor to enable this.");

            return (true, null);
        }
    }
}
