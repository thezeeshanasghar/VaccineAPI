using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public StockController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public Response<List<StockDTO>> Get()
        {
            var stocks = _db.Stocks
                .Include(s => s.Bill)
                .ToList();

            if (!stocks.Any())
                return new Response<List<StockDTO>>(false, "No stocks found", null);

            var stockDTOs = _mapper.Map<List<StockDTO>>(stocks);
            return new Response<List<StockDTO>>(true, null, stockDTOs);
        }

        [HttpGet("{id}")]
        public Response<StockDTO> Get(int id)
        {
            var stock = _db.Stocks
                .Include(s => s.Bill)
                .FirstOrDefault(s => s.Id == id);

            if (stock == null)
                return new Response<StockDTO>(false, "Stock not found", null);

            var stockDTO = _mapper.Map<StockDTO>(stock);
            return new Response<StockDTO>(true, null, stockDTO);
        }

        [HttpGet("bill/{billId}")]
        public Response<List<StockDTO>> GetByBillId(int billId)
        {
            var stocks = _db.Stocks
                .Include(s => s.Bill)
                .Include(s => s.Brand)
                .Where(s => s.BillId == billId)
                .ToList();

            if (!stocks.Any())
                return new Response<List<StockDTO>>(false, "No stocks found for this bill", null);

            var stockDTOs = _mapper.Map<List<StockDTO>>(stocks);
            foreach (var dto in stockDTOs)
            {
                var stock = stocks.First(s => s.Id == dto.Id);
                dto.BillNo = stock.Bill?.BillNo ?? "";
                dto.Supplier = stock.Bill?.Supplier ?? "";
                dto.BillDate = stock.Bill?.BillDate ?? DateTime.MinValue;
                dto.IsPaid = stock.Bill?.IsPaid ?? false;
                dto.PaidDate = stock.Bill?.PaidDate ?? DateTime.MinValue;
                dto.DoctorId = stock.Bill?.DoctorId ?? 0;
                dto.ClinicId = stock.Bill?.ClinicId ?? 0;
            }
            return new Response<List<StockDTO>>(true, null, stockDTOs);
        }

        [HttpGet("latest")]
        public Response<StockDTO> GetLatestByBrand([FromQuery] long brandId, [FromQuery] long clinicId)
        {
            if (brandId <= 0 || clinicId <= 0)
            {
                return new Response<StockDTO>(false, "Invalid brandId or clinicId", null);
            }

            if (!IsInventoryEnabledForClinic(clinicId))
            {
                return new Response<StockDTO>(true, "Inventory is disabled for this clinic.", null);
            }

            var stock = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId);

            var today = DateTime.UtcNow.Date;

            // FEFO: Prefer the nearest upcoming expiry first.
            var stockSelection = stock
                .Where(s => s.Expiry.HasValue && s.Expiry.Value.Date >= today)
                .OrderBy(s => s.Expiry)
                .ThenBy(s => s.Bill.BillDate)
                .ThenBy(s => s.Id)
                .FirstOrDefault();

            // Fallback 1: if all are already expired, still pick the earliest expiry.
            stockSelection ??= stock
                .Where(s => s.Expiry.HasValue)
                .OrderBy(s => s.Expiry)
                .ThenBy(s => s.Bill.BillDate)
                .ThenBy(s => s.Id)
                .FirstOrDefault();

            // Fallback 2: rows without expiry.
            stockSelection ??= stock
                .OrderByDescending(s => s.Bill.BillDate)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();

            if (stockSelection == null)
            {
                return new Response<StockDTO>(false, "Stock not found", null);
            }

            var stockDTO = _mapper.Map<StockDTO>(stockSelection);
            return new Response<StockDTO>(true, null, stockDTO);
        }

        [HttpGet("batch-lots")]
        public Response<List<StockDTO>> GetBatchLotsByBrand([FromQuery] long brandId, [FromQuery] long clinicId)
        {
            if (brandId <= 0 || clinicId <= 0)
            {
                return new Response<List<StockDTO>>(false, "Invalid brandId or clinicId", null);
            }

            if (!IsInventoryEnabledForClinic(clinicId))
            {
                return new Response<List<StockDTO>>(true, "Inventory is disabled for this clinic.", new List<StockDTO>());
            }

            var stocks = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId)
                .Where(s => !string.IsNullOrEmpty(s.BatchLot))
                .OrderByDescending(s => s.Bill.BillDate)
                .ThenByDescending(s => s.Id)
                .ToList();

            if (!stocks.Any())
            {
                return new Response<List<StockDTO>>(true, null, new List<StockDTO>());
            }

            var batchLots = stocks
                .GroupBy(s => s.BatchLot.Trim())
                .Select(g => g.First())
                .Select(s => new StockDTO
                {
                    BatchLot = s.BatchLot,
                    Expiry = s.Expiry,
                    BrandId = s.BrandId
                })
                .OrderBy(s => s.BatchLot)
                .ToList();

            return new Response<List<StockDTO>>(true, null, batchLots);
        }

        [HttpPost]
        public async Task<Response<List<StockDTO>>> Post([FromBody] List<StockDTO> stockDTOs)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return new Response<List<StockDTO>>(false, $"Validation error: {errors}", null);
            }

            if (!stockDTOs.Any())
            {
                return new Response<List<StockDTO>>(false, "No stocks provided", null);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var firstStock = stockDTOs.First();
                var resolvedClinicId = firstStock.ClinicId;

                // Validate doctor exists
                var doctor = await _db.Doctors.FindAsync(firstStock.DoctorId);
                if (doctor == null)
                {
                    return new Response<List<StockDTO>>(false, "Doctor not found", null);
                }

                if (resolvedClinicId <= 0)
                {
                    return new Response<List<StockDTO>>(false, "ClinicId is required", null);
                }

                // Validate clinic exists
                var clinicExists = await _db.Clinics.AnyAsync(c => c.Id == resolvedClinicId);
                if (!clinicExists)
                {
                    return new Response<List<StockDTO>>(false, $"Clinic not found for ClinicId {resolvedClinicId}", null);
                }

                // Create Bill
                var bill = new Bill
                {
                    BillNo = firstStock.BillNo,
                    Supplier = firstStock.Supplier?.Trim() ?? "",
                    BillDate = firstStock.BillDate != default ? firstStock.BillDate : DateTime.Now,
                    IsPaid = firstStock.IsPaid,
                    DoctorId = firstStock.DoctorId,
                    PaidDate = firstStock.PaidDate,
                    ClinicId = resolvedClinicId,
                    IsPAApprove = firstStock.IsPAApprove,
                };

                _db.Bills.Add(bill);
                await _db.SaveChangesAsync();

                var resultStocks = new List<StockDTO>();

                foreach (var stockDTO in stockDTOs)
                {
                    // Validate stock data
                    if (stockDTO.StockAmount <= 0 || stockDTO.Quantity <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,
                            "StockAmount and Quantity must be greater than zero.", null);
                    }

                    // Validate brand exists
                    var brand = await _db.Brands.FindAsync(stockDTO.BrandId);
                    if (brand == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,
                            $"Brand with ID {stockDTO.BrandId} not found", null);
                    }

                    var stock = new Stock
                    {
                        BrandId = stockDTO.BrandId,
                        BillId = bill.Id,
                        Quantity = stockDTO.Quantity,
                        StockAmount = stockDTO.StockAmount,
                        BatchLot = stockDTO.BatchLot?.Trim(),
                        Expiry = stockDTO.Expiry
                    };

                    _db.Stocks.Add(stock);

                    var effectiveClinicId = stockDTO.ClinicId > 0 ? stockDTO.ClinicId : resolvedClinicId;
                    if (effectiveClinicId <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,
                            "ClinicId is required to save stock.", null);
                    }

                    if (effectiveClinicId != resolvedClinicId)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,
                            "All stocks in the same bill must have the same ClinicId.", null);
                    }

                    // Update or Create BrandAmount
                    var brandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == stockDTO.BrandId && ba.ClinicId == effectiveClinicId
                    );
                    decimal unitPrice = 0;
                    if (brandAmount == null || brandAmount.PurchasedAmt == 0)
                    {
                        unitPrice = stockDTO.StockAmount;
                    }
                    else
                    {
                        unitPrice = (brandAmount.PurchasedAmt + stockDTO.StockAmount) / 2;
                    }

                    if (brandAmount != null)
                    {
                        brandAmount.Count += stock.Quantity;
                        brandAmount.PurchasedAmt = unitPrice;
                        brandAmount.DoctorId = stockDTO.DoctorId;
                        brandAmount.ClinicId = effectiveClinicId;
                        _db.Entry(brandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        brandAmount = new BrandAmount
                        {
                            BrandId = stock.BrandId,
                            Count = stock.Quantity,
                            DoctorId = stockDTO.DoctorId,
                            ClinicId = effectiveClinicId,
                            PurchasedAmt = (int)unitPrice
                        };
                        _db.BrandAmounts.Add(brandAmount);
                    }

                    await _db.SaveChangesAsync();

                    // Get result with all relationships
                    var resultStock = await _db.Stocks
                        .Include(s => s.Bill)
                        .Include(s => s.Brand)
                        .FirstOrDefaultAsync(s => s.Id == stock.Id);

                    var resultDto = _mapper.Map<StockDTO>(resultStock);
                    resultDto.IsPaid = bill.IsPaid;
                    resultStocks.Add(resultDto);
                }

                await transaction.CommitAsync();

                var message = $"Stocks created successfully. Bill #{bill.BillNo} " +
                    $"{(bill.IsPaid ? "is paid" : "is pending payment")}. " +
                    $"Total items: {resultStocks.Count}";

                return new Response<List<StockDTO>>(true, message, resultStocks);
            }
            catch (Exception ex)
            {
                // Stringify the exception message and any inner exception message 
                string errorMessage = $"Error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner Exception: {ex.InnerException.Message}";
                }
                var clinicIds = stockDTOs.Select(s => s.ClinicId).Distinct().ToList();
                errorMessage += $" | ClinicIds in payload: [{string.Join(",", clinicIds)}]";
                await transaction.RollbackAsync();
                return new Response<List<StockDTO>>(false, errorMessage, null);
            }
        }

        // [HttpPut("{id}")]
        // public Response<StockDTO> Put(int id, StockDTO stockDTO)
        // {
        //     if (id != stockDTO.Id)
        //         return new Response<StockDTO>(false, "ID mismatch", null);

        //     var stock = _mapper.Map<Stock>(stockDTO);
        //     _db.Entry(stock).State = EntityState.Modified;
        //     _db.SaveChanges();
        //     return new Response<StockDTO>(true, "Stock updated successfully", stockDTO);
        // }

        // [HttpPut("{id}")]
        // public async Task<Response<StockDTO>> Put(int id, [FromBody] StockDTO stockDTO)
        // {
        //     if (id != stockDTO.Id)
        //         return new Response<StockDTO>(false, "ID mismatch", null);

        //     if (!ModelState.IsValid)
        //     {
        //         var errors = string.Join("; ", ModelState.Values
        //             .SelectMany(v => v.Errors)
        //             .Select(e => e.ErrorMessage));
        //         return new Response<StockDTO>(false, $"Validation error: {errors}", null);
        //     }

        //     using var transaction = await _db.Database.BeginTransactionAsync();
        //     try
        //     {
        //         // Find the stock
        //         var stock = await _db.Stocks
        //             .Include(s => s.Bill)
        //             .FirstOrDefaultAsync(s => s.Id == id);

        //         if (stock == null)
        //             return new Response<StockDTO>(false, "Stock not found", null);

        //         // Update stock details
        //         stock.BrandId = stockDTO.BrandId;
        //         stock.Quantity = stockDTO.Quantity;
        //         stock.StockAmount = stockDTO.StockAmount;

        //         _db.Entry(stock).State = EntityState.Modified;

        //         // Update the associated Bill if provided
        //         if (stock.Bill != null)
        //         {
        //             stock.Bill.BillNo = stockDTO.BillNo;
        //             stock.Bill.Supplier = stockDTO.Supplier?.Trim() ?? stock.Bill.Supplier;
        //             stock.Bill.BillDate = stockDTO.BillDate != default ? stockDTO.BillDate : stock.Bill.BillDate;
        //             stock.Bill.IsPaid = stockDTO.IsPaid;
        //             stock.Bill.PaidDate = stockDTO.PaidDate != default ? stockDTO.PaidDate : stock.Bill.PaidDate;
        //             stock.Bill.DoctorId = stockDTO.DoctorId != default ? stockDTO.DoctorId : stock.Bill.DoctorId;

        //             _db.Entry(stock.Bill).State = EntityState.Modified;
        //         }

        //         // Update or create BrandAmount
        //         var brandAmount = await _db.BrandAmounts
        //             .FirstOrDefaultAsync(ba => ba.BrandId == stockDTO.BrandId
        //                 && ba.ClinicId == stockDTO.ClinicId);

        //         decimal unitPrice = Math.Round(stockDTO.StockAmount, 2);

        //         if (brandAmount != null)
        //         {
        //             brandAmount.Count = stockDTO.Quantity;
        //             brandAmount.PurchasedAmt = (int)unitPrice;
        //             _db.Entry(brandAmount).State = EntityState.Modified;
        //         }
        //         else
        //         {
        //             brandAmount = new BrandAmount
        //             {
        //                 BrandId = stock.BrandId,
        //                 Count = stock.Quantity,
        //                 DoctorId = stockDTO.DoctorId,
        //                 PurchasedAmt = (int)unitPrice
        //             };
        //             _db.BrandAmounts.Add(brandAmount);
        //         }

        //         await _db.SaveChangesAsync();
        //         await transaction.CommitAsync();

        //         // Fetch updated stock with relationships
        //         var updatedStock = await _db.Stocks
        //             .Include(s => s.Bill)
        //             .Include(s => s.Brand)
        //                 .ThenInclude(b => b.Vaccine)
        //             .FirstOrDefaultAsync(s => s.Id == stock.Id);

        //         var resultDto = _mapper.Map<StockDTO>(updatedStock);
        //         resultDto.IsPaid = updatedStock.Bill?.IsPaid ?? false;

        //         return new Response<StockDTO>(true, "Stock and Bill updated successfully", resultDto);
        //     }
        //     catch (Exception ex)
        //     {
        //         await transaction.RollbackAsync();
        //         string errorMessage = $"Error: {ex.Message}";
        //         if (ex.InnerException != null)
        //         {
        //             errorMessage += $" | Inner Exception: {ex.InnerException.Message}";
        //         }
        //         return new Response<StockDTO>(false, errorMessage, null);
        //     }
        // }

        [HttpDelete("{id}")]
        public Response<StockDTO> Delete(int id)
        {
            var stock = _db.Stocks.Find(id);
            if (stock == null)
                return new Response<StockDTO>(false, "Stock not found", null);

            _db.Stocks.Remove(stock);
            _db.SaveChanges();
            return new Response<StockDTO>(true, "Stock deleted successfully", null);
        }

        [HttpPut]
        public async Task<Response<List<StockDTO>>> Edit([FromBody] List<StockDTO> stockDTOs)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ",ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return new Response<List<StockDTO>>(false, $"Validation error: {errors}", null);
            }

            if (!stockDTOs.Any())
            {
                return new Response<List<StockDTO>>(false, "No stocks provided", null);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var firstStock = stockDTOs.First();
                // Validate doctor exists
                var doctor = await _db.Doctors.FindAsync(firstStock.DoctorId);
                if (doctor == null)
                {
                    return new Response<List<StockDTO>>(false, "Doctor not found", null);
                }

                var resultStocks = new List<StockDTO>();

                foreach (var stockDTO in stockDTOs)
                {
                    // Validate stock exists
                    var stock = await _db.Stocks.Include(s => s.Bill)
                        .FirstOrDefaultAsync(s => s.Id == stockDTO.Id);

                    if (stock == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,$"Stock with ID {stockDTO.Id} not found",null);
                    }

                    // Validate stock data
                    if (stockDTO.StockAmount <= 0 || stockDTO.Quantity <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,"StockAmount and Quantity must be greater than zero.",null);
                    }

                    // Validate brand exists
                    var brand = await _db.Brands.FindAsync(stockDTO.BrandId);
                    if (brand == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,$"Brand with ID {stockDTO.BrandId} not found",null);
                    }

                    // Update stock details
                    stock.BrandId = stockDTO.BrandId;
                    stock.Quantity = stockDTO.Quantity;
                    stock.StockAmount = stockDTO.StockAmount;
                    stock.BatchLot = string.IsNullOrWhiteSpace(stockDTO.BatchLot)
                        ? stock.BatchLot
                        : stockDTO.BatchLot.Trim();
                    stock.Expiry = stockDTO.Expiry ?? stock.Expiry;

                    _db.Entry(stock).State = EntityState.Modified;

                    // Update the associated Bill if provided
                    if (stock.Bill != null)
                    {
                        stock.Bill.BillNo = stockDTO.BillNo;
                        stock.Bill.Supplier = stockDTO.Supplier?.Trim() ?? stock.Bill.Supplier;
                        stock.Bill.BillDate = stockDTO.BillDate != default ? stockDTO.BillDate : stock.Bill.BillDate;
                        stock.Bill.IsPaid = stockDTO.IsPaid;
                        stock.Bill.PaidDate = stockDTO.PaidDate != default ? stockDTO.PaidDate : stock.Bill.PaidDate;
                        stock.Bill.DoctorId = stockDTO.DoctorId != default ? stockDTO.DoctorId : stock.Bill.DoctorId;
                        stock.Bill.ClinicId = stockDTO.ClinicId != default ? stockDTO.ClinicId : stock.Bill.ClinicId;

                        _db.Entry(stock.Bill).State = EntityState.Modified;
                    }

                    var effectiveClinicId = stockDTO.ClinicId > 0
                        ? stockDTO.ClinicId
                        : (stock.Bill != null ? stock.Bill.ClinicId : 0);
                    if (effectiveClinicId <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false,
                            "ClinicId is required to update stock.", null);
                    }

                    // Update or create BrandAmount
                    var brandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == stockDTO.BrandId && ba.ClinicId == effectiveClinicId
                    );

                    decimal unitPrice = Math.Round(stockDTO.StockAmount, 2);

                    if (brandAmount != null)
                    {
                        brandAmount.Count = stockDTO.Quantity;
                        brandAmount.PurchasedAmt = (int)unitPrice;
                        brandAmount.ClinicId = effectiveClinicId;
                        _db.Entry(brandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        brandAmount = new BrandAmount
                        {
                            BrandId = stock.BrandId,
                            Count = stock.Quantity,
                            DoctorId = stockDTO.DoctorId,
                            ClinicId = effectiveClinicId,
                            PurchasedAmt = (int)unitPrice,
                        };
                        _db.BrandAmounts.Add(brandAmount);
                    }

                    await _db.SaveChangesAsync();

                    // Fetch updated stock with relationships
                    var updatedStock = await _db
                        .Stocks.Include(s => s.Bill)
                        .Include(s => s.Brand)
                        .FirstOrDefaultAsync(s => s.Id == stock.Id);

                    if (updatedStock == null)
                    {
                        continue;
                    }

                    var resultDto = _mapper.Map<StockDTO>(updatedStock);
                    resultDto.IsPaid = updatedStock.Bill?.IsPaid ?? false;
                    resultStocks.Add(resultDto);
                }

                await transaction.CommitAsync();

                var message = $"Stocks updated successfully. Total items: {resultStocks.Count}";

                return new Response<List<StockDTO>>(true, message, resultStocks);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                string errorMessage = $"Error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner Exception: {ex.InnerException.Message}";
                }
                return new Response<List<StockDTO>>(false, errorMessage, null);
            }
        }

        private bool IsInventoryEnabledForClinic(long clinicId)
        {
            if (clinicId <= 0)
            {
                return true;
            }

            var allowInventory = _db.Clinics
                .Where(c => c.Id == clinicId)
                .Select(c => (bool?)c.Doctor.AllowInventory)
                .FirstOrDefault();

            return allowInventory ?? true;
        }
    }
}