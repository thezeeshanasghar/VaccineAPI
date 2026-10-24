using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    public class CustomQuickItemDTO
    {
        public long DoseId { get; set; }
        public string DoseName { get; set; } = "";
        public string VaccineName { get; set; } = "";
        public int SortOrder { get; set; }
    }

    // Customize ("special") quick select. Deliberately separate from TravelQuickController:
    // own table, no dependency on the Travel permission.
    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
    [Owns(OwnerKind.Doctor, "doctorId")]
    public class CustomQuickController : ControllerBase
    {
        public const int MaxItems = 4;
        private readonly Context _db;

        public CustomQuickController(Context db)
        {
            _db = db;
        }

        [HttpGet("{doctorId:long}")]
        public ActionResult<Response<List<CustomQuickItemDTO>>> Get(long doctorId)
        {
            if (!_db.Doctors.Any(d => d.Id == doctorId))
                return NotFound(new Response<List<CustomQuickItemDTO>>(false, "Doctor not found", null));

            var items = (from q in _db.DoctorCustomQuick
                         join d in _db.Doses on q.DoseId equals d.Id
                         where q.DoctorId == doctorId
                         orderby q.SortOrder, q.Id
                         select new CustomQuickItemDTO
                         {
                             DoseId = d.Id,
                             DoseName = d.Name,
                             VaccineName = d.Vaccine.Name,
                             SortOrder = q.SortOrder
                         }).ToList();
            return Ok(new Response<List<CustomQuickItemDTO>>(true, null, items));
        }

        // Replaces the doctor's whole list (order = array order). Doctor-only.
        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [HttpPut("{doctorId:long}")]
        public ActionResult<Response<List<long>>> Save(long doctorId, [FromBody] List<long> doseIds)
        {
            if (!_db.Doctors.Any(d => d.Id == doctorId))
                return NotFound(new Response<List<long>>(false, "Doctor not found", null));

            doseIds = doseIds ?? new List<long>();
            if (doseIds.Count > MaxItems)
                return BadRequest(new Response<List<long>>(false, "You can choose at most " + MaxItems + " vaccines", null));
            if (doseIds.Distinct().Count() != doseIds.Count)
                return BadRequest(new Response<List<long>>(false, "A vaccine can only be listed once", null));
            var found = _db.Doses.Count(d => doseIds.Contains(d.Id));
            if (found != doseIds.Count)
                return BadRequest(new Response<List<long>>(false, "Unknown vaccine in the list", null));

            using var tx = _db.Database.BeginTransaction();
            _db.DoctorCustomQuick.RemoveRange(_db.DoctorCustomQuick.Where(q => q.DoctorId == doctorId));
            for (int i = 0; i < doseIds.Count; i++)
                _db.DoctorCustomQuick.Add(new DoctorCustomQuick { DoctorId = doctorId, DoseId = doseIds[i], SortOrder = i });
            _db.SaveChanges();
            tx.Commit();
            return Ok(new Response<List<long>>(true, null, doseIds));
        }
    }
}
