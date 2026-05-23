using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockTransferController : ControllerBase
    {
        private readonly Context _db;

        public StockTransferController(Context db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StockTransferCreateDTO dto)
        {
            if (dto.Quantity <= 0)
                return Ok(new { IsSuccess = false, Message = "Quantity must be greater than 0" });
            if (dto.FromClinicId == dto.ToClinicId)
                return Ok(new { IsSuccess = false, Message = "Source and destination clinics must be different" });
            if (string.IsNullOrWhiteSpace(dto.BatchLot))
                return Ok(new { IsSuccess = false, Message = "Batch is required" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Source BrandAmount
                var sourceBa = await _db.BrandAmounts
                    .FirstOrDefaultAsync(b => b.BrandId == dto.BrandId && b.DoctorId == dto.DoctorId && b.ClinicId == dto.FromClinicId);
                if (sourceBa == null || sourceBa.Count == 0)
                    return Ok(new { IsSuccess = false, Message = "No stock available for this brand at the source clinic" });

                // Source Stock row — find via Bill.ClinicId (same pattern as AdjustStockController)
                var sourceStock = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == dto.BrandId && s.BillId != null && s.BatchLot == dto.BatchLot && s.Quantity > 0 && s.Bill.ClinicId == dto.FromClinicId)
                    .FirstOrDefaultAsync();

                if (sourceStock == null)
                    return Ok(new { IsSuccess = false, Message = "Batch not found or has no remaining stock at the source clinic" });
                if (dto.Quantity > sourceStock.Quantity)
                    return Ok(new { IsSuccess = false, Message = $"Cannot transfer more than available ({sourceStock.Quantity}) in this batch" });

                // Decrement source
                sourceStock.Quantity -= dto.Quantity;
                sourceBa.Count = Math.Max(0, sourceBa.Count - dto.Quantity);

                // Destination BrandAmount — create if not exists
                var destBa = await _db.BrandAmounts
                    .FirstOrDefaultAsync(b => b.BrandId == dto.BrandId && b.DoctorId == dto.DoctorId && b.ClinicId == dto.ToClinicId);
                if (destBa == null)
                {
                    destBa = new BrandAmount
                    {
                        BrandId = dto.BrandId,
                        DoctorId = dto.DoctorId,
                        ClinicId = dto.ToClinicId,
                        Count = 0,
                        Amount = sourceBa.Amount,
                        PurchasedAmt = 0
                    };
                    _db.BrandAmounts.Add(destBa);
                    await _db.SaveChangesAsync();
                }
                destBa.Count += dto.Quantity;

                // Destination Stock row (BillId = null — no purchase bill)
                var destStock = new Stock
                {
                    BrandId = dto.BrandId,
                    Quantity = dto.Quantity,
                    OriginalQuantity = dto.Quantity,
                    StockAmount = sourceStock.StockAmount,
                    BillId = null,
                    BatchLot = dto.BatchLot,
                    Expiry = dto.ExpiryDate
                };
                _db.Stocks.Add(destStock);

                // StockTransfer record
                var transfer = new StockTransfer
                {
                    DoctorId = dto.DoctorId,
                    FromClinicId = dto.FromClinicId,
                    ToClinicId = dto.ToClinicId,
                    BrandId = dto.BrandId,
                    BatchLot = dto.BatchLot,
                    ExpiryDate = dto.ExpiryDate,
                    Quantity = dto.Quantity,
                    Reason = dto.Reason ?? "",
                    TransferDate = dto.TransferDate,
                    CreatedAt = DateTime.UtcNow
                };
                _db.StockTransfers.Add(transfer);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Transfer recorded", ResponseData = new { transfer.Id } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Failed to save transfer: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            var rows = await _db.StockTransfers
                .Include(t => t.Brand)
                .Include(t => t.FromClinic)
                .Include(t => t.ToClinic)
                .Where(t => t.DoctorId == doctorId && (t.FromClinicId == clinicId || t.ToClinicId == clinicId))
                .OrderByDescending(t => t.TransferDate)
                .ToListAsync();

            var brandIds = rows.Select(t => t.BrandId).Distinct().ToList();
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            var result = rows.Select(t =>
            {
                var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == t.BrandId);
                return new StockTransferListDTO
                {
                    Id = t.Id,
                    BrandName = t.Brand != null ? t.Brand.Name : "",
                    VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                    FromClinicName = t.FromClinic != null ? t.FromClinic.Name : "",
                    ToClinicName = t.ToClinic != null ? t.ToClinic.Name : "",
                    BatchLot = t.BatchLot ?? "",
                    ExpiryDate = t.ExpiryDate,
                    Quantity = t.Quantity,
                    Reason = t.Reason ?? "",
                    TransferDate = t.TransferDate
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (transfer == null)
                return Ok(new { IsSuccess = false, Message = "Transfer not found" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Restore source BrandAmount.Count
                var sourceBa = await _db.BrandAmounts
                    .FirstOrDefaultAsync(b => b.BrandId == transfer.BrandId && b.DoctorId == transfer.DoctorId && b.ClinicId == transfer.FromClinicId);
                if (sourceBa != null)
                    sourceBa.Count += transfer.Quantity;

                // Restore source Stock row Quantity
                var sourceStock = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == transfer.BrandId && s.BillId != null && s.BatchLot == transfer.BatchLot && s.Bill.ClinicId == transfer.FromClinicId)
                    .FirstOrDefaultAsync();
                if (sourceStock != null)
                    sourceStock.Quantity += transfer.Quantity;

                // Undo destination — find the transferred Stock row (BillId = null, matching brand + batch)
                var destStock = await _db.Stocks
                    .Where(s => s.BrandId == transfer.BrandId && s.BillId == null && s.BatchLot == transfer.BatchLot)
                    .FirstOrDefaultAsync();
                if (destStock != null)
                {
                    destStock.Quantity = Math.Max(0, destStock.Quantity - transfer.Quantity);
                    if (destStock.Quantity == 0)
                        _db.Stocks.Remove(destStock);
                }

                // Undo destination BrandAmount.Count
                var destBa = await _db.BrandAmounts
                    .FirstOrDefaultAsync(b => b.BrandId == transfer.BrandId && b.DoctorId == transfer.DoctorId && b.ClinicId == transfer.ToClinicId);
                if (destBa != null)
                    destBa.Count = Math.Max(0, destBa.Count - transfer.Quantity);

                _db.StockTransfers.Remove(transfer);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Transfer reversed" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Failed to reverse transfer: " + ex.Message });
            }
        }
    }
}
