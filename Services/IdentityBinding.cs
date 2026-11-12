using System;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Services
{
    // PA_REVERSAL_SPEC.md R11 / PA accountability I1: who acted comes from the signed token, never from a
    // field the client may omit. When the caller's token says PA (or MANAGER), the server writes that id
    // into the request before any logic reads it, so "no PaId in the body" can no longer turn a PA into
    // "the doctor" (the Sidra sequence). A body id that disagrees with the token is replaced and recorded.
    // A DOCTOR token is left alone: a doctor acting on a PA's visit is a legitimate request shape.
    // Does nothing unless the identity.bind flag is on, and a request with no token is untouched (Log mode).
    public static class IdentityBinding
    {
        public const string Flag = "identity.bind";

        // Returns what was changed, or null when nothing was. Never throws: it must not break the action.
        public static string? Bind(Context db, ScheduleDTO dto)
        {
            try
            {
                if (dto == null || !FeatureFlags.IsOn(db, Flag)) return null;
                var id = AuthContext.Current;
                if (id == null) return null;

                string? note = null;
                var claimedPa = dto.PaId;

                if (id.Role == "PA" && id.PaId.HasValue)
                {
                    if (dto.PaId != id.PaId)
                    {
                        note = dto.PaId.HasValue
                            ? "body PaId " + dto.PaId + " replaced by token PA " + id.PaId
                            : "PaId filled from token PA " + id.PaId;
                        dto.PaId = id.PaId;
                    }
                    if (dto.ManagerId.HasValue)
                    {
                        note = (note == null ? "" : note + "; ") + "body ManagerId " + dto.ManagerId + " dropped (token is a PA)";
                        dto.ManagerId = null;
                    }
                }
                else if (id.Role == "MANAGER" && id.ManagerId.HasValue)
                {
                    if (dto.ManagerId != id.ManagerId)
                    {
                        note = dto.ManagerId.HasValue
                            ? "body ManagerId " + dto.ManagerId + " replaced by token manager " + id.ManagerId
                            : "ManagerId filled from token manager " + id.ManagerId;
                        dto.ManagerId = id.ManagerId;
                    }
                    if (dto.PaId.HasValue)
                    {
                        note = (note == null ? "" : note + "; ") + "body PaId " + dto.PaId + " dropped (token is a manager)";
                        dto.PaId = null;
                    }
                }

                if (note != null)
                    FinancialAudit.Stamp(db, "IdentityBind", doctorId: dto.DoctorId > 0 ? dto.DoctorId : (long?)null,
                        childId: dto.ChildId > 0 ? dto.ChildId : (long?)null, scheduleId: dto.Id > 0 ? dto.Id : (long?)null,
                        claimedPaId: claimedPa, detail: new { note });
                return note;
            }
            catch { return null; }
        }
    }
}
