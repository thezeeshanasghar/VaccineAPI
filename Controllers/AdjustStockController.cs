using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdjustStockController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryTransactionService _inventory;
        public AdjustStockController(Context db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        // GET /api/adjuststock?doctorId=X&clinicId=Y
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            var rows = await _db.AdjustStocks
                .Include(a => a.Brand)
                .Where(a => a.DoctorId == doctorId && a.ClinicId == clinicId)
                .OrderByDescending(a => a.Date)
                .ToListAsync();

            var brandIds = rows.Select(a => a.BrandId).Distinct().ToList();
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            var clinic = await _db.Clinics.FindAsync(clinicId);
            string clinicName = clinic != null ? clinic.Name : "";

            var result = rows.Select(a =>
            {
                var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == a.BrandId);
                return new AdjustStockListDTO
                {
                    Id = a.Id,
                    BrandName = a.Brand != null ? a.Brand.Name : "",
                    VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                    Adjustment = a.Adjustment,
                    Type = a.Adjustment >= 0 ? "Increase" : "Loss",
                    Reason = a.Reason ?? "",
                    Price = a.Price,
                    BatchLot = a.BatchLot ?? "",
                    ExpiryDate = a.ExpiryDate,
                    Date = a.Date,
                    ClinicName = clinicName
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // POST /api/adjuststock/writeoff
        // Typed write-off of units from one specific batch (wastage / breakage / expired vials).
        [HttpPost("writeoff")]
        public async Task<IActionResult> WriteOff([FromBody] WriteOffDTO dto)
        {
            if (dto == null)
                return Ok(new { IsSuccess = false, Message = "Invalid request" });

            var guard = StockActionGuard.CheckStockAction(
                _db, dto.PaId, dto.ManagerId, dto.CallerUserId, dto.SecurityStamp,
                perm => perm.StockAdjust, "write off stock");
            if (!guard.allowed)
                return Ok(new { IsSuccess = false, Message = guard.error });

            if (dto.Kind != "Wastage" && dto.Kind != "Expiry")
                return Ok(new { IsSuccess = false, Message = "Kind must be Wastage or Expiry" });
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return Ok(new { IsSuccess = false, Message = "A reason is required" });
            if (dto.Quantity <= 0)
                return Ok(new { IsSuccess = false, Message = "Quantity must be greater than 0" });

            var replay = Idempotency.TryReplay(_db, dto.DoctorId, dto.ClientRequestId, "adjuststock.writeoff");
            if (replay != null) return replay;

            var batch = await _db.Stocks.FirstOrDefaultAsync(s => s.Id == dto.StockId);
            if (batch == null)
                return Ok(new { IsSuccess = false, Message = "Batch not found" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var row = new AdjustStock
                {
                    BrandId = batch.BrandId,
                    ClinicId = dto.ClinicId,
                    DoctorId = dto.DoctorId,
                    Adjustment = -dto.Quantity,
                    Price = 0,
                    Reason = $"{dto.Kind}: {dto.Reason.Trim()}",
                    Date = dto.Date,
                    BatchLot = batch.BatchLot,
                    ExpiryDate = batch.Expiry
                };
                _db.AdjustStocks.Add(row);
                await _db.SaveChangesAsync(); // need row.Id for the ledger SourceId

                var res = _inventory.WriteOff(dto.DoctorId, dto.ClinicId, dto.StockId, dto.Quantity, dto.Kind == "Expiry", row.Id, dto.Date);
                if (!res.IsSuccess)
                    return Ok(new { IsSuccess = false, Message = res.Message });   // tx disposed = rolled back

                var response = new { IsSuccess = true, Message = "Stock written off", ResponseData = new { row.Id } };
                Idempotency.Record(_db, dto.DoctorId, dto.ClientRequestId, "adjuststock.writeoff", response);
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();
                return Ok(response);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // POST /api/adjuststock
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdjustStockCreateDTO dto)
        {
            if (dto == null)
                return Ok(new { IsSuccess = false, Message = "Invalid request" });

            var guard = StockActionGuard.CheckStockAction(
                _db, dto.PaId, dto.ManagerId, dto.CallerUserId, dto.SecurityStamp,
                perm => perm.StockAdjust, "adjust stock");
            if (!guard.allowed)
                return Ok(new { IsSuccess = false, Message = guard.error });

            if (dto.BrandId <= 0)
                return Ok(new { IsSuccess = false, Message = "Brand is required" });
            if (dto.Quantity <= 0)
                return Ok(new { IsSuccess = false, Message = "Quantity must be greater than 0" });
            if (dto.Type != "Increase" && dto.Type != "Loss")
                return Ok(new { IsSuccess = false, Message = "Type must be Increase or Loss" });
            if (string.IsNullOrWhiteSpace(dto.BatchLot))
                return Ok(new { IsSuccess = false, Message = "Batch is required" });
            if (dto.Type == "Increase" && dto.Price <= 0)
                return Ok(new { IsSuccess = false, Message = "Price per unit is required for Increase" });

            // Including pending doses in a batch is a doctor decision: PA and Manager requests cannot do it.
            if ((dto.PaId.HasValue || dto.ManagerId.HasValue) && ((dto.ClaimUnbatchedUseIds != null && dto.ClaimUnbatchedUseIds.Count > 0) || dto.ClearUnbatchedBacklog == true))
                return Ok(new { IsSuccess = false, Message = "Only the doctor can include pending doses in a batch." });

            var replay = Idempotency.TryReplay(_db, dto.DoctorId, dto.ClientRequestId, "adjuststock.create");
            if (replay != null) return replay;

            // Pending (unbatched) doses: doses recorded as given while no batch was on the shelf. When
            // stock is added, ask once whether to include this batch and deduct those units.
            // (Message prefix "recorded as given with no batch" is what the VacDoc page matches on.)
            if (dto.Type == "Increase" && dto.ClearUnbatchedBacklog == null && dto.ClaimUnbatchedUseIds == null)
            {
                var pending = _inventory.PendingCount(dto.DoctorId, dto.ClinicId, dto.BrandId);
                if (pending > 0)
                {
                    return Ok(new
                    {
                        IsSuccess = false,
                        Message = $"You have {pending} dose(s) recorded as given with no batch. Include this batch details and deduct the units from this purchase as they were already used?"
                    });
                }
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                int adjustment = dto.Type == "Increase" ? dto.Quantity : -dto.Quantity;

                var row = new AdjustStock
                {
                    BrandId = dto.BrandId,
                    ClinicId = dto.ClinicId,
                    DoctorId = dto.DoctorId,
                    Adjustment = adjustment,
                    Price = dto.Type == "Increase" ? dto.Price : 0,
                    Reason = dto.Reason,
                    Date = dto.Date,
                    BatchLot = dto.BatchLot,
                    ExpiryDate = dto.ExpiryDate
                };
                _db.AdjustStocks.Add(row);
                await _db.SaveChangesAsync(); // need row.Id for the ledger SourceId

                InventoryOperationResult result = dto.Type == "Loss"
                    ? await _inventory.AdjustLoss(dto.DoctorId, dto.ClinicId, dto.BrandId, dto.Quantity, row.Id, dto.BatchLot, dto.Date)
                    : await _inventory.AdjustIncrease(dto.DoctorId, dto.ClinicId, dto.BrandId, dto.Quantity, dto.Price, row.Id, dto.BatchLot, dto.ExpiryDate, dto.Date);

                if (!result.IsSuccess)
                {
                    await tx.RollbackAsync();
                    return Ok(new { IsSuccess = false, Message = result.Message });
                }

                // "Yes": allocate the chosen pending doses to the batch just created (real deduction).
                int claimed = 0;
                if (dto.Type == "Increase" && result.Batch != null)
                {
                    var chosen = dto.ClaimUnbatchedUseIds
                        ?? (dto.ClearUnbatchedBacklog == true
                            ? _inventory.PendingUses(dto.DoctorId, dto.ClinicId, dto.BrandId).Select(u => u.Id).ToList()
                            : null);
                    claimed = _inventory.ClaimPendingForNewBatch(dto.DoctorId, result.Batch, chosen);
                }

                var adjustResponse = new { IsSuccess = true, Message = "Adjustment saved", ResponseData = new { row.Id, ClaimedUnbatched = claimed } };
                Idempotency.Record(_db, dto.DoctorId, dto.ClientRequestId, "adjuststock.create", adjustResponse);
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();

                return Ok(adjustResponse);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // DELETE /api/adjuststock/{id}?paId=&managerId=&callerUserId=&securityStamp=
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(
            long id,
            [FromQuery] long? paId = null,
            [FromQuery] long? managerId = null,
            [FromQuery] long? callerUserId = null,
            [FromQuery] string? securityStamp = null)
        {
            var guard = StockActionGuard.CheckStockAction(
                _db, paId, managerId, callerUserId, securityStamp,
                perm => perm.StockAdjust, "delete a stock adjustment");
            if (!guard.allowed)
                return Ok(new { IsSuccess = false, Message = guard.error });

            var row = await _db.AdjustStocks.FindAsync(id);
            if (row == null)
                return Ok(new { IsSuccess = false, Message = "Adjustment not found" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var rev = await _inventory.ReverseAdjustment(row.DoctorId, row.ClinicId, row.BrandId, row.Adjustment, row.BatchLot, row.Id);
                if (!rev.IsSuccess)
                    return Ok(new { IsSuccess = false, Message = rev.Message });

                _db.AdjustStocks.Remove(row);
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Adjustment deleted" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }
    }
}
