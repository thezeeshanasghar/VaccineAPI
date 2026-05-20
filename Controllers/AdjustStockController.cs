using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdjustStockController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public AdjustStockController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<AdjustStockDTO>>> GetAll()
        {
            var adjustments = await _db.AdjustStocks
                .Include(a => a.Brand)
                .OrderByDescending(a => a.Id)
                .ToListAsync();

            if (!adjustments.Any())
                return new Response<List<AdjustStockDTO>>(false, "No stock adjustments found", null);

            var dtos = BuildHistoryDtos(adjustments);
            return new Response<List<AdjustStockDTO>>(true, null, dtos);
        }

        [HttpGet("{id}")]
        public async Task<Response<AdjustStockDTO>> Get(long id)
        {
            var adjustment = await _db.AdjustStocks
                .Include(a => a.Brand)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (adjustment == null)
                return new Response<AdjustStockDTO>(false, "Stock adjustment not found", null);

            var dto = _mapper.Map<AdjustStockDTO>(adjustment);
            return new Response<AdjustStockDTO>(true, null, dto);
        }

        [HttpGet("history")]
        public async Task<Response<List<AdjustStockDTO>>> GetHistory(
            [FromQuery] long? clinicId,
            [FromQuery] long? brandId,
            [FromQuery] long? doctorId,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var query = _db.AdjustStocks
                .Include(a => a.Brand)
                .AsQueryable();

            if (clinicId.HasValue)
                query = query.Where(a => a.ClinicId == clinicId.Value);
            if (brandId.HasValue)
                query = query.Where(a => a.BrandId == brandId.Value);
            if (doctorId.HasValue)
                query = query.Where(a => a.DoctorId == doctorId.Value);
            if (fromDate.HasValue)
                query = query.Where(a => a.Date >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(a => a.Date <= toDate.Value.AddDays(1));

            var records = await query.OrderByDescending(a => a.Date).ThenByDescending(a => a.Id).ToListAsync();
            var dtos = BuildHistoryDtos(records);
            return new Response<List<AdjustStockDTO>>(true, null, dtos);
        }

        [HttpGet("brand/{brandId}")]
        public async Task<Response<List<AdjustStockDTO>>> GetByBrand(long brandId)
        {
            var adjustments = await _db.AdjustStocks
                .Include(a => a.Brand)
                .Where(a => a.BrandId == brandId)
                .OrderByDescending(a => a.Id)
                .ToListAsync();

            if (!adjustments.Any())
                return new Response<List<AdjustStockDTO>>(false, "No adjustments found for this brand", null);

            var dtos = BuildHistoryDtos(adjustments);
            return new Response<List<AdjustStockDTO>>(true, null, dtos);
        }

        // Single item — kept for backward compatibility
        [HttpPost]
        public async Task<Response<List<AdjustStockDTO>>> Create([FromBody] List<AdjustStockDTO> dtos)
        {
            if (dtos == null || !dtos.Any())
                return new Response<List<AdjustStockDTO>>(false, "No adjustment items provided.", null);

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var results = new List<AdjustStockDTO>();

                foreach (var dto in dtos)
                {
                    if (dto.Adjustment == 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<AdjustStockDTO>>(false, "Adjustment value cannot be zero.", null);
                    }

                    var brandAmount = await _db.BrandAmounts
                        .Include(b => b.Brand)
                        .FirstOrDefaultAsync(b => b.BrandId == dto.BrandId
                                               && b.ClinicId == dto.ClinicId);

                    if (brandAmount == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<AdjustStockDTO>>(false,
                            $"Brand not found in clinic inventory for brand ID {dto.BrandId}.", null);
                    }

                    var newCount = brandAmount.Count + dto.Adjustment;
                    if (newCount < 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<AdjustStockDTO>>(false,
                            $"Insufficient inventory for '{brandAmount.Brand?.Name}'. Current: {brandAmount.Count}, Adjustment: {dto.Adjustment}.", null);
                    }

                    if (dto.Adjustment > 0)
                    {
                        if (string.IsNullOrWhiteSpace(dto.BatchLot) || dto.ExpiryDate == null)
                        {
                            await transaction.RollbackAsync();
                            return new Response<List<AdjustStockDTO>>(false,
                                "Batch/Lot and Expiry date are required for stock increase.", null);
                        }

                        brandAmount.PurchasedAmt = brandAmount.PurchasedAmt == 0 || brandAmount.Count == 0
                            ? dto.Price
                            : ((brandAmount.PurchasedAmt * brandAmount.Count) + (dto.Price * dto.Adjustment))
                              / (brandAmount.Count + dto.Adjustment);

                        // Create or merge a stocks row so batch/expiry tracking stays accurate
                        var anchorBill = await _db.Bills
                            .Where(b => b.ClinicId == dto.ClinicId)
                            .OrderByDescending(b => b.BillDate)
                            .ThenByDescending(b => b.Id)
                            .FirstOrDefaultAsync();

                        if (anchorBill == null)
                        {
                            await transaction.RollbackAsync();
                            return new Response<List<AdjustStockDTO>>(false,
                                $"No purchase bill found for clinic ID {dto.ClinicId}. Please create a purchase bill first before adjusting stock.", null);
                        }

                        var existingStock = await _db.Stocks
                            .Include(s => s.Bill)
                            .Where(s => s.BrandId == dto.BrandId
                                     && s.Bill.ClinicId == dto.ClinicId
                                     && (s.BatchLot ?? "").Trim() == dto.BatchLot.Trim()
                                     && s.Expiry.HasValue && s.Expiry.Value.Date == dto.ExpiryDate.Value.Date)
                            .FirstOrDefaultAsync();

                        if (existingStock != null)
                        {
                            existingStock.Quantity += dto.Adjustment;
                            _db.Entry(existingStock).State = EntityState.Modified;
                        }
                        else
                        {
                            _db.Stocks.Add(new Stock
                            {
                                BrandId          = dto.BrandId,
                                BillId           = anchorBill.Id,
                                Quantity         = dto.Adjustment,
                                OriginalQuantity = dto.Adjustment,
                                StockAmount      = dto.Price,
                                BatchLot         = dto.BatchLot.Trim(),
                                Expiry           = dto.ExpiryDate
                            });
                        }
                    }
                    else
                    {
                        // Stock Loss — deduct from stocks rows in FEFO order (earliest expiry first)
                        int lossRemaining = -dto.Adjustment;
                        var lossLot = string.IsNullOrWhiteSpace(dto.BatchLot) ? null : dto.BatchLot.Trim();
                        var lossStocks = await _db.Stocks
                            .Include(s => s.Bill)
                            .Where(s => s.BrandId == dto.BrandId
                                     && s.Bill.ClinicId == dto.ClinicId
                                     && s.Quantity > 0
                                     && (lossLot == null || (s.BatchLot ?? "").Trim() == lossLot))
                            .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                            .ThenBy(s => s.Expiry)
                            .ThenBy(s => s.Id)
                            .ToListAsync();

                        foreach (var src in lossStocks)
                        {
                            if (lossRemaining <= 0) break;
                            int deduct = Math.Min(src.Quantity, lossRemaining);
                            src.Quantity -= deduct;
                            lossRemaining -= deduct;
                            if (src.Quantity == 0)
                                _db.Stocks.Remove(src);
                            else
                                _db.Entry(src).State = EntityState.Modified;
                        }

                        if (lossRemaining > 0)
                        {
                            await transaction.RollbackAsync();
                            return new Response<List<AdjustStockDTO>>(false,
                                $"Could not deduct full loss quantity for '{brandAmount.Brand?.Name}'" +
                                (lossLot != null ? $" batch '{lossLot}'" : "") +
                                $". Stock rows only covered {-dto.Adjustment - lossRemaining} of {-dto.Adjustment} requested.", null);
                        }
                    }

                    brandAmount.Count = newCount;
                    _db.Entry(brandAmount).State = EntityState.Modified;

                    var adjustment = new AdjustStock
                    {
                        BrandId   = dto.BrandId,
                        ClinicId  = dto.ClinicId,
                        DoctorId  = dto.DoctorId,
                        Adjustment = dto.Adjustment,
                        Reason    = dto.Reason?.Trim().Length > 0 ? dto.Reason : "Stock adjustment",
                        Price     = dto.Price,
                        Date      = dto.Date != default ? dto.Date : DateTime.Now,
                        BatchLot  = dto.BatchLot?.Trim(),
                        ExpiryDate = dto.ExpiryDate
                    };
                    _db.AdjustStocks.Add(adjustment);
                    await _db.SaveChangesAsync();

                    var resultDto = _mapper.Map<AdjustStockDTO>(adjustment);
                    resultDto.BrandName = brandAmount.Brand?.Name ?? "";
                    results.Add(resultDto);
                }

                await transaction.CommitAsync();
                return new Response<List<AdjustStockDTO>>(true,
                    $"{results.Count} item(s) adjusted successfully.", results);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                var inner = ex.InnerException != null ? ex.InnerException.Message : "";
                return new Response<List<AdjustStockDTO>>(false,
                    $"Error: {ex.Message}{(inner.Length > 0 ? " | " + inner : "")}", null);
            }
        }

        private List<AdjustStockDTO> BuildHistoryDtos(List<AdjustStock> records)
        {
            var clinicIds  = records.Select(r => r.ClinicId).Distinct().ToList();
            var doctorIds  = records.Select(r => r.DoctorId).Distinct().ToList();
            var clinics    = _db.Clinics.Where(c => clinicIds.Contains(c.Id)).ToList();
            var doctors    = _db.Doctors.Where(d => doctorIds.Contains(d.Id)).ToList();

            return records.Select(a =>
            {
                var dto = _mapper.Map<AdjustStockDTO>(a);
                dto.BrandName  = a.Brand?.Name ?? "";
                dto.ClinicName = clinics.FirstOrDefault(c => c.Id == a.ClinicId)?.Name ?? "";
                dto.DoctorName = doctors.FirstOrDefault(d => d.Id == a.DoctorId)?.FirstName ?? "";
                return dto;
            }).ToList();
        }
    }
}
