using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    // Doses that were given while no stock batch existed yet (the purchase had not been entered).
    // They carry no stock effect until the doctor allocates them to a real batch ("include this
    // batch details and deduct the units already used") or dismisses them with a reason.
    // Listing is open to the owning doctor's session; allocating and dismissing are DOCTOR-ONLY
    // and verified against the caller's session stamp.
    [Route("api/[controller]")]
    [ApiController]
    public class UnbatchedUseController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryTransactionService _inventory;

        public UnbatchedUseController(Context db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        public class ClaimDTO
        {
            public long DoctorId { get; set; }
            public int StockId { get; set; }
            public List<long> UseIds { get; set; } = new List<long>();
            public long? CallerUserId { get; set; }
            public string? SecurityStamp { get; set; }
        }

        public class DismissDTO
        {
            public long DoctorId { get; set; }
            public string Reason { get; set; } = "";
            public long? CallerUserId { get; set; }
            public string? SecurityStamp { get; set; }
        }

        private bool CallerIsDoctor(long doctorId, long? callerUserId, string? stamp)
        {
            var doctor = _db.Doctors.Find(doctorId);
            return doctor != null && CallerGuard.VerifyCaller(_db, callerUserId, stamp) && callerUserId!.Value == doctor.UserId;
        }

        // GET /api/UnbatchedUse?doctorId=&clinicId=&brandId=
        [HttpGet]
        public async Task<IActionResult> GetPending([FromQuery] long doctorId, [FromQuery] long clinicId, [FromQuery] long? brandId = null)
        {
            var q = _db.UnbatchedUses.Where(u => u.DoctorId == doctorId && u.ClinicId == clinicId
                                                 && u.Status == UnbatchedUseStatus.Pending);
            if (brandId.HasValue) q = q.Where(u => u.BrandId == brandId.Value);
            var uses = await q.OrderBy(u => u.GivenDate).ThenBy(u => u.Id).ToListAsync();

            var scheduleIds = uses.Select(u => u.ScheduleId).ToList();
            var schedules = await _db.Schedules.Include(s => s.Child).Include(s => s.Dose)
                .Where(s => scheduleIds.Contains(s.Id) && s.IsDone).ToListAsync();
            var brandIds = uses.Select(u => u.BrandId).Distinct().ToList();
            var brands = await _db.Brands.Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);

            var rows = uses.Join(schedules, u => u.ScheduleId, s => s.Id, (u, s) => new
            {
                u.Id, u.ScheduleId, u.BrandId,
                BrandName = brands.TryGetValue(u.BrandId, out var bn) ? bn : "",
                u.ClinicId, u.GivenDate, u.GivenByPaId,
                ChildId = s.ChildId, ChildName = s.Child != null ? s.Child.Name : "",
                DoseName = s.Dose != null ? s.Dose.Name : ""
            }).ToList();
            return Ok(new { IsSuccess = true, ResponseData = rows });
        }

        // GET /api/UnbatchedUse/count?doctorId=&clinicId=
        [HttpGet("count")]
        public async Task<IActionResult> Count([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            int n = await _db.UnbatchedUses.CountAsync(u => u.DoctorId == doctorId && u.ClinicId == clinicId
                                                            && u.Status == UnbatchedUseStatus.Pending);
            return Ok(new { IsSuccess = true, ResponseData = n });
        }

        // POST /api/UnbatchedUse/claim
        [HttpPost("claim")]
        public async Task<IActionResult> Claim([FromBody] ClaimDTO dto)
        {
            if (!CallerIsDoctor(dto.DoctorId, dto.CallerUserId, dto.SecurityStamp))
                return Ok(new { IsSuccess = false, Message = "Only the doctor can include doses in a batch." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var res = _inventory.ClaimUnbatched(dto.DoctorId, dto.StockId, dto.UseIds);
                if (!res.IsSuccess)
                    return Ok(new { IsSuccess = false, Message = res.Message });
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();
                return Ok(new { IsSuccess = true, Message = "Doses included in the batch", ResponseData = new { Claimed = dto.UseIds.Count } });
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Stock was updated by another action just now. Please retry." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // POST /api/UnbatchedUse/{id}/dismiss
        [HttpPost("{id}/dismiss")]
        public async Task<IActionResult> Dismiss(long id, [FromBody] DismissDTO dto)
        {
            if (!CallerIsDoctor(dto.DoctorId, dto.CallerUserId, dto.SecurityStamp))
                return Ok(new { IsSuccess = false, Message = "Only the doctor can dismiss a dose." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var res = _inventory.DismissUnbatched(dto.DoctorId, id, dto.Reason);
                if (!res.IsSuccess)
                    return Ok(new { IsSuccess = false, Message = res.Message });
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();
                return Ok(new { IsSuccess = true, Message = "Dose dismissed" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }
    }
}
