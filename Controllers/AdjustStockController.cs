using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdjustStockController : ControllerBase
    {
        private readonly Context _db;
        public AdjustStockController(Context db) { _db = db; }

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
                // Check BrandAmount exists and has stock
                var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                    x.BrandId == dto.BrandId &&
                    x.DoctorId == dto.DoctorId &&
                    x.ClinicId == dto.ClinicId);

                if (ba == null || ba.Count == 0)
                    return Ok(new { IsSuccess = false, Message = "No stock available for this brand at this clinic" });

                if (dto.Type == "Loss")
                {
                    // Find the specific Stock row by brand + clinic + batch
                    var stockRow = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == dto.BrandId &&
                                    s.Bill.ClinicId == dto.ClinicId &&
                                    s.BatchLot == dto.BatchLot &&
                                    s.Quantity > 0)
                        .FirstOrDefaultAsync();

                    if (stockRow == null)
                        return Ok(new { IsSuccess = false, Message = "Batch not found or has no remaining stock" });

                    if (dto.Quantity > stockRow.Quantity)
                        return Ok(new { IsSuccess = false, Message = $"Cannot reduce more than available quantity ({stockRow.Quantity}) in this batch" });

                    // Decrement batch-level stock
                    stockRow.Quantity -= dto.Quantity;

                    // Decrement BrandAmount count
                    ba.Count = Math.Max(0, ba.Count - dto.Quantity);
                }
                else
                {
                    // Increase — just add to BrandAmount count
                    ba.Count += dto.Quantity;
                }

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
                var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                    x.BrandId == row.BrandId &&
                    x.DoctorId == row.DoctorId &&
                    x.ClinicId == row.ClinicId);

                if (ba != null)
                {
                    if (row.Adjustment > 0)
                    {
                        // Was an Increase — reverse by subtracting
                        ba.Count = Math.Max(0, ba.Count - row.Adjustment);
                    }
                    else
                    {
                        // Was a Loss — reverse by adding back
                        ba.Count += Math.Abs(row.Adjustment);
                    }
                }

                // For Loss: also restore the batch-level Stock row
                if (row.Adjustment < 0 && !string.IsNullOrEmpty(row.BatchLot))
                {
                    var stockRow = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == row.BrandId &&
                                    s.Bill.ClinicId == row.ClinicId &&
                                    s.BatchLot == row.BatchLot)
                        .FirstOrDefaultAsync();

                    if (stockRow != null)
                        stockRow.Quantity += Math.Abs(row.Adjustment);
                }

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
