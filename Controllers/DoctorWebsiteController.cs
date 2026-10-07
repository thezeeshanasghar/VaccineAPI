using System.Linq;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
    [Owns(OwnerKind.Doctor, "doctorId")]
    [Owns(OwnerKind.Clinic, "clinicId", "OnlineClinicId")]
    [Owns(OwnerKind.Child, "childId")]
    [Owns(OwnerKind.Pa, "paId")]
    public class DoctorWebsiteController : ControllerBase
    {
        private readonly Context _db;

        public DoctorWebsiteController(Context db)
        {
            _db = db;
        }

        [HttpGet("{doctorId:long}")]
        public ActionResult<DoctorWebsiteDTO> Get(long doctorId)
        {
            var doctor = _db.Doctors.FirstOrDefault(d => d.Id == doctorId);
            if (doctor == null)
            {
                return NotFound(new Response<DoctorWebsiteDTO>(false, "Doctor not found", null));
            }

            // Mirrors EmailSenderResolver's pattern: AllowOwnWebsite alone doesn't mean a
            // usable link exists yet. A doctor who flipped the permission on but hasn't
            // saved a URL should fall back to default (no custom website) behavior, not
            // present a blank/broken link to callers that trust the flag.
            var effectiveUrl = doctor.AllowOwnWebsite && !string.IsNullOrWhiteSpace(doctor.WebsiteUrl)
                ? doctor.WebsiteUrl
                : null;

            return Ok(new DoctorWebsiteDTO
            {
                AllowOwnWebsite = doctor.AllowOwnWebsite,
                WebsiteUrl = effectiveUrl
            });
        }

        [HttpPut("{doctorId:long}")]
        public ActionResult<Response<DoctorWebsiteDTO>> Save(long doctorId, [FromBody] DoctorWebsiteDTO dto)
        {
            var doctor = _db.Doctors.FirstOrDefault(d => d.Id == doctorId);
            if (doctor == null)
            {
                return NotFound(new Response<DoctorWebsiteDTO>(false, "Doctor not found", null));
            }
            if (!doctor.AllowOwnWebsite)
            {
                return BadRequest(new Response<DoctorWebsiteDTO>(false, "This doctor is not allowed to use their own website", null));
            }

            doctor.WebsiteUrl = dto.WebsiteUrl;
            _db.SaveChanges();

            return Ok(new Response<DoctorWebsiteDTO>(true, null, dto));
        }
    }
}
