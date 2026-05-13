using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using System;
using System.Linq;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DevelopmentalAssessmentController : ControllerBase
    {
        private readonly Context _db;

        public DevelopmentalAssessmentController(Context db)
        {
            _db = db;
        }

        // GET api/DevelopmentalAssessment/child/5
        [HttpGet("child/{childId:long}")]
        public ActionResult GetByChild(long childId)
        {
            var records = _db.DevelopmentalAssessments
                .Where(d => d.ChildId == childId)
                .OrderByDescending(d => d.VisitDate)
                .ToList();
            return Ok(new { IsSuccess = true, ResponseData = records });
        }

        // GET api/DevelopmentalAssessment/child/5/visit/2025-06-01
        [HttpGet("child/{childId:long}/visit/{visitDate}")]
        public ActionResult GetByVisit(long childId, string visitDate)
        {
            if (!DateTime.TryParse(visitDate, out DateTime date))
                return BadRequest(new { message = "Invalid date format." });

            var record = _db.DevelopmentalAssessments
                .FirstOrDefault(d => d.ChildId == childId && d.VisitDate.Date == date.Date);

            return Ok(new { IsSuccess = true, ResponseData = record });
        }

        // POST api/DevelopmentalAssessment
        [HttpPost]
        public ActionResult Upsert([FromBody] DevelopmentalAssessment incoming)
        {
            var existing = _db.DevelopmentalAssessments
                .FirstOrDefault(d => d.ChildId == incoming.ChildId && d.VisitDate.Date == incoming.VisitDate.Date);

            if (existing == null)
            {
                incoming.Id = 0;
                incoming.CreatedAt = DateTime.UtcNow;
                _db.DevelopmentalAssessments.Add(incoming);
            }
            else
            {
                existing.AgeBracket = incoming.AgeBracket;
                existing.AgeInMonths = incoming.AgeInMonths;
                existing.Q1 = incoming.Q1;
                existing.Q2 = incoming.Q2;
                existing.Q3 = incoming.Q3;
                existing.Q4 = incoming.Q4;
                existing.Q5 = incoming.Q5;
                existing.Q6 = incoming.Q6;
                existing.Q7 = incoming.Q7;
                existing.Q8 = incoming.Q8;
                existing.Q9 = incoming.Q9;
                existing.Q10 = incoming.Q10;
                existing.Notes = incoming.Notes;
                existing.PaId = incoming.PaId;
                existing.DoctorId = incoming.DoctorId;
                _db.Entry(existing).State = EntityState.Modified;
            }

            _db.SaveChanges();
            return Ok(new { IsSuccess = true, message = "Assessment saved." });
        }
    }
}
