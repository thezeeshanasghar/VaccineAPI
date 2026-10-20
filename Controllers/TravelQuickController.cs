using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    public class TravelQuickItemDTO
    {
        public long DoseId { get; set; }
        public string DoseName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public int SortOrder { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
    [Owns(OwnerKind.Doctor, "doctorId")]
    public class TravelQuickController : ControllerBase
    {
        public const int MaxItems = 4;
        private readonly Context _db;

        public TravelQuickController(Context db)
        {
            _db = db;
        }

        // Empty list when the doctor does not hold the Travel permission, so a stale list never surfaces.
        [HttpGet("{doctorId:long}")]
        public ActionResult<Response<List<TravelQuickItemDTO>>> Get(long doctorId)
        {
            var doctor = _db.Doctors.FirstOrDefault(d => d.Id == doctorId);
            if (doctor == null)
                return NotFound(new Response<List<TravelQuickItemDTO>>(false, "Doctor not found", null));

            var items = new List<TravelQuickItemDTO>();
            if (doctor.AllowTravel)
            {
                items = (from q in _db.DoctorTravelQuick
                         join d in _db.Doses on q.DoseId equals d.Id
                         where q.DoctorId == doctorId
                         orderby q.SortOrder, q.Id
                         select new TravelQuickItemDTO
                         {
                             DoseId = d.Id,
                             DoseName = d.Name,
                             VaccineName = d.Vaccine.Name,
                             SortOrder = q.SortOrder
                         }).ToList();
            }
            return Ok(new Response<List<TravelQuickItemDTO>>(true, null, items));
        }

        // Replaces the doctor's whole list (order = array order). Doctor-only.
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [HttpPut("{doctorId:long}")]
        public ActionResult<Response<List<long>>> Save(long doctorId, [FromBody] List<long> doseIds)
        {
            var doctor = _db.Doctors.FirstOrDefault(d => d.Id == doctorId);
            if (doctor == null)
                return NotFound(new Response<List<long>>(false, "Doctor not found", null));
            if (!doctor.AllowTravel)
                return BadRequest(new Response<List<long>>(false, "This doctor does not have the Travel permission", null));

            doseIds = doseIds ?? new List<long>();
            if (doseIds.Count > MaxItems)
                return BadRequest(new Response<List<long>>(false, "You can choose at most " + MaxItems + " vaccines", null));
            if (doseIds.Distinct().Count() != doseIds.Count)
                return BadRequest(new Response<List<long>>(false, "A vaccine can only be listed once", null));
            var found = _db.Doses.Count(d => doseIds.Contains(d.Id));
            if (found != doseIds.Count)
                return BadRequest(new Response<List<long>>(false, "Unknown vaccine in the list", null));

            using var tx = _db.Database.BeginTransaction();
            _db.DoctorTravelQuick.RemoveRange(_db.DoctorTravelQuick.Where(q => q.DoctorId == doctorId));
            for (int i = 0; i < doseIds.Count; i++)
                _db.DoctorTravelQuick.Add(new DoctorTravelQuick { DoctorId = doctorId, DoseId = doseIds[i], SortOrder = i });
            _db.SaveChanges();
            tx.Commit();
            return Ok(new Response<List<long>>(true, null, doseIds));
        }
    }
}
