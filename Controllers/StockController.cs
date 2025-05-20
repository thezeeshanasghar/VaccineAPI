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
                    .ThenInclude(b => b.Vaccine)
                .Where(s => s.BillId == billId)
                .ToList();

            if (!stocks.Any())
                return new Response<List<StockDTO>>(false, "No stocks found for this bill", null);

            var stockDTOs = _mapper.Map<List<StockDTO>>(stocks);
            foreach (var dto in stockDTOs)
            {
                var stock = stocks.First(s => s.Id == dto.Id);
                dto.BillNo = stock.Bill?.BillNo;
                dto.Supplier = stock.Bill?.Supplier;
                dto.BillDate = stock.Bill?.BillDate ?? DateTime.MinValue;
                dto.IsPaid = stock.Bill?.IsPaid ?? false;
                dto.PaidDate = stock.Bill?.PaidDate ?? DateTime.MinValue;
                dto.DoctorId = stock.Bill?.DoctorId ?? 0;
            }
            return new Response<List<StockDTO>>(true, null, stockDTOs);
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
                // Validate doctor exists
                var doctor = await _db.Doctors.FindAsync(firstStock.DoctorId);
                if (doctor == null)
                {
                    return new Response<List<StockDTO>>(false, "Doctor not found", null);
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
                    ClinicId = firstStock.ClinicId
                    
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
                        StockAmount = stockDTO.StockAmount
                    };

                    _db.Stocks.Add(stock);

                    // Update or Create BrandAmount
                    var brandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == stockDTO.BrandId && ba.ClinicId == stockDTO.ClinicId
                    );
                    decimal unitPrice = 0;
                    if (brandAmount.PurchasedAmt == 0)
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
                        _db.Entry(brandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        brandAmount = new BrandAmount
                        {
                            BrandId = stock.BrandId,
                            Count = stock.Quantity,
                            DoctorId = stockDTO.DoctorId,
                            PurchasedAmt = (int)unitPrice
                        };
                        _db.BrandAmounts.Add(brandAmount);
                    }

                    await _db.SaveChangesAsync();

                    // Get result with all relationships
                    var resultStock = await _db.Stocks
                        .Include(s => s.Bill)
                        .Include(s => s.Brand)
                            .ThenInclude(b => b.Vaccine)
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

        [HttpPut("{id}")]
        public async Task<Response<StockDTO>> Put(int id, [FromBody] StockDTO stockDTO)
        {
            if (id != stockDTO.Id)
                return new Response<StockDTO>(false, "ID mismatch", null);

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return new Response<StockDTO>(false, $"Validation error: {errors}", null);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Find the stock
                var stock = await _db.Stocks
                    .Include(s => s.Bill)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (stock == null)
                    return new Response<StockDTO>(false, "Stock not found", null);

                // Update stock details
                stock.BrandId = stockDTO.BrandId;
                stock.Quantity = stockDTO.Quantity;
                stock.StockAmount = stockDTO.StockAmount;

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

                    _db.Entry(stock.Bill).State = EntityState.Modified;
                }

                // Update or create BrandAmount
                var brandAmount = await _db.BrandAmounts
                    .FirstOrDefaultAsync(ba => ba.BrandId == stockDTO.BrandId
                        && ba.ClinicId == stockDTO.ClinicId);

                decimal unitPrice = Math.Round(stockDTO.StockAmount, 2);

                if (brandAmount != null)
                {
                    brandAmount.Count = stockDTO.Quantity;
                    brandAmount.PurchasedAmt = (int)unitPrice;
                    _db.Entry(brandAmount).State = EntityState.Modified;
                }
                else
                {
                    brandAmount = new BrandAmount
                    {
                        BrandId = stock.BrandId,
                        Count = stock.Quantity,
                        DoctorId = stockDTO.DoctorId,
                        PurchasedAmt = (int)unitPrice
                    };
                    _db.BrandAmounts.Add(brandAmount);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Fetch updated stock with relationships
                var updatedStock = await _db.Stocks
                    .Include(s => s.Bill)
                    .Include(s => s.Brand)
                        .ThenInclude(b => b.Vaccine)
                    .FirstOrDefaultAsync(s => s.Id == stock.Id);

                var resultDto = _mapper.Map<StockDTO>(updatedStock);
                resultDto.IsPaid = updatedStock.Bill?.IsPaid ?? false;

                return new Response<StockDTO>(true, "Stock and Bill updated successfully", resultDto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                string errorMessage = $"Error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner Exception: {ex.InnerException.Message}";
                }
                return new Response<StockDTO>(false, errorMessage, null);
            }
        }

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
    }
}