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
    public class DoctorSmtpController : ControllerBase
    {
        private readonly Context _db;

        public DoctorSmtpController(Context db)
        {
            _db = db;
        }

        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [HttpGet("{doctorId:long}")]
        public ActionResult<DoctorSmtpDTO> Get(long doctorId)
        {
            var doctor = _db.Doctors.FirstOrDefault(d => d.Id == doctorId);
            if (doctor == null)
            {
                return NotFound(new Response<DoctorSmtpDTO>(false, "Doctor not found", null));
            }

            return Ok(new DoctorSmtpDTO
            {
                AllowOwnEmail = doctor.AllowOwnEmail,
                SmtpHost = doctor.SmtpHost,
                SmtpPort = doctor.SmtpPort,
                SmtpUseSsl = doctor.SmtpUseSsl,
                SmtpUsername = doctor.SmtpUsername,
                SmtpPassword = doctor.SmtpPassword,
                SmtpFromEmail = doctor.SmtpFromEmail,
                SmtpFromName = doctor.SmtpFromName
            });
        }

        [RolesOnly("DOCTOR", "SUPERADMIN")]
        [HttpPut("{doctorId:long}")]
        public ActionResult<Response<DoctorSmtpDTO>> Save(long doctorId, [FromBody] DoctorSmtpDTO dto)
        {
            var doctor = _db.Doctors.FirstOrDefault(d => d.Id == doctorId);
            if (doctor == null)
            {
                return NotFound(new Response<DoctorSmtpDTO>(false, "Doctor not found", null));
            }
            if (!doctor.AllowOwnEmail)
            {
                return BadRequest(new Response<DoctorSmtpDTO>(false, "This doctor is not allowed to use their own email settings", null));
            }

            doctor.SmtpHost = dto.SmtpHost;
            doctor.SmtpPort = dto.SmtpPort;
            doctor.SmtpUseSsl = dto.SmtpUseSsl;
            doctor.SmtpUsername = dto.SmtpUsername;
            doctor.SmtpPassword = dto.SmtpPassword;
            doctor.SmtpFromEmail = dto.SmtpFromEmail;
            doctor.SmtpFromName = dto.SmtpFromName;
            _db.SaveChanges();

            return Ok(new Response<DoctorSmtpDTO>(true, null, dto));
        }
    }
}
