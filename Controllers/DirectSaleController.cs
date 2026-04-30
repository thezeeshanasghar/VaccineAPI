using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DirectSaleController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public DirectSaleController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpPost]
        public async Task<Response<DirectSaleDTO>> Create([FromBody] DirectSaleDTO dto)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var brand = await _db.Brands.FindAsync(dto.BrandId);
                if (brand == null)
                    return new Response<DirectSaleDTO>(false, $"Brand ID {dto.BrandId} not found.", null);

                var brandAmount = await _db.BrandAmounts
                    .FirstOrDefaultAsync(ba => ba.BrandId == dto.BrandId && ba.ClinicId == dto.ClinicId);

                if (brandAmount == null || brandAmount.Count < dto.Quantity)
                    return new Response<DirectSaleDTO>(false,
                        $"Insufficient stock for '{brand.Name}'. Available: {brandAmount?.Count ?? 0}.", null);

                // Deduct from Stock records in FEFO order
                var sourceStocks = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == dto.BrandId
                             && s.Bill.ClinicId == dto.ClinicId
                             && s.Quantity > 0
                             && (dto.BatchLot == null || (s.BatchLot ?? "").Trim() == dto.BatchLot.Trim()))
                    .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                    .ThenBy(s => s.Expiry)
                    .ThenBy(s => s.Id)
                    .ToListAsync();

                int remaining = dto.Quantity;
                foreach (var src in sourceStocks)
                {
                    if (remaining <= 0) break;
                    int deduct = System.Math.Min(src.Quantity, remaining);
                    src.Quantity -= deduct;
                    remaining -= deduct;
                    if (src.Quantity == 0) _db.Stocks.Remove(src);
                    else _db.Entry(src).State = EntityState.Modified;
                }

                if (remaining > 0)
                {
                    await transaction.RollbackAsync();
                    return new Response<DirectSaleDTO>(false,
                        $"Could not deduct full quantity for '{brand.Name}'.", null);
                }

                // Update BrandAmount
                brandAmount.Count -= dto.Quantity;
                _db.Entry(brandAmount).State = EntityState.Modified;

                // Compute financials
                decimal purchasePrice = brandAmount.PurchasedAmt;
                decimal totalSale  = dto.Quantity * dto.SalePricePerUnit;
                decimal totalCost  = dto.Quantity * purchasePrice;
                decimal profit     = totalSale - totalCost;

                var sale = new DirectSale
                {
                    BrandId            = dto.BrandId,
                    ClinicId           = dto.ClinicId,
                    DoctorId           = dto.DoctorId,
                    BatchLot           = dto.BatchLot?.Trim(),
                    ExpiryDate         = dto.ExpiryDate,
                    Quantity           = dto.Quantity,
                    SalePricePerUnit   = dto.SalePricePerUnit,
                    PurchasePricePerUnit = purchasePrice,
                    TotalSaleValue     = totalSale,
                    TotalCostValue     = totalCost,
                    Profit             = profit,
                    ClientName         = dto.ClientName?.Trim(),
                    PaymentMode        = dto.PaymentMode ?? "Cash",
                    Notes              = dto.Notes?.Trim(),
                    SaleDate           = dto.SaleDate != default ? dto.SaleDate : System.DateTime.UtcNow
                };
                _db.DirectSales.Add(sale);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = _mapper.Map<DirectSaleDTO>(sale);
                result.BrandName  = brand.Name;
                result.ClinicName = _db.Clinics.Find(dto.ClinicId)?.Name ?? "";
                return new Response<DirectSaleDTO>(true, "Direct sale recorded successfully.", result);
            }
            catch (System.Exception ex)
            {
                await transaction.RollbackAsync();
                return new Response<DirectSaleDTO>(false, $"Error: {ex.Message}", null);
            }
        }

        [HttpGet("history")]
        public Response<List<DirectSaleDTO>> GetHistory(
            [FromQuery] long? clinicId,
            [FromQuery] long? brandId,
            [FromQuery] long? doctorId,
            [FromQuery] System.DateTime? fromDate,
            [FromQuery] System.DateTime? toDate)
        {
            var query = _db.DirectSales
                .Include(d => d.Brand)
                .Include(d => d.Clinic)
                .AsQueryable();

            if (clinicId.HasValue)  query = query.Where(d => d.ClinicId  == clinicId.Value);
            if (brandId.HasValue)   query = query.Where(d => d.BrandId   == brandId.Value);
            if (doctorId.HasValue)  query = query.Where(d => d.DoctorId  == doctorId.Value);
            if (fromDate.HasValue)  query = query.Where(d => d.SaleDate  >= fromDate.Value);
            if (toDate.HasValue)    query = query.Where(d => d.SaleDate  <= toDate.Value.AddDays(1));

            var records = query.OrderByDescending(d => d.SaleDate).ToList();
            var dtos = _mapper.Map<List<DirectSaleDTO>>(records);
            return new Response<List<DirectSaleDTO>>(true, null, dtos);
        }
    }
}
