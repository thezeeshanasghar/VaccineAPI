using System.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // The PA / manager apps send no DoctorId on schedule calls, so ScheduleDTO.DoctorId arrives as 0 and every
    // PaActivityLog written from it was stored with DoctorId = 0 (prod 2026-11-24: all 40 pending ungive
    // requests). The doctor's pending list and every approve path filter by doctor, so none were ever shown.
    // The doctor is never taken from the client when we can read it from the actor's own record.
    public static class PaDoctor
    {
        // supplied is used only when the actor's record gives nothing; it is never trusted over the record.
        public static long Resolve(Context db, long? paId, long? managerId, long supplied)
        {
            if (paId.HasValue && paId.Value > 0)
            {
                var d = db.PersonalAssistant.Where(p => p.Id == paId.Value).Select(p => p.DoctorId).FirstOrDefault();
                if (d > 0) return d;
            }
            if (managerId.HasValue && managerId.Value > 0)
            {
                var d = db.Manager.Where(m => m.Id == managerId.Value).Select(m => m.DoctorId).FirstOrDefault();
                if (d > 0) return d;
            }
            return supplied;
        }

        // Effective doctor of an existing row: its own DoctorId, else its PA's / manager's doctor (rows written
        // before the writer fix, until the backfill has run).
        public static long Effective(Context db, PaActivityLog l) =>
            l.DoctorId > 0 ? l.DoctorId : Resolve(db, l.PaId, l.ManagerId, 0);
    
        // Rows that belong to this doctor: stamped with it, or (legacy rows with DoctorId 0) written by one of
        // its PAs / managers.
        public static IQueryable<PaActivityLog> ForDoctor(Context db, IQueryable<PaActivityLog> q, long doctorId) =>
            q.Where(l => l.DoctorId == doctorId
                      || (l.DoctorId == 0
                          && (db.PersonalAssistant.Any(p => p.Id == l.PaId && p.DoctorId == doctorId)
                              || db.Manager.Any(m => m.Id == l.ManagerId && m.DoctorId == doctorId))));
    }
}
