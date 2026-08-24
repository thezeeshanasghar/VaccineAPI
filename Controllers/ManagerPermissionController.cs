using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ManagerPermissionController : ControllerBase
    {
        private readonly Context _db;

        public ManagerPermissionController(Context db)
        {
            _db = db;
        }

        [HttpGet("{managerId:long}")]
        public ActionResult<ManagerPermission> GetByManagerId(long managerId)
        {
            var perm = _db.ManagerPermissions.FirstOrDefault(p => p.ManagerId == managerId);
            if (perm == null)
            {
                // Return a blank permission object so frontend knows none are set yet.
                return Ok(new ManagerPermission { ManagerId = managerId });
            }
            return Ok(perm);
        }

        [HttpPut("{managerId:long}")]
        public ActionResult Upsert(long managerId, [FromBody] ManagerPermission incoming)
        {
            var manager = _db.Manager.Find(managerId);
            if (manager == null)
                return NotFound(new { message = "Manager not found." });

            var existing = _db.ManagerPermissions.FirstOrDefault(p => p.ManagerId == managerId);
            if (existing == null)
            {
                incoming.ManagerId = managerId;
                incoming.Id = 0;
                _db.ManagerPermissions.Add(incoming);
            }
            else
            {
                existing.ViewPaAssignmentStatus = incoming.ViewPaAssignmentStatus;
                existing.ReassignPaTask = incoming.ReassignPaTask;
                existing.ViewFeedbackResponseTracker = incoming.ViewFeedbackResponseTracker;
                existing.SendFeedbackEmail = incoming.SendFeedbackEmail;
                existing.SendFeedbackWhatsApp = incoming.SendFeedbackWhatsApp;
                existing.ManagePaClinicAssignments = incoming.ManagePaClinicAssignments;

                _db.Entry(existing).State = EntityState.Modified;
            }

            _db.SaveChanges();
            return Ok(new { message = "Permissions saved successfully." });
        }
    }
}
