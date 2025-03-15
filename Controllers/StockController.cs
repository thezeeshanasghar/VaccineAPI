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
                dto.Date = stock.Bill?.Date ?? DateTime.MinValue;
                dto.IsPaid = stock.Bill?.IsPaid ?? false;
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

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Create or get the Bill first
                var firstStock = stockDTOs.First();
                var bill = new Bill
                {
                    BillNo = firstStock.BillNo,
                    Supplier = firstStock.Supplier ?? "",
                    Date = firstStock.Date != default ? firstStock.Date : DateTime.Now,
                    IsPaid = firstStock.IsPaid
                };

                _db.Bills.Add(bill);
                await _db.SaveChangesAsync();

                var resultStocks = new List<StockDTO>();

                foreach (var stockDTO in stockDTOs)
                {
                    if (stockDTO.StockAmount <= 0 || stockDTO.Quantity <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockDTO>>(false, "StockAmount and Quantity must be greater than zero.", null);
                    }

                    var stock = new Stock
                    {
                        BrandId = stockDTO.BrandId,
                        BillId = bill.Id, // Use the new bill's ID
                        Quantity = stockDTO.Quantity,
                        StockAmount = stockDTO.StockAmount
                    };

                    _db.Stocks.Add(stock);

                    var brandAmount = await _db.BrandAmounts
                        .FirstOrDefaultAsync(ba => ba.BrandId == stockDTO.BrandId);

                    decimal unitPrice = Math.Round(stock.StockAmount);

                    if (brandAmount != null)
                    {
                        brandAmount.Count += stock.Quantity;
                        brandAmount.PurchasedAmt = (int)unitPrice;
                        _db.Entry(brandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        brandAmount = new BrandAmount
                        {
                            BrandId = stock.BrandId,
                            Count = stock.Quantity,
                            PurchasedAmt = (int)unitPrice
                        };
                        _db.BrandAmounts.Add(brandAmount);
                    }

                    await _db.SaveChangesAsync();

                    var resultStock = await _db.Stocks
                        .Include(s => s.Bill)
                        .Include(s => s.Brand)
                            .ThenInclude(b => b.Vaccine)
                        .FirstOrDefaultAsync(s => s.Id == stock.Id);

                    var resultDto = _mapper.Map<StockDTO>(resultStock);
                    resultDto.IsPaid = bill.IsPaid; // Include IsPaid in response
                    resultStocks.Add(resultDto);
                }

                await transaction.CommitAsync();

                return new Response<List<StockDTO>>(true,
                    $"Stocks created successfully. Bill #{bill.BillNo} {(bill.IsPaid ? "is paid" : "is pending payment")}.",
                    resultStocks);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new Response<List<StockDTO>>(false, $"Error creating stocks: {ex.Message}", null);
            }
        }

        [HttpPut("{id}")]
        public Response<StockDTO> Put(int id, StockDTO stockDTO)
        {
            if (id != stockDTO.Id)
                return new Response<StockDTO>(false, "ID mismatch", null);

            var stock = _mapper.Map<Stock>(stockDTO);
            _db.Entry(stock).State = EntityState.Modified;
            _db.SaveChanges();
            return new Response<StockDTO>(true, "Stock updated successfully", stockDTO);
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