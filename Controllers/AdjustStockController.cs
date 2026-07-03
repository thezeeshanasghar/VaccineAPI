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

        // POST /api/adjuststock
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdjustStockCreateDTO dto)
        {
            if (dto == null)
                return Ok(new { IsSuccess = false, Message = "Invalid request" });
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

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Adjustment saved", ResponseData = new { row.Id } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // DELETE /api/adjuststock/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var row = await _db.AdjustStocks.FindAsync(id);
            if (row == null)
                return Ok(new { IsSuccess = false, Message = "Adjustment not found" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await _inventory.ReverseAdjustment(row.DoctorId, row.ClinicId, row.BrandId, row.Adjustment, row.BatchLot, row.Id);

                _db.AdjustStocks.Remove(row);
                await _db.SaveChangesAsync();
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
