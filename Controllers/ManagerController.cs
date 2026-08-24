using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ManagerController : ControllerBase
    {
        private readonly Context _db;

        public ManagerController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public ActionResult<IEnumerable<Manager>> GetAll()
        {
            var managers = _db.Manager.ToList();
            return Ok(managers);
        }

        [HttpGet("{id:long}")]
        public ActionResult<Manager> GetById(long id)
        {
            var manager = _db.Manager
                .Include(m => m.User)
                .FirstOrDefault(m => m.Id == id);
            if (manager == null)
            {
                return NotFound(new { message = "Manager not found." });
            }
            return Ok(manager);
        }

        [HttpGet("doctor/{doctorId:long}")]
        public ActionResult GetByDoctorId(long doctorId)
        {
            var managers = _db.Manager
                .Where(m => m.DoctorId == doctorId)
                .Select(m => new { m.Id, m.Name, m.Email, m.IsActive, m.IsVerified })
                .ToList();

            return Ok(managers);
        }

        [HttpPut("{id:long}")]
        public ActionResult Update(long id, [FromBody] Manager manager)
        {
            if (id != manager.Id)
            {
                return BadRequest(new { message = "ID mismatch." });
            }

            var existingManager = _db.Manager.Find(id);
            if (existingManager == null)
            {
                return NotFound(new { message = "Manager not found." });
            }

            existingManager.IsVerified = manager.IsVerified;
            existingManager.IsActive = manager.IsActive;
            _db.Entry(existingManager).State = EntityState.Modified;
            _db.SaveChanges();
            return Ok(new Response<Manager>(true, "Manager updated successfully.", existingManager));
        }

        [HttpDelete("{id:long}")]
        public ActionResult Delete(long id)
        {
            var manager = _db.Manager.Find(id);
            if (manager == null)
            {
                return NotFound(new { message = "Manager not found." });
            }
            var managerAccessEntries = _db.ManagerAccess.Where(ma => ma.ManagerId == id).ToList();
            if (managerAccessEntries.Any())
            {
                _db.ManagerAccess.RemoveRange(managerAccessEntries);
            }
            var user = _db.Users.FirstOrDefault(u => u.Id == manager.UserId);
            if (user != null)
            {
                _db.Users.Remove(user);
            }
            _db.Manager.Remove(manager);
            _db.SaveChanges();
            return Ok(new { message = "Manager and related data deleted successfully." });
        }

        [HttpGet("clinics/{managerId:long}")]
        public async Task<ActionResult<IEnumerable<object>>> GetClinicsByManagerId(long managerId)
        {
            try
            {
                var managerAccessList = await _db
                    .ManagerAccess.Include(ma => ma.Clinic)
                        .ThenInclude(c => c.ClinicTimings)
                    .Where(ma => ma.ManagerId == managerId)
                    .ToListAsync();

                if (!managerAccessList.Any())
                {
                    return NotFound(new { message = "No clinics found for the provided Manager ID." });
                }

                // Project ClinicTimings to DTOs to avoid circular reference (ClinicTiming.Clinic -> Clinic.ClinicTimings)
                var clinicsWithManagerAccess = managerAccessList.Select(ma => new
                {
                    Id = ma.Clinic.Id,
                    Name = ma.Clinic.Name,
                    PhoneNumber = ma.Clinic.PhoneNumber,
                    Address = ma.Clinic.Address,
                    MonogramImage = ma.Clinic.MonogramImage,
                    ConsultationFee = ma.Clinic.ConsultationFee,
                    Lat = ma.Clinic.Lat,
                    Long = ma.Clinic.Long,
                    DoctorId = ma.Clinic.DoctorId,
                    RegNo = ma.Clinic.RegNo,
                    ClinicTimings = ma.Clinic.ClinicTimings.Select(t => new ClinicTimingDTO
                    {
                        Id = t.Id,
                        Day = t.Day,
                        StartTime = t.StartTime,
                        EndTime = t.EndTime,
                        Session = t.Session,
                        IsOpen = t.IsOpen,
                        ClinicId = t.ClinicId
                    }).ToList(),
                    ManagerAccessId = ma.Id
                }).ToList();

                return Ok(new Response<object>(true, "Clinics for given id fetched successfully.", clinicsWithManagerAccess));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching clinics for Manager ID {managerId}: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred while fetching clinics." });
            }
        }

        [HttpPut("{id:long}/profile")]
        public ActionResult UpdateProfile(long id, [FromBody] ManagerDTO dto)
        {
            var manager = _db.Manager
                .Include(m => m.User)
                .FirstOrDefault(m => m.Id == id);
            if (manager == null)
                return NotFound(new { message = "Manager not found." });

            manager.Name = dto.Name?.Trim() ?? manager.Name;
            manager.Email = dto.Email?.Trim() ?? manager.Email;

            if (!string.IsNullOrWhiteSpace(dto.ProfileImage))
                manager.ProfileImage = dto.ProfileImage.Trim();

            _db.Entry(manager).State = EntityState.Modified;
            _db.SaveChanges();
            return Ok(new Response<Manager>(true, "Profile updated successfully.", manager));
        }

        [HttpPut("{id:long}/toggle-active")]
        public ActionResult ToggleActive(long id)
        {
            var manager = _db.Manager.Find(id);
            if (manager == null)
                return NotFound(new { message = "Manager not found." });

            manager.IsActive = !manager.IsActive;
            _db.Entry(manager).State = EntityState.Modified;
            _db.SaveChanges();

            string status = manager.IsActive ? "activated" : "deactivated";
            return Ok(new Response<Manager>(true, $"Manager {status} successfully.", manager));
        }

        [HttpPut("{id:long}/toggle-verify")]
        public ActionResult ToggleVerify(long id)
        {
            var manager = _db.Manager.Find(id);
            if (manager == null)
                return NotFound(new { message = "Manager not found." });

            manager.IsVerified = !manager.IsVerified;
            _db.Entry(manager).State = EntityState.Modified;
            _db.SaveChanges();

            string status = manager.IsVerified ? "approved" : "unapproved";
            return Ok(new Response<Manager>(true, $"Manager {status} successfully.", manager));
        }

        [HttpPost("signup")]
        public ActionResult<Response<ManagerDTO>> Signup([FromBody] ManagerDTO managerDTO)
        {
            if (managerDTO == null)
            {
                return BadRequest(new { message = "Invalid data." });
            }
            var existingUser = _db.Users.FirstOrDefault(x => x.MobileNumber == managerDTO.MobileNumber && x.UserType == "MANAGER");
            if (existingUser != null)
            {
                return new Response<ManagerDTO>(false, "Manager with this mobile number already exists.", null);
            }
            if (string.IsNullOrEmpty(managerDTO.Email))
            {
                return new Response<ManagerDTO>(false, "Email address is required.", null);
            }
            var user = new User
            {
                MobileNumber = managerDTO.MobileNumber,
                Password = managerDTO.Password,
                CountryCode = managerDTO.CountryCode,
                UserType = "MANAGER",
            };
            _db.Users.Add(user);
            _db.SaveChanges();
            var manager = new Manager
            {
                Name = managerDTO.Name,
                Email = managerDTO.Email,
                DoctorId = managerDTO.DoctorId,
                UserId = user.Id,
                IsVerified = true
            };
            _db.Manager.Add(manager);
            _db.SaveChanges();
            managerDTO.Id = manager.Id;
            try
            {
                manager.User = user;
                string body = ""
                 + "Hello " + manager.Name + "\n\n"
                 + "You have been registered as a Manager in the Vaccination Centre system.\n\n"
                 + "Your login details are:\n"
                 + "Mobile Number: " + manager.User.MobileNumber + "\n"
                 + "Password: " + manager.User.Password + "\n\n"
                 + "Please login at: https://doctor.vaccinationcentre.com/loginpa\n\n"
                 + "Regards,\n"
                 + "Vaccination Centre Team";
                UserEmail.SendEmail(manager.Email, body, "Your Manager Account Details");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                return new Response<ManagerDTO>(true, "Signup successful but failed to send email notification.", managerDTO);
            }

            return new Response<ManagerDTO>(true, "Signup successful. Login details sent to email.", managerDTO);
        }
    }
}
