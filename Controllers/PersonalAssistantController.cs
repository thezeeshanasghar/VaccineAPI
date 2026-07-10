using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using System.Data;
using AutoMapper;
using VaccineAPI.ModelDTO;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PersonalAssistantController : ControllerBase
    {
        private readonly Context _db;

        public PersonalAssistantController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public ActionResult<IEnumerable<PersonalAssistant>> GetAll()
        {
            var personalAssistants = _db.PersonalAssistant.ToList();
            return Ok(personalAssistants);
        }

        [HttpGet("{id:long}")]
        public ActionResult<PersonalAssistant> GetById(long id)
        {
            // Include the linked User so callers (e.g. the PA self-profile screen)
            // can read the login mobile number, which is the WhatsApp number.
            var personalAssistant = _db.PersonalAssistant
                .Include(p => p.User)
                .FirstOrDefault(p => p.Id == id);
            if (personalAssistant == null)
            {
                return NotFound(new { message = "Personal Assistant not found." });
            }
            return Ok(personalAssistant);
        }

        [HttpGet("doctor/{doctorId:long}")]
        public ActionResult GetByDoctorId(long doctorId)
        {
            var personalAssistants = _db.PersonalAssistant
                .Where(pa => pa.DoctorId == doctorId)
                .Select(pa => new { pa.Id, pa.Name, pa.Email, pa.IsActive, pa.IsVerified })
                .ToList();

            return Ok(personalAssistants);
        }

        [HttpPost]
        public ActionResult<PersonalAssistant> Create([FromBody] PersonalAssistant personalAssistant)
        {
            if (personalAssistant == null)
            {
                return BadRequest(new { message = "Invalid data." });
            }

            _db.PersonalAssistant.Add(personalAssistant);
            _db.SaveChanges();

            return CreatedAtAction(nameof(GetById), new { id = personalAssistant.Id }, personalAssistant);
        }

        [HttpPut("{id:long}")]
        public ActionResult Update(long id, [FromBody] PersonalAssistant personalAssistant)
        {
            if (id != personalAssistant.Id)
            {
                return BadRequest(new { message = "ID mismatch." });
            }

            var existingAssistant = _db.PersonalAssistant.Find(id);
            if (existingAssistant == null)
            {
                return NotFound(new { message = "Personal Assistant not found." });
            }

            existingAssistant.AllowStock = personalAssistant.AllowStock;
            existingAssistant.AllowAlert = personalAssistant.AllowAlert;
            existingAssistant.AllowClinic = personalAssistant.AllowClinic;
            existingAssistant.AllowSchedule = personalAssistant.AllowSchedule;
            existingAssistant.AllowVacation = personalAssistant.AllowVacation;
            existingAssistant.AllowAnalytics = personalAssistant.AllowAnalytics;
            existingAssistant.AllowChild = personalAssistant.AllowChild;
            existingAssistant.IsVerified = personalAssistant.IsVerified;
            existingAssistant.IsActive = personalAssistant.IsActive;
            _db.Entry(existingAssistant).State = EntityState.Modified;
            _db.SaveChanges();
            return Ok(new Response<PersonalAssistant>(true, "Personal Assistant updated successfully.", existingAssistant));
        }

        [HttpDelete("{id:long}")]
        public ActionResult Delete(long id)
        {
            var personalAssistant = _db.PersonalAssistant.Find(id);
            if (personalAssistant == null)
            {
            return NotFound(new { message = "Personal Assistant not found." });
            }
            var paAccessEntries = _db.PaAccess.Where(pa => pa.PersonalAssistantId == id).ToList();
            if (paAccessEntries.Any())
            {
            _db.PaAccess.RemoveRange(paAccessEntries);
            }
            var user = _db.Users.FirstOrDefault(u => u.Id == personalAssistant.UserId);
            if (user != null)
            {
            _db.Users.Remove(user);
            }
            _db.PersonalAssistant.Remove(personalAssistant);
            _db.SaveChanges();
            return Ok(new { message = "Personal Assistant and related data deleted successfully." });
        }

        [HttpGet("clinics/{paId:long}")]
        public async Task<ActionResult<IEnumerable<object>>> GetClinicsByPaId(long paId)
        {
            try
            {
                var paAccessList = await _db
                    .PaAccess.Include(pa => pa.Clinic)
                        .ThenInclude(c => c.ClinicTimings)
                    .Where(pa => pa.PersonalAssistantId == paId)
                    .ToListAsync();
                    
                if (!paAccessList.Any())
                {
                    return NotFound(new { message = "No clinics found for the provided PA ID." });
                }
                
                // Return clinics with PA-specific IsOnline status
                // Project ClinicTimings to DTOs to avoid circular reference (ClinicTiming.Clinic -> Clinic.ClinicTimings)
                var clinicsWithPaStatus = paAccessList.Select(pa => new
                {
                    Id = pa.Clinic.Id,
                    Name = pa.Clinic.Name,
                    PhoneNumber = pa.Clinic.PhoneNumber,
                    Address = pa.Clinic.Address,
                    MonogramImage = pa.Clinic.MonogramImage,
                    ConsultationFee = pa.Clinic.ConsultationFee,
                    Lat = pa.Clinic.Lat,
                    Long = pa.Clinic.Long,
                    DoctorId = pa.Clinic.DoctorId,
                    RegNo = pa.Clinic.RegNo,
                    IsOnline = pa.IsOnline, // Use PA's IsOnline instead of Clinic's IsOnline
                    ClinicTimings = pa.Clinic.ClinicTimings.Select(t => new ClinicTimingDTO
                    {
                        Id = t.Id,
                        Day = t.Day,
                        StartTime = t.StartTime,
                        EndTime = t.EndTime,
                        Session = t.Session,
                        IsOpen = t.IsOpen,
                        ClinicId = t.ClinicId
                    }).ToList(),
                    PaAccessId = pa.Id
                }).ToList();
                
                return Ok(new Response<object>(true, "Clinics for given id fetched successfully.", clinicsWithPaStatus));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching clinics for PA ID {paId}: {ex.Message}");
                return StatusCode(500,new { message = "An error occurred while fetching clinics." });
            }
        }

        [HttpPut("{id:long}/profile")]
        public ActionResult UpdateProfile(long id, [FromBody] PersonalAssistantDTO dto)
        {
            var pa = _db.PersonalAssistant
                .Include(p => p.User)
                .FirstOrDefault(p => p.Id == id);
            if (pa == null)
                return NotFound(new { message = "Personal Assistant not found." });

            pa.Name  = dto.Name?.Trim() ?? pa.Name;
            pa.Email = dto.Email?.Trim() ?? pa.Email;

            // Only overwrite the photo when a new one was uploaded this save — the frontend
            // uploads via UploadController first and sends back the returned filename here.
            // Empty means "unchanged", so an edit that doesn't touch the picture keeps it.
            if (!string.IsNullOrWhiteSpace(dto.ProfileImage))
                pa.ProfileImage = dto.ProfileImage.Trim();

            // The PA's mobile number is intentionally NOT editable from the self-profile
            // screen: it is the login number and is the WhatsApp number shown to parents,
            // so it stays locked to whatever the doctor set at account creation.

            _db.Entry(pa).State = EntityState.Modified;
            _db.SaveChanges();
            return Ok(new Response<PersonalAssistant>(true, "Profile updated successfully.", pa));
        }

        [HttpPut("{id:long}/toggle-active")]
        public ActionResult ToggleActive(long id)
        {
            var pa = _db.PersonalAssistant.Find(id);
            if (pa == null)
                return NotFound(new { message = "Personal Assistant not found." });

            pa.IsActive = !pa.IsActive;
            _db.Entry(pa).State = EntityState.Modified;
            _db.SaveChanges();

            string status = pa.IsActive ? "activated" : "deactivated";
            return Ok(new Response<PersonalAssistant>(true, $"Personal Assistant {status} successfully.", pa));
        }

        [HttpPut("{id:long}/toggle-verify")]
        public ActionResult ToggleVerify(long id)
        {
            var pa = _db.PersonalAssistant.Find(id);
            if (pa == null)
                return NotFound(new { message = "Personal Assistant not found." });

            pa.IsVerified = !pa.IsVerified;
            _db.Entry(pa).State = EntityState.Modified;
            _db.SaveChanges();

            string status = pa.IsVerified ? "approved" : "unapproved";
            return Ok(new Response<PersonalAssistant>(true, $"Personal Assistant {status} successfully.", pa));
        }

        [HttpPost("signup")]
        public ActionResult<Response<PersonalAssistantDTO>> Signup([FromBody] PersonalAssistantDTO personalAssistantDTO)
        {
            if (personalAssistantDTO == null)
            {
                return BadRequest(new { message = "Invalid data." });
            }
            var existingUser = _db.Users.FirstOrDefault(x => x.MobileNumber == personalAssistantDTO.MobileNumber && x.UserType == "PA");
            if (existingUser != null)
            {
                return new Response<PersonalAssistantDTO>(false, "Personal Assistant with this mobile number already exists.", null);
            }
            if (string.IsNullOrEmpty(personalAssistantDTO.Email))
            {
                return new Response<PersonalAssistantDTO>(false, "Email address is required.", null);
            }
            var user = new User
            {
                MobileNumber = personalAssistantDTO.MobileNumber,
                Password = personalAssistantDTO.Password,
                CountryCode = personalAssistantDTO.CountryCode,
                UserType = "PA",
            };
            _db.Users.Add(user);
            _db.SaveChanges();
            var personalAssistant = new PersonalAssistant
            {
                Name = personalAssistantDTO.Name,
                Email = personalAssistantDTO.Email,
                DoctorId = personalAssistantDTO.DoctorId,
                UserId = user.Id,
                AllowStock = false,
                AllowAlert = false,
                AllowClinic = false,
                AllowSchedule = false,
                AllowVacation = false,
                AllowAnalytics = false,
                AllowChild = false,
                IsVerified = true
            };
            _db.PersonalAssistant.Add(personalAssistant);
            _db.SaveChanges();
            personalAssistantDTO.Id = personalAssistant.Id;
            try
            {
                personalAssistant.User = user;
                  string body = ""
                   + "Hello " + personalAssistant.Name + "\n\n"
                   + "You have been registered as a Personal Assistant in the Vaccination Centre system.\n\n"
                   + "Your login details are:\n"
                   + "Mobile Number: " + personalAssistant.User.MobileNumber + "\n"
                   + "Password: " + personalAssistant.User.Password + "\n\n"
                   + "Please login at: https://doctor.vaccinationcentre.com/loginpa\n\n"
                   + "Regards,\n"
                   + "Vaccination Centre Team";
                UserEmail.SendEmail(personalAssistant.Email, body, "Your Personal Assistant Account Details");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                return new Response<PersonalAssistantDTO>(true, "Signup successful but failed to send email notification.", personalAssistantDTO);
            }

            return new Response<PersonalAssistantDTO>(true, "Signup successful. Login details sent to email.", personalAssistantDTO);
        }
    }
}