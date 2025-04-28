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

            existingAssistant.Name = personalAssistant.Name;
            existingAssistant.DoctorId = personalAssistant.DoctorId;

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

        // [HttpPost("login")]
        // public ActionResult<Response<PersonalAssistant>> Login([FromBody] PersonalAssistantDTO PersonalAssistantDTO)
        // {
        //     if (PersonalAssistantDTO == null || string.IsNullOrEmpty(PersonalAssistantDTO.MobileNumber) || string.IsNullOrEmpty(PersonalAssistantDTO.Password))
        //     {
        //         return BadRequest(new { message = "Invalid login data." });
        //     }

        //     var personalAssistant = _db.Users.FirstOrDefault(pa =>pa.MobileNumber == PersonalAssistantDTO.MobileNumber && pa.UserType == "PA");

        //     if (personalAssistant == null)
        //     {
        //         return Unauthorized(new { message = "Invalid Mobile Number or Password." });
        //     }

        //     return Ok( new Response<PersonalAssistant>(true, "Login successful.", personalAssistant));
        // }

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
                return new Response<PersonalAssistantDTO>( false,"Personal Assistant with this mobile number already exists.",null);
            }

            var user = new User
            {
                MobileNumber = personalAssistantDTO.MobileNumber,
                Password = personalAssistantDTO.Password,
                CountryCode = personalAssistantDTO.CountryCode,
                UserType = "PA", // UserType for Personal Assistant
            };
            _db.Users.Add(user);
            _db.SaveChanges();

            var personalAssistant = new PersonalAssistant
            {
                Name = personalAssistantDTO.Name,
                DoctorId = personalAssistantDTO.DoctorId,
                UserId = user.Id, // Link to the User table
            };
            _db.PersonalAssistant.Add(personalAssistant);
            _db.SaveChanges();

            personalAssistantDTO.Id = personalAssistant.Id;

            return new Response<PersonalAssistantDTO>(true,"Signup successful.",personalAssistantDTO );
        }
    }
}