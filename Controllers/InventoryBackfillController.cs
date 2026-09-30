using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Models;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    // The one-time "rebuild the ledger from the documents" tool is RETIRED. The ledger is now the
    // authoritative record; wiping and re-deriving it from documents would destroy history
    // (opening balances, pending doses, reversal links) that the documents cannot reproduce.
    // What remains is read-only verification and the sanctioned projection rebuild.
    [Route("api/[controller]")]
    [ApiController]
    public class InventoryBackfillController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryTransactionService _inventory;
        public InventoryBackfillController(Context db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        [HttpPost("run")]
        public IActionResult Run()
        {
            return Ok(new
            {
                IsSuccess = false,
                Message = "Retired: the inventory ledger can no longer be wiped and rebuilt from documents. Use GET /api/InventoryAudit to review differences."
            });
        }

        [HttpGet("verify")]
        public IActionResult Verify()
        {
            var report = new InventoryAuditService(_db).Report();
            return Ok(new { IsSuccess = true, ResponseData = report });
        }

        // Rebuilds the BrandAmount projection from batch rows for every clinic of ONE doctor (session-verified). Never touches
        // batches or ledger movements; every change is logged as a BA_REPROJECTED note.
        [HttpPost("correct-drift")]
        public async Task<IActionResult> CorrectDrift([FromQuery] long doctorId, [FromQuery] long? callerUserId = null, [FromQuery] string? securityStamp = null)
        {
            var doctor = _db.Doctors.Find(doctorId);
            if (doctor == null || !CallerGuard.VerifyCaller(_db, callerUserId, securityStamp) || callerUserId!.Value != doctor.UserId)
                return Ok(new { IsSuccess = false, Message = "Only the doctor can rebuild the counters." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                int corrected = 0;
                foreach (var clinicId in _db.Clinics.Where(c => c.DoctorId == doctorId).Select(c => c.Id).ToList())
                    corrected += _inventory.RebuildBrandAmounts(clinicId).Count;
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();
                return Ok(new { IsSuccess = true, Message = $"Rebuilt {corrected} BrandAmount row(s)", CorrectedCount = corrected });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }
    }
}
