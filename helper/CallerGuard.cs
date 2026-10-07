using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Shared static form of the VerifyCaller check duplicated privately in ScheduleController
    // and PAAssignmentController.
    //
    // When the request carries a valid session token (see AuthMiddleware) the token IS the proof of
    // identity and the ids/stamp in the body are only cross-checked against it. While the API is
    // still in Log mode, an older app that sends no token falls back to the stamp check.
    public static class CallerGuard
    {
        public static bool VerifyCaller(Context db, long? userId, string? securityStamp)
        {
            var id = AuthContext.Current;
            if (id != null)
                return id.Role != "AGENT" && userId.HasValue && id.UserId == userId.Value;
            if (AuthContext.Enforcing)
                return false;

            if (!userId.HasValue || string.IsNullOrEmpty(securityStamp))
                return false;

            var user = db.Users.Find(userId.Value);
            return user != null && user.SecurityStamp == securityStamp;
        }

        // Does this doctor belong to the caller's own practice? True for the super admin.
        // Without a token: true only while the API is in Log mode.
        public static bool OwnsDoctor(long doctorId)
        {
            var id = AuthContext.Current;
            if (id == null) return !AuthContext.Enforcing;
            if (id.Role == "SUPERADMIN") return true;
            return (id.Role == "DOCTOR" || id.Role == "PA" || id.Role == "MANAGER") && id.DoctorId == doctorId;
        }

        public static bool OwnsClinic(Context db, long clinicId)
        {
            var id = AuthContext.Current;
            if (id == null) return !AuthContext.Enforcing;
            if (id.Role == "SUPERADMIN") return true;
            if (id.Role != "DOCTOR" && id.Role != "PA" && id.Role != "MANAGER") return false;
            return db.Clinics.Any(c => c.Id == clinicId && c.DoctorId == id.DoctorId);
        }

        // A staff member may touch children of their own doctor's clinics; a parent only their own child.
        public static bool OwnsChild(Context db, long childId)
        {
            var id = AuthContext.Current;
            if (id == null) return !AuthContext.Enforcing;
            if (id.Role == "SUPERADMIN") return true;

            if (id.Role == "PARENT")
                return db.Childs.Any(c => c.Id == childId && c.UserId == id.UserId);
            if (id.Role == "DOCTOR" || id.Role == "PA" || id.Role == "MANAGER")
                return db.Childs.Any(c => c.Id == childId && c.Clinic.DoctorId == id.DoctorId);
            if (id.Role == "AGENT")
                return db.Childs.Any(c => c.Id == childId && (c.AgentId == id.UserId || c.AddedByAgentId == id.UserId));
            return false;
        }
    }
}
