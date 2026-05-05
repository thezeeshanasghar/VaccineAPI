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

            var costPrice = brandAmount?.PurchasedAmt ?? 0;
            // BrandAmount.Count is the authoritative current inventory — Stock.Quantity is the
            // original purchase quantity and is never decremented when vaccines are administered.
            var actualTotal = brandAmount?.Count ?? 0;

            var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
            var brandName = brand?.Name ?? "";

            var stocks = _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity > 0)
                .ToList();

            var purchasedTotal = stocks.Sum(s => s.Quantity);

            // Load all adjustments that are tied to a specific batch lot for this brand+clinic.
            // Unbatched adjustments (no BatchLot) are intentionally excluded here — they stay
            // inside pureCount and get distributed proportionally across batches.
            var batchedAdjRecords = _db.AdjustStocks
                .Where(a => a.BrandId == brandId && a.ClinicId == clinicId
                            && a.BatchLot != null && a.BatchLot != "")
                .ToList();

            var totalBatchedAdj = batchedAdjRecords.Sum(a => a.Adjustment);

            // Per-batch net adjustment map, keyed by (lot, expiryDate)
            var batchAdjMap = batchedAdjRecords
                .GroupBy(a => (Lot: a.BatchLot!.Trim(), a.ExpiryDate))
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

            // pureCount = inventory count with batch-specific adjustments removed,
            // so the proportional formula only distributes purchases + unbatched adjustments.
            var pureCount = Math.Max(0, actualTotal - totalBatchedAdj);

            var batches = stocks
                .GroupBy(s => new { Lot = (s.BatchLot ?? "").Trim(), s.Expiry })
                .Where(g => !string.IsNullOrWhiteSpace(g.Key.Lot))
                .Select(g =>
                {
                    var batchPurchased = g.Sum(s => s.Quantity);
                    var proportionalBase = purchasedTotal > 0
                        ? (int)Math.Round((double)batchPurchased / purchasedTotal * pureCount)
                        : 0;
                    batchAdjMap.TryGetValue((g.Key.Lot, g.Key.Expiry), out var batchAdj);
                    var available = Math.Max(0, proportionalBase + batchAdj);
                    return new AvailableBatchDTO
                    {
                        BrandId = brandId,
                        BrandName = brandName,
                        BatchLot = g.Key.Lot,
                        Expiry = g.Key.Expiry,
                        AvailableQuantity = available,
                        CostPrice = costPrice
                    };
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
                        BrandId = brandId,
                        BrandName = brandName,
                        BatchLot = null,
                        Expiry = noLotStocks.OrderBy(s => s.Expiry).FirstOrDefault()?.Expiry,
                        AvailableQuantity = actualTotal,
                        CostPrice = costPrice
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

                // Create Bill
                var bill = new Bill
                {
                    BillNo = firstStock.BillNo,
                    Supplier = resolvedSupplierName,
                    SupplierId = resolvedSupplierId,
                    BillDate = firstStock.BillDate != default ? firstStock.BillDate : DateTime.Now,
                    IsPaid = firstStock.IsPaid,
                    DoctorId = firstStock.DoctorId,
                    PaidDate = firstStock.PaidDate,
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
                    if (brandAmount == null || brandAmount.PurchasedAmt == 0 || brandAmount.Count == 0)
                    {
                        unitPrice = stockDTO.StockAmount;
                    }
                    else
                    {
                        unitPrice = ((brandAmount.PurchasedAmt * brandAmount.Count) + (stockDTO.StockAmount * stockDTO.Quantity))
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

                    // Capture old quantity before overwriting — needed for Count delta below
                    int oldQty = stock.Quantity;

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
                        stock.Bill.BillDate = stockDTO.BillDate != default ? stockDTO.BillDate : stock.Bill.BillDate;
                        stock.Bill.IsPaid = stockDTO.IsPaid;
                        stock.Bill.PaidDate = stockDTO.PaidDate != default ? stockDTO.PaidDate : stock.Bill.PaidDate;
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

                    // Count delta: BrandAmount.Count is the live administered-adjusted inventory.
                    // Stock.Quantity is never decremented by sales — only Count is.
                    // So we adjust Count by the quantity difference, not reset it entirely.
                    int qtyDelta = stockDTO.Quantity - oldQty;

                    var brandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == stockDTO.BrandId && ba.ClinicId == effectiveClinicId);

                    if (brandAmount != null)
                    {
                        brandAmount.Count        = Math.Max(0, brandAmount.Count + qtyDelta);
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