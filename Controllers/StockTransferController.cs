using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using Microsoft.EntityFrameworkCore;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockTransferController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public StockTransferController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpPost]
        public async Task<Response<List<StockTransferHistoryDTO>>> Post([FromBody] StockTransferRequestDTO request)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return new Response<List<StockTransferHistoryDTO>>(false, $"Validation error: {errors}", null);
            }

            if (request.FromClinicId == request.ToClinicId)
                return new Response<List<StockTransferHistoryDTO>>(false, "From and To clinics must be different.", null);

            if (!request.Items.Any())
                return new Response<List<StockTransferHistoryDTO>>(false, "No transfer items provided.", null);

            var fromClinic = await _db.Clinics.FindAsync(request.FromClinicId);
            if (fromClinic == null)
                return new Response<List<StockTransferHistoryDTO>>(false, $"From clinic (ID {request.FromClinicId}) not found.", null);

            var toClinic = await _db.Clinics.FindAsync(request.ToClinicId);
            if (toClinic == null)
                return new Response<List<StockTransferHistoryDTO>>(false, $"To clinic (ID {request.ToClinicId}) not found.", null);

            var doctor = await _db.Doctors.FindAsync(request.DoctorId);
            if (doctor == null)
                return new Response<List<StockTransferHistoryDTO>>(false, "Doctor not found.", null);

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Create a transfer bill in the destination clinic for audit linking
                var transferBill = new Bill
                {
                    BillNo = $"XFER-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Supplier = $"Transfer from {fromClinic.Name}",
                    BillDate = DateTime.UtcNow,
                    IsPaid = true,
                    PaidDate = DateTime.UtcNow,
                    DoctorId = request.DoctorId,
                    ClinicId = request.ToClinicId,
                    IsPAApprove = true
                };
                _db.Bills.Add(transferBill);
                await _db.SaveChangesAsync();

                var transferRecords = new List<StockTransfer>();

                foreach (var item in request.Items)
                {
                    if (item.Quantity <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false, "Transfer quantity must be greater than zero.", null);
                    }

                    var brand = await _db.Brands.FindAsync(item.BrandId);
                    if (brand == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false, $"Brand ID {item.BrandId} not found.", null);
                    }

                    // Verify source has sufficient inventory
                    var sourceBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == item.BrandId && ba.ClinicId == request.FromClinicId);

                    if (sourceBrandAmount == null || sourceBrandAmount.Count < item.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false,
                            $"Insufficient inventory for brand '{brand.Name}'. Available: {sourceBrandAmount?.Count ?? 0}, Requested: {item.Quantity}.", null);
                    }

                    // Deduct from source Stock records in FEFO order (earliest expiry first)
                    var sourceStocks = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == item.BrandId
                                 && s.Bill.ClinicId == request.FromClinicId
                                 && s.Quantity > 0
                                 && (item.BatchNumber == null || (s.BatchLot ?? "").Trim() == item.BatchNumber.Trim()))
                        .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                        .ThenBy(s => s.Expiry)
                        .ThenBy(s => s.Id)
                        .ToListAsync();

                    int remaining = item.Quantity;
                    foreach (var src in sourceStocks)
                    {
                        if (remaining <= 0) break;
                        int deduct = Math.Min(src.Quantity, remaining);
                        src.Quantity -= deduct;
                        remaining -= deduct;
                        if (src.Quantity == 0)
                            _db.Stocks.Remove(src);
                        else
                            _db.Entry(src).State = EntityState.Modified;
                    }

                    if (remaining > 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false,
                            $"Could not deduct full transfer quantity for brand '{brand.Name}' batch '{item.BatchNumber}'.", null);
                    }

                    // Update source BrandAmount
                    sourceBrandAmount.Count -= item.Quantity;
                    _db.Entry(sourceBrandAmount).State = EntityState.Modified;

                    // Add to destination Stock
                    var destStock = new Stock
                    {
                        BrandId = item.BrandId,
                        BillId = transferBill.Id,
                        Quantity = item.Quantity,
                        StockAmount = item.CostPrice,
                        BatchLot = item.BatchNumber?.Trim(),
                        Expiry = item.ExpiryDate
                    };
                    _db.Stocks.Add(destStock);

                    // Update destination BrandAmount
                    var destBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == item.BrandId && ba.ClinicId == request.ToClinicId);

                    if (destBrandAmount != null)
                    {
                        decimal newAvg = destBrandAmount.Count == 0
                            ? item.CostPrice
                            : (destBrandAmount.PurchasedAmt + item.CostPrice) / 2;
                        destBrandAmount.Count += item.Quantity;
                        destBrandAmount.PurchasedAmt = newAvg;
                        _db.Entry(destBrandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        _db.BrandAmounts.Add(new BrandAmount
                        {
                            BrandId = item.BrandId,
                            Count = item.Quantity,
                            DoctorId = request.DoctorId,
                            ClinicId = request.ToClinicId,
                            PurchasedAmt = item.CostPrice
                        });
                    }

                    // Record the transfer
                    var transfer = new StockTransfer
                    {
                        FromClinicId = request.FromClinicId,
                        ToClinicId = request.ToClinicId,
                        BrandId = item.BrandId,
                        BatchNumber = item.BatchNumber?.Trim(),
                        ExpiryDate = item.ExpiryDate,
                        Quantity = item.Quantity,
                        CostPrice = item.CostPrice,
                        TotalValue = item.Quantity * item.CostPrice,
                        CreatedBy = request.DoctorId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.StockTransfers.Add(transfer);
                    transferRecords.Add(transfer);

                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                // Build response DTOs with clinic and brand names
                var resultDtos = transferRecords.Select(t => new StockTransferHistoryDTO
                {
                    Id = t.Id,
                    CreatedAt = t.CreatedAt,
                    FromClinicId = t.FromClinicId,
                    FromClinicName = fromClinic.Name,
                    ToClinicId = t.ToClinicId,
                    ToClinicName = toClinic.Name,
                    BrandId = t.BrandId,
                    BrandName = request.Items.FirstOrDefault(i => i.BrandId == t.BrandId)?.BrandName ?? "",
                    BatchNumber = t.BatchNumber,
                    ExpiryDate = t.ExpiryDate,
                    Quantity = t.Quantity,
                    CostPrice = t.CostPrice,
                    TotalValue = t.TotalValue,
                    CreatedBy = t.CreatedBy,
                    TransferredByName = $"{doctor.FirstName}"
                }).ToList();

                return new Response<List<StockTransferHistoryDTO>>(true, "Stock transferred successfully.", resultDtos);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                string msg = $"Error: {ex.Message}";
                if (ex.InnerException != null) msg += $" | {ex.InnerException.Message}";
                return new Response<List<StockTransferHistoryDTO>>(false, msg, null);
            }
        }

        [HttpGet("history")]
        public Response<List<StockTransferHistoryDTO>> GetHistory(
            [FromQuery] long? fromClinicId,
            [FromQuery] long? toClinicId,
            [FromQuery] long? brandId,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] long? doctorId)
        {
            var query = _db.StockTransfers
                .Include(t => t.FromClinic)
                .Include(t => t.ToClinic)
                .Include(t => t.Brand)
                .AsQueryable();

            if (fromClinicId.HasValue)
                query = query.Where(t => t.FromClinicId == fromClinicId.Value || t.ToClinicId == fromClinicId.Value);

            if (toClinicId.HasValue)
                query = query.Where(t => t.ToClinicId == toClinicId.Value);

            if (brandId.HasValue)
                query = query.Where(t => t.BrandId == brandId.Value);

            if (fromDate.HasValue)
                query = query.Where(t => t.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(t => t.CreatedAt <= toDate.Value.AddDays(1));

            if (doctorId.HasValue)
                query = query.Where(t => t.CreatedBy == doctorId.Value);

            var records = query
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            var dtos = records.Select(t =>
            {
                var dto = _mapper.Map<StockTransferHistoryDTO>(t);
                var doc = _db.Doctors.Find(t.CreatedBy);
                dto.TransferredByName = doc != null ? $"{doc.FirstName}" : "";
                return dto;
            }).ToList();

            return new Response<List<StockTransferHistoryDTO>>(true, null, dtos);
        }
    }
}
