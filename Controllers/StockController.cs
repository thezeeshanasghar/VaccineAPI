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
                dto.SupplierId = stock.Bill?.SupplierId;
                dto.AwtAmount = stock.Bill?.AwtAmount;
                dto.AmountPaid = stock.Bill?.AmountPaid;
                dto.PaymentMethod = stock.Bill?.PaymentMethod;
                dto.BillDate = stock.Bill?.BillDate ?? DateTime.MinValue;
                dto.IsPaid = stock.Bill?.IsPaid ?? false;
                dto.PaidDate = stock.Bill?.PaidDate;
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
                .Select(s => new
                {
                    BatchLot = (s.BatchLot ?? "").Trim(),
                    Expiry = s.Expiry,
                    BrandId = s.BrandId
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.BatchLot))
                .Distinct()
                .OrderBy(x => x.Expiry.HasValue ? 0 : 1)
                .ThenBy(x => x.Expiry)
                .ThenBy(x => x.BatchLot)
                .Select(x => new StockDTO
                {
                    BatchLot = x.BatchLot,
                    Expiry = x.Expiry,
                    BrandId = x.BrandId
                })
                .ToList();

            return new Response<List<StockDTO>>(true, null, batchLots);
        }

        [HttpGet("available-batches")]
        public Response<List<AvailableBatchDTO>> GetAvailableBatches([FromQuery] long brandId, [FromQuery] long clinicId)
        {
            if (brandId <= 0 || clinicId <= 0)
                return new Response<List<AvailableBatchDTO>>(false, "Invalid brandId or clinicId", null);

            if (!IsInventoryEnabledForClinic(clinicId))
                return new Response<List<AvailableBatchDTO>>(true, "Inventory is disabled for this clinic.", new List<AvailableBatchDTO>());

            var brandAmount = _db.BrandAmounts
                .Where(ba => ba.BrandId == brandId && ba.ClinicId == clinicId)
                .FirstOrDefault();

            var costPrice   = brandAmount?.PurchasedAmt ?? 0;
            var actualTotal = brandAmount?.Count ?? 0;

            var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
            var brandName = brand?.Name ?? "";

            var stocks = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity > 0)
                .ToList();

            // Available quantity per batch = sum of Stock.Quantity rows directly.
            // Stock.Quantity is now decremented by every operation (fill, transfer, adjust, sale).
            var batches = stocks
                .GroupBy(s => new { Lot = (s.BatchLot ?? "").Trim(), s.Expiry })
                .Where(g => !string.IsNullOrWhiteSpace(g.Key.Lot))
                .Select(g => new AvailableBatchDTO
                {
                    BrandId           = brandId,
                    BrandName         = brandName,
                    BatchLot          = g.Key.Lot,
                    Expiry            = g.Key.Expiry,
                    AvailableQuantity = g.Sum(s => s.Quantity),
                    CostPrice         = costPrice
                })
                .Where(b => b.AvailableQuantity > 0)
                .OrderBy(b => b.Expiry.HasValue ? 0 : 1)
                .ThenBy(b => b.Expiry)
                .ThenBy(b => b.BatchLot)
                .ToList();

            if (!batches.Any())
            {
                var noLotStocks = stocks.Where(s => string.IsNullOrWhiteSpace(s.BatchLot)).ToList();
                if (noLotStocks.Any())
                {
                    batches.Add(new AvailableBatchDTO
                    {
                        BrandId           = brandId,
                        BrandName         = brandName,
                        BatchLot          = null,
                        Expiry            = noLotStocks.OrderBy(s => s.Expiry).FirstOrDefault()?.Expiry,
                        AvailableQuantity = actualTotal,
                        CostPrice         = costPrice
                    });
                }
            }

            return new Response<List<AvailableBatchDTO>>(true, null, batches);
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

                // Resolve supplier name from master if SupplierId is provided
                string resolvedSupplierName = firstStock.Supplier?.Trim() ?? "";
                long? resolvedSupplierId = null;
                if (firstStock.SupplierId.HasValue && firstStock.SupplierId.Value > 0)
                {
                    var supplierEntity = await _db.Suppliers.FindAsync(firstStock.SupplierId.Value);
                    if (supplierEntity != null)
                    {
                        resolvedSupplierId = supplierEntity.Id;
                        resolvedSupplierName = supplierEntity.Name;
                    }
                }

                // Calculate total upfront to determine payment status.
                // AWT is part of what is owed to the supplier so it is included in the payable total.
                decimal totalAmount = stockDTOs.Sum(s => s.StockAmount * s.Quantity);
                decimal awtAmount   = firstStock.AwtAmount ?? 0m;
                decimal totalPayable = totalAmount + awtAmount;
                decimal amountPaid = firstStock.AmountPaid ?? 0m;
                bool isPaid = amountPaid > 0 && amountPaid >= totalPayable;
                var billDate = firstStock.BillDate != default ? firstStock.BillDate : DateTime.Now;

                // Create Bill
                var bill = new Bill
                {
                    BillNo = firstStock.BillNo,
                    Supplier = resolvedSupplierName,
                    SupplierId = resolvedSupplierId,
                    BillDate = billDate,
                    IsPaid = isPaid,
                    PaidDate = amountPaid > 0 ? billDate : (DateTime?)null,
                    AmountPaid = amountPaid > 0 ? amountPaid : null,
                    PaymentMethod = amountPaid > 0 ? firstStock.PaymentMethod : null,
                    DoctorId = firstStock.DoctorId,
                    ClinicId = resolvedClinicId,
                    IsPAApprove = firstStock.IsPAApprove,
                    AwtAmount = firstStock.AwtAmount,
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

                    // Calculate AWT-inclusive landed cost per unit before creating stock row.
                    // AWT is distributed proportionally by each item's share of the total bill value.
                    decimal itemTotal    = stockDTO.StockAmount * stockDTO.Quantity;
                    decimal itemAwtShare = totalAmount > 0 ? (itemTotal / totalAmount) * awtAmount : 0m;
                    decimal awtPerUnit   = stockDTO.Quantity > 0 ? itemAwtShare / stockDTO.Quantity : 0m;
                    decimal trueUnitCost = stockDTO.StockAmount + awtPerUnit;

                    var stock = new Stock
                    {
                        BrandId          = stockDTO.BrandId,
                        BillId           = bill.Id,
                        Quantity         = stockDTO.Quantity,
                        OriginalQuantity = stockDTO.Quantity,
                        StockAmount      = trueUnitCost,
                        BatchLot         = stockDTO.BatchLot?.Trim(),
                        Expiry           = stockDTO.Expiry
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

                    // Update or Create BrandAmount.
                    var brandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == stockDTO.BrandId && ba.ClinicId == effectiveClinicId
                    );

                    decimal unitPrice = 0;
                    if (brandAmount == null || brandAmount.Count == 0)
                    {
                        unitPrice = trueUnitCost;
                    }
                    else
                    {
                        unitPrice = ((brandAmount.PurchasedAmt * brandAmount.Count) + (trueUnitCost * stockDTO.Quantity))
                                    / (brandAmount.Count + stockDTO.Quantity);
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
                            PurchasedAmt = unitPrice
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

                // Auto-create SupplierPayment when amount was paid at time of bill
                if (amountPaid > 0 && resolvedSupplierId.HasValue)
                {
                    _db.SupplierPayments.Add(new SupplierPayment
                    {
                        SupplierId = resolvedSupplierId.Value,
                        ClinicId = resolvedClinicId,
                        Amount = amountPaid,
                        PaymentDate = billDate,
                        PaymentMethod = firstStock.PaymentMethod ?? "Cash",
                        Notes = $"Paid at time of bill #{bill.BillNo}",
                        BillId = bill.Id
                    });
                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                string payStatus = amountPaid <= 0 ? "unpaid" : isPaid ? "fully paid" : "partially paid";
                var message = $"Bill #{bill.BillNo} created — {payStatus}. Total items: {resultStocks.Count}";

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
            var stock = _db.Stocks
                .Include(s => s.Bill)
                .FirstOrDefault(s => s.Id == id);
            if (stock == null)
                return new Response<StockDTO>(false, "Stock not found", null);

            var clinicId = stock.Bill != null ? stock.Bill.ClinicId : 0;
            if (clinicId > 0)
            {
                var brandAmount = _db.BrandAmounts
                    .FirstOrDefault(ba => ba.BrandId == stock.BrandId && ba.ClinicId == clinicId);
                if (brandAmount != null)
                {
                    brandAmount.Count = Math.Max(0, brandAmount.Count - stock.Quantity);
                    var remaining = _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == stock.BrandId && s.Bill.ClinicId == clinicId
                                 && s.Id != stock.Id && s.Quantity > 0)
                        .ToList();
                    if (remaining.Any())
                    {
                        var totalQty  = remaining.Sum(s => s.Quantity);
                        var totalCost = remaining.Sum(s => (decimal)s.StockAmount * s.Quantity);
                        brandAmount.PurchasedAmt = totalQty > 0 ? Math.Round(totalCost / totalQty, 2) : 0;
                    }
                    else
                    {
                        brandAmount.PurchasedAmt = 0;
                    }
                    _db.Entry(brandAmount).State = EntityState.Modified;
                }
            }

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

                    // Delta against OriginalQuantity (what was purchased), not the live-decremented Quantity.
                    // This keeps BrandAmount.Count accurate when editing a bill line.
                    int oldQty = stock.OriginalQuantity > 0 ? stock.OriginalQuantity : stock.Quantity;
                    int qtyEditDelta = stockDTO.Quantity - oldQty;

                    // Update stock details
                    stock.BrandId = stockDTO.BrandId;
                    // Apply the delta to live Quantity (not reset to full new value — some units may already be consumed)
                    stock.Quantity = Math.Max(0, stock.Quantity + qtyEditDelta);
                    stock.OriginalQuantity = stockDTO.Quantity;
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
                        stock.Bill.BillDate = stockDTO.BillDate != default ? stockDTO.BillDate : stock.Bill.BillDate;
                        stock.Bill.DoctorId = stockDTO.DoctorId != default ? stockDTO.DoctorId : stock.Bill.DoctorId;
                        stock.Bill.ClinicId = stockDTO.ClinicId != default ? stockDTO.ClinicId : stock.Bill.ClinicId;
                        stock.Bill.AwtAmount = stockDTO.AwtAmount ?? stock.Bill.AwtAmount;

                        if (stockDTO.SupplierId.HasValue && stockDTO.SupplierId.Value > 0)
                        {
                            var supplierEntity = await _db.Suppliers.FindAsync(stockDTO.SupplierId.Value);
                            if (supplierEntity != null)
                            {
                                stock.Bill.SupplierId = supplierEntity.Id;
                                stock.Bill.Supplier = supplierEntity.Name;
                            }
                        }
                        else
                        {
                            stock.Bill.Supplier = stockDTO.Supplier?.Trim() ?? stock.Bill.Supplier;
                        }

                        // Recalculate payment status from AmountPaid.
                        // StockAmount already includes AWT (landed cost). Do NOT add AwtAmount again.
                        if (stockDTO.AmountPaid.HasValue)
                        {
                            var allStocks = await _db.Stocks
                                .Where(s => s.BillId == stock.BillId)
                                .ToListAsync();
                            decimal newTotalPayable = allStocks.Sum(s =>
                                s.Id == stock.Id
                                    ? stockDTO.StockAmount * stockDTO.Quantity
                                    : s.StockAmount * s.Quantity);

                            decimal newAmountPaid = stockDTO.AmountPaid.Value;
                            stock.Bill.AmountPaid = newAmountPaid > 0 ? newAmountPaid : null;
                            stock.Bill.PaymentMethod = newAmountPaid > 0
                                ? (stockDTO.PaymentMethod ?? stock.Bill.PaymentMethod)
                                : null;
                            stock.Bill.IsPaid = newAmountPaid > 0 && newAmountPaid >= newTotalPayable;
                            stock.Bill.PaidDate = newAmountPaid > 0 ? stock.Bill.BillDate : null;

                            // Sync the linked SupplierPayment if supplier is set
                            if (stock.Bill.SupplierId.HasValue)
                            {
                                var existingPmt = await _db.SupplierPayments
                                    .FirstOrDefaultAsync(p => p.BillId == stock.BillId);
                                if (newAmountPaid > 0)
                                {
                                    if (existingPmt != null)
                                    {
                                        existingPmt.Amount = newAmountPaid;
                                        existingPmt.PaymentMethod = stockDTO.PaymentMethod ?? existingPmt.PaymentMethod;
                                        _db.Entry(existingPmt).State = EntityState.Modified;
                                    }
                                    else
                                    {
                                        _db.SupplierPayments.Add(new SupplierPayment
                                        {
                                            SupplierId = stock.Bill.SupplierId.Value,
                                            ClinicId = stock.Bill.ClinicId,
                                            Amount = newAmountPaid,
                                            PaymentDate = stock.Bill.BillDate,
                                            PaymentMethod = stockDTO.PaymentMethod ?? "Cash",
                                            Notes = $"Paid at time of bill #{stock.Bill.BillNo}",
                                            BillId = stock.Bill.Id
                                        });
                                    }
                                }
                                else if (existingPmt != null)
                                {
                                    _db.SupplierPayments.Remove(existingPmt);
                                }
                            }
                        }

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

                    // Recalculate PurchasedAmt as a true weighted average across ALL stocks.
                    // EF's identity map returns the just-updated stock entity with its new
                    // StockAmount, so the average already reflects the corrected price.
                    var allBrandStocks = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == stockDTO.BrandId
                                 && s.Bill.ClinicId == effectiveClinicId
                                 && s.Quantity > 0)
                        .ToListAsync();

                    int     totalPurchased = allBrandStocks.Sum(s => s.Quantity);
                    decimal totalCost      = allBrandStocks.Sum(s => (decimal)s.StockAmount * s.Quantity);
                    decimal avgPrice       = totalPurchased > 0
                        ? Math.Round(totalCost / totalPurchased, 2)
                        : Math.Round(stockDTO.StockAmount, 2);

                    var brandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == stockDTO.BrandId && ba.ClinicId == effectiveClinicId);

                    if (brandAmount != null)
                    {
                        brandAmount.Count        = Math.Max(0, brandAmount.Count + qtyEditDelta);
                        brandAmount.PurchasedAmt = avgPrice;
                        brandAmount.ClinicId     = effectiveClinicId;
                        _db.Entry(brandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        _db.BrandAmounts.Add(new BrandAmount
                        {
                            BrandId      = stockDTO.BrandId,
                            Count        = Math.Max(0, stockDTO.Quantity),
                            DoctorId     = stockDTO.DoctorId,
                            ClinicId     = effectiveClinicId,
                            PurchasedAmt = avgPrice,
                        });
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

        // Expiry dates are always midnight values. A browser in UTC+5 converts
        // "2027-02-28T00:00:00" local to "2027-02-27T19:00:00" UTC before sending,
        // so stored AdjustStock.ExpiryDate may differ from Stock.Expiry by up to ~14 hours.
        // A 24-hour tolerance safely covers this without risk of matching different expiry dates
        // (adjacent expiry dates are always >= 24 hours apart).
        private static bool AdjustExpiryMatches(DateTime? adjExpiry, DateTime? stockExpiry)
        {
            if (!adjExpiry.HasValue && !stockExpiry.HasValue) return true;
            if (!adjExpiry.HasValue || !stockExpiry.HasValue) return false;
            return Math.Abs((adjExpiry.Value - stockExpiry.Value).TotalHours) < 24;
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