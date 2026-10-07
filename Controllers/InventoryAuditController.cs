using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Models;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    // Read-only reconciliation report: stored stock vs ledger-derived stock vs BrandAmount per
    // clinic / brand / batch, plus every anomaly class. Never changes data.
    [Route("api/[controller]")]
    [ApiController]
    [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
    [Owns(OwnerKind.Doctor, "doctorId")]
    [Owns(OwnerKind.Clinic, "clinicId", "OnlineClinicId")]
    [Owns(OwnerKind.Child, "childId")]
    [Owns(OwnerKind.Pa, "paId")]
    public class InventoryAuditController : ControllerBase
    {
        private readonly Context _db;
        public InventoryAuditController(Context db) { _db = db; }

        [HttpGet]
        public IActionResult Get([FromQuery] long? doctorId = null, [FromQuery] long? clinicId = null)
        {
            var report = new InventoryAuditService(_db).Report(doctorId, clinicId);
            return Ok(new
            {
                IsSuccess = true,
                report.IsClean,
                report.CountsByCode,
                report.Brands,
                report.Findings,
                report.Batches
            });
        }
    }
}
