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

        // GET: api/PersonalAssistant
        [HttpGet]
        public ActionResult<IEnumerable<PersonalAssistant>> GetAll()
        {
            var personalAssistants = _db.PersonalAssistant.ToList();
            return Ok(personalAssistants);
        }

        // GET: api/PersonalAssistant/{id}
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

        // POST: api/PersonalAssistant
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

        // PUT: api/PersonalAssistant/{id}
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

        // DELETE: api/PersonalAssistant/{id}
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
    }
}