using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Shared "is this caller actually allowed to touch this clinic" check, used by every
    // alert-fetch/send endpoint (Schedule, FollowUp, Birthday) so a PA/doctor can't view or
    // send alerts for a clinic they aren't assigned to. Both paId/doctorId are optional so
    // existing callers that omit them keep prior (unrestricted) behavior until every
    // frontend call site is updated to pass one.
    //
    // Not a general auth system — see project notes: this app has no server-side session
    // identity verification anywhere, so this only closes the specific clinic-scoping gap.
    public static class ClinicAccessGuard
    {
        public static bool CallerOwnsClinic(Context db, long clinicId, long? paId, long? doctorId)
        {
            if (paId.HasValue)
            {
                return db.PaAccess.Any(pa => pa.PersonalAssistantId == paId.Value && pa.ClinicId == clinicId);
            }
            if (doctorId.HasValue)
            {
                return db.Clinics.Any(c => c.Id == clinicId && c.DoctorId == doctorId.Value);
            }
            return true;
        }
    }
}
