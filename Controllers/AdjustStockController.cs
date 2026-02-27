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
            {
                return new Response<List<AdjustStockDTO>>(false, "No stock adjustments found", null);
            }

            var dtos = _mapper.Map<List<AdjustStockDTO>>(adjustments);
            return new Response<List<AdjustStockDTO>>(true, null, dtos);
        }

        [HttpGet("{id}")]
        public async Task<Response<AdjustStockDTO>> Get(long id)
        {
            var adjustment = await _db.AdjustStocks
                .Include(a => a.Brand)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (adjustment == null)
            {
                return new Response<AdjustStockDTO>(false, "Stock adjustment not found", null);
            }

            var dto = _mapper.Map<AdjustStockDTO>(adjustment);
            return new Response<AdjustStockDTO>(true, null, dto);
        }

        [HttpPost]
        public async Task<Response<AdjustStockDTO>> Create(AdjustStockDTO dto)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Validate brand exists and include related data
                var brandAmount = await _db.BrandAmounts
                    .Include(b => b.Brand)
                    .FirstOrDefaultAsync(b => b.BrandId == dto.BrandId && b.DoctorId == dto.DoctorId && b.ClinicId == dto.ClinicId);

                if (brandAmount == null)
                {
                    return new Response<AdjustStockDTO>(false,
                        $"Brand amount not found for Doctor ID {dto.DoctorId}", null);
                }

                // Validate adjustment value
                if (dto.Adjustment == 0)
                {
                    return new Response<AdjustStockDTO>(false, "Adjustment value cannot be zero", null);
                }

                // Check if adjustment would result in negative inventory
                var newCount = brandAmount.Count + dto.Adjustment;
                if (newCount < 0)
                {
                    return new Response<AdjustStockDTO>(false,
                        $"Insufficient inventory. Current: {brandAmount.Count}, Adjustment: {dto.Adjustment}", null);
                }
                if (dto.Adjustment > 0)
                {
                    if (brandAmount.PurchasedAmt == 0)
                    {
                        brandAmount.PurchasedAmt = dto.Price;
                    }
                    else
                    {
                        brandAmount.PurchasedAmt =((brandAmount.PurchasedAmt * brandAmount.Count)+ (dto.Price * dto.Adjustment))
                         / (brandAmount.Count + dto.Adjustment);
                    }
                }
                var adjustment = new AdjustStock
                {
                    BrandId = dto.BrandId,
                    Adjustment = dto.Adjustment,
                    Reason = dto.Reason ?? "Stock adjustment",
                    Price = dto.Price,
                    Date = dto.Date != default ? dto.Date : DateTime.Now,
                    ClinicId = dto.ClinicId,
                };
                _db.AdjustStocks.Add(adjustment);
                
                // brandAmount.PurchasedAmt = dto.Price;
                brandAmount.Count = newCount;
                _db.Entry(brandAmount).State = EntityState.Modified;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Map result with included data
                var resultDto = _mapper.Map<AdjustStockDTO>(adjustment);
                resultDto.BrandName = brandAmount.Brand?.Name;
                resultDto.VaccineName = "";

                return new Response<AdjustStockDTO>(true,
                    $"Stock adjusted successfully for Doctor ID {dto.DoctorId}. New count: {newCount}",
                    resultDto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                var innerExceptionMessage =ex.InnerException != null ? ex.InnerException.Message : "No inner exception";
                return new Response<AdjustStockDTO>(false,$"Error adjusting stock: {ex.Message}. Inner Exception: {innerExceptionMessage}",null);
            }
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
            {
                return new Response<List<AdjustStockDTO>>(false, "No adjustments found for this brand", null);
            }

            var dtos = _mapper.Map<List<AdjustStockDTO>>(adjustments);
            return new Response<List<AdjustStockDTO>>(true, null, dtos);
        }
    }
}