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
            var personalAssistant = _db.PersonalAssistant.Find(id);
            if (personalAssistant == null)
            {
                return NotFound(new { message = "Personal Assistant not found." });
            }
            return Ok(personalAssistant);
        }

        [HttpGet("doctor/{doctorId:long}")]
        public ActionResult<IEnumerable<PersonalAssistant>> GetByDoctorId(long doctorId)
        {
            var personalAssistants = _db.PersonalAssistant.Where(pa => pa.DoctorId == doctorId).ToList();
            if (!personalAssistants.Any())
            {
                return NotFound(new { message = "No Personal Assistants found for the given Doctor ID." });
            }

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

            _db.Entry(existingAssistant).State = EntityState.Modified;
            _db.SaveChanges();

            return NoContent();
        }

        [HttpDelete("{id:long}")]
        public ActionResult Delete(long id)
        {
            var personalAssistant = _db.PersonalAssistant.Find(id);
            if (personalAssistant == null)
            {
                return NotFound(new { message = "Personal Assistant not found." });
            }

            _db.PersonalAssistant.Remove(personalAssistant);
            _db.SaveChanges();

            return Ok(new { message = "Personal Assistant deleted successfully." });
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
                IsVerified = false
            };
            _db.PersonalAssistant.Add(personalAssistant);
            _db.SaveChanges();

            personalAssistantDTO.Id = personalAssistant.Id;

            try
            {
                personalAssistant.User = user;
                
                UserEmail.PersonalAssistantLoginDetails(personalAssistant, personalAssistantDTO.Password);
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