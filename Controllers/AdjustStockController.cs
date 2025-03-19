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
                    .ThenInclude(b => b.Vaccine)
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
                    .ThenInclude(b => b.Vaccine)
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
                        .ThenInclude(b => b.Vaccine)
                    .FirstOrDefaultAsync(b => b.BrandId == dto.BrandId);

                if (brandAmount == null)
                {
                    return new Response<AdjustStockDTO>(false, "Brand amount not found", null);
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

                // Create adjustment record
                var adjustment = new AdjustStock
                {
                    BrandId = dto.BrandId,
                    Adjustment = dto.Adjustment,
                    Reason = dto.Reason ?? "Stock adjustment",
                    Date = dto.Date != default ? dto.Date : DateTime.Now  // Add this line
                };

                _db.AdjustStocks.Add(adjustment);

                // Update brand amount count
                brandAmount.Count = newCount;
                _db.Entry(brandAmount).State = EntityState.Modified;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Map result with included data
                var resultDto = _mapper.Map<AdjustStockDTO>(adjustment);
                resultDto.BrandName = brandAmount.Brand?.Name;
                resultDto.VaccineName = brandAmount.Brand?.Vaccine?.Name;

                return new Response<AdjustStockDTO>(true,
                    $"Stock adjusted successfully. New count: {newCount}", resultDto);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return new Response<AdjustStockDTO>(false,
                    "Concurrent update detected. Please refresh and try again.", null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new Response<AdjustStockDTO>(false,
                    $"Error adjusting stock: {ex.Message}", null);
            }
        }

        [HttpGet("brand/{brandId}")]
        public async Task<Response<List<AdjustStockDTO>>> GetByBrand(long brandId)
        {
            var adjustments = await _db.AdjustStocks
                .Include(a => a.Brand)
                    .ThenInclude(b => b.Vaccine)
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