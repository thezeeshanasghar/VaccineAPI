using VaccineAPI.Models;

namespace VaccineAPI
{
    // Shared static form of the VerifyCaller check duplicated privately in ScheduleController
    // and PAAssignmentController — userId/securityStamp must match a real User row's
    // currently-issued SecurityStamp. For a new call site that doesn't already have its own
    // instance-method copy (e.g. DirectSaleController.Confirm, which previously trusted a raw
    // client-supplied doctorId with no identity check at all).
    public static class CallerGuard
    {
        public static bool VerifyCaller(Context db, long? userId, string? securityStamp)
        {
            if (!userId.HasValue || string.IsNullOrEmpty(securityStamp))
                return false;

            var user = db.Users.Find(userId.Value);
            return user != null && user.SecurityStamp == securityStamp;
        }
    }
}
