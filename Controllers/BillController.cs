using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using System;
using System.Data;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
        private readonly IWebHostEnvironment _host;

        public BillController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _db = context;
            _mapper = mapper;
            _host = host;
        }

        [HttpGet]
        public Response<List<BillDTO>> Get()
        {
            var bills = _db.Bills.Where(b => !b.BillNo.StartsWith("XFER-")).ToList();
            if (!bills.Any())
                return new Response<List<BillDTO>>(false, "No bills found", null);

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        [HttpGet("doctor/{doctorId}")] // Changed route to avoid conflict
        public Response<List<BillDTO>> GetByDoctor(long doctorId)
        {
            var bills = _db
                .Bills.Include(b => b.Doctor)
                .ThenInclude(d => d.User)
                .Include(b => b.Stocks)
                .ThenInclude(s => s.Brand)
                .Where(b => b.DoctorId == doctorId && !b.BillNo.StartsWith("XFER-"))
                .ToList();

            if (!bills.Any())
                return new Response<List<BillDTO>>(
                    false,
                    $"No bills found for doctor ID {doctorId}",
                    null
                );

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        // [HttpGet("{id:int}")]  // Added constraint to differentiate from doctorId
        // public Response<BillDTO> Getbyid(int id)
        // {
        //     var bill = _db.Bills
        //         .Include(b => b.Doctor)
        //             .ThenInclude(d => d.User)
        //         .FirstOrDefault(b => b.Id == id);

        //     if (bill == null)
        //         return new Response<BillDTO>(false, "Bill not found", null);

        //     var billDTO = _mapper.Map<BillDTO>(bill);
        //     return new Response<BillDTO>(true, null, billDTO);
        // }

        [HttpGet("{id}")]
        public Response<BillDTO> Getbyid(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            var billDTO = _mapper.Map<BillDTO>(bill);
            return new Response<BillDTO>(true, null, billDTO);
        }

        // [HttpGet("suppliers")]
        // public Response<List<SupplierDTO>> GetSuppliers()
        // {
        //     try
        //     {
        //         var suppliers = _db.Bills
        //             .Where(b => !string.IsNullOrEmpty(b.Supplier))
        //             .Select(b => new SupplierDTO { Name = b.Supplier })
        //             .Distinct()
        //             .OrderBy(s => s.Name)
        //             .ToList();

        //         if (!suppliers.Any())
        //         {
        //             return new Response<List<SupplierDTO>>(false, "No suppliers found", null);
        //         }

        //         return new Response<List<SupplierDTO>>(true, null, suppliers);
        //     }
        //     catch (Exception ex)
        //     {
        //         return new Response<List<SupplierDTO>>(
        //             false,
        //             $"Error retrieving suppliers: {ex.Message}",
        //             null
        //         );
        //     }
        // }

        [HttpGet("clinic/{clinicId}")] // Changed route to avoid conflict
        public Response<List<BillDTO>> GetByClinic(long clinicId)
        {
            var bills = _db
                .Bills.Include(b => b.Doctor)
                .ThenInclude(d => d.User)
                .Include(b => b.Stocks)
                .ThenInclude(s => s.Brand)
                .Where(b => b.ClinicId == clinicId && !b.BillNo.StartsWith("XFER-"))
                .OrderByDescending(x => x.Id)
                .ToList();

            if (!bills.Any())
                return new Response<List<BillDTO>>(
                    false,
                    $"No bills found for clinic ID {clinicId}",
                    null
                );

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            foreach (var dto in billDTOs)
            {
                var bill = bills.First(b => b.Id == dto.Id);
                dto.TotalAmount = bill.Stocks.Sum(s => s.StockAmount * s.Quantity);
                dto.TotalItems  = bill.Stocks.Count;
                dto.DoctorName  = bill.Doctor?.FirstName ?? "";
            }
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        [HttpGet("clinic/{clinicId}/next-bill-number")]
        public Response<string> GetNextBillNumber(long clinicId)
        {
            // Collect all used BILL-{n} numbers across ALL clinics globally.
            // Start from 1001 and return the first unused number.
            var used = _db.Bills
                .Where(b => b.BillNo.StartsWith("BILL-"))
                .Select(b => b.BillNo)
                .AsEnumerable()
                .Select(n => { int v; return int.TryParse(n.Replace("BILL-", ""), out v) ? v : 0; })
                .Where(v => v > 0)
                .ToHashSet();

            int next = 1001;
            while (used.Contains(next)) { next++; }

            return new Response<string>(true, null, next.ToString());
        }

        [HttpPost]
        public Response<BillDTO> Post(BillDTO billDTO)
        {
            var bill = _mapper.Map<Bill>(billDTO);
            _db.Bills.Add(bill);
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill created successfully", billDTO);
        }

        [HttpPut("{id}")]
        public Response<BillDTO> Put(int id, BillDTO billDTO)
        {
            if (id != billDTO.Id)
                return new Response<BillDTO>(false, "ID mismatch", null);

            var bill = _mapper.Map<Bill>(billDTO);
            _db.Entry(bill).State = EntityState.Modified;
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill updated successfully", billDTO);
        }

        [HttpDelete("{id}")]
        public Response<BillDTO> Delete(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            _db.Bills.Remove(bill);
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill deleted successfully", null);
        }

        [HttpGet("{id}/consumption")]
        public async Task<IActionResult> GetConsumption(long id)
        {
            var bill = await _db.Bills
                .Include(b => b.Stocks)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return NotFound(new { message = "Bill not found." });

            // Sum of original purchase quantities across all stock rows for this bill
            var purchasedQty = bill.Stocks.Sum(s => s.Quantity);

            // Current remaining count from BrandAmount
            // There may be multiple brands on one bill — handle each separately
            var brandIds = bill.Stocks.Select(s => s.BrandId).Distinct().ToList();

            var consumedDoses = new List<object>();
            int totalConsumed = 0;
            int totalRemaining = 0;

            foreach (var brandId in brandIds)
            {
                var brandStock = bill.Stocks.Where(s => s.BrandId == brandId).ToList();
                var brandPurchasedQty = brandStock.Sum(s => s.Quantity);

                var brandAmount = await _db.BrandAmounts
                    .FirstOrDefaultAsync(ba => ba.BrandId == brandId && ba.ClinicId == bill.ClinicId);

                var currentCount = brandAmount?.Count ?? 0;

                // Consumed from this bill = what was purchased minus what remains
                // (this is an approximation when multiple bills exist for same brand)
                var consumed = Math.Max(0, brandPurchasedQty - currentCount);
                var remaining = Math.Max(0, currentCount);

                totalConsumed += consumed;
                totalRemaining += remaining;

                // Find schedules where IsDone = true, BrandId matches, Lot matches any stock row from this bill
                var billLots = brandStock
                    .Where(s => !string.IsNullOrWhiteSpace(s.BatchLot))
                    .Select(s => s.BatchLot.Trim())
                    .Distinct()
                    .ToList();

                if (billLots.Any())
                {
                    var schedules = await _db.Schedules
                        .Include(s => s.Child)
                        .Include(s => s.Dose)
                            .ThenInclude(d => d.Vaccine)
                        .Include(s => s.Brand)
                        .Where(s => s.BrandId == brandId
                                 && s.IsDone == true
                                 && s.Lot != null
                                 && billLots.Contains(s.Lot.Trim()))
                        .OrderBy(s => s.GivenDate)
                        .ToListAsync();

                    consumedDoses.AddRange(schedules.Select(s => new
                    {
                        ScheduleId  = s.Id,
                        ChildId     = s.ChildId,
                        ChildName   = s.Child?.Name ?? "",
                        VaccineName = s.Brand?.Name ?? "",
                        DoseName    = s.Dose?.Vaccine?.Name ?? "",
                        GivenDate   = s.GivenDate,
                        CurrentLot  = s.Lot,
                        CurrentExpiry = s.Expiry,
                        BrandId     = s.BrandId
                    }));
                }
            }

            // Available replacement batches for each brand — excluding this bill's own lots
            var replacementBatches = new List<object>();
            foreach (var brandId in brandIds)
            {
                var billLots = bill.Stocks
                    .Where(s => s.BrandId == brandId && !string.IsNullOrWhiteSpace(s.BatchLot))
                    .Select(s => s.BatchLot.Trim())
                    .Distinct()
                    .ToList();

                var otherBatches = await _db.Stocks
                    .Include(s => s.Bill)
                    .Include(s => s.Brand)
                    .Where(s => s.BrandId == brandId
                             && s.Bill.ClinicId == bill.ClinicId
                             && s.BillId != id
                             && s.Quantity > 0
                             && !string.IsNullOrWhiteSpace(s.BatchLot)
                             && !billLots.Contains(s.BatchLot.Trim()))
                    .GroupBy(s => new { s.BatchLot, s.Expiry })
                    .Select(g => new
                    {
                        BrandId   = brandId,
                        BatchLot  = g.Key.BatchLot,
                        Expiry    = g.Key.Expiry,
                        Quantity  = g.Sum(x => x.Quantity)
                    })
                    .OrderBy(b => b.Expiry)
                    .ToListAsync();

                replacementBatches.AddRange(otherBatches);
            }

            return Ok(new
            {
                BillId           = id,
                PurchasedQty     = purchasedQty,
                ConsumedQty      = totalConsumed,
                RemainingQty     = totalRemaining,
                HasConsumption   = totalConsumed > 0,
                ConsumedDoses    = consumedDoses,
                ReplacementBatches = replacementBatches
            });
        }

        [HttpPost("{id}/reassign-batches")]
        public async Task<IActionResult> ReassignBatches(long id, [FromBody] List<BatchReassignDTO> items)
        {
            if (items == null || !items.Any())
                return BadRequest(new { message = "No reassignment items provided." });

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in items)
                {
                    var schedule = await _db.Schedules.FindAsync(item.ScheduleId);
                    if (schedule == null)
                    {
                        await transaction.RollbackAsync();
                        return NotFound(new { message = $"Schedule ID {item.ScheduleId} not found." });
                    }

                    if (string.IsNullOrWhiteSpace(item.NewBatchLot))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Replacement batch is required for schedule ID {item.ScheduleId}." });
                    }

                    schedule.Lot    = item.NewBatchLot.Trim();
                    schedule.Expiry = item.NewExpiry;
                    _db.Entry(schedule).State = EntityState.Modified;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = $"{items.Count} dose(s) reassigned successfully." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = $"Error: {ex.Message}" });
            }
        }

        [HttpDelete("{id}/delete-remaining")]
        public async Task<IActionResult> DeleteRemaining(long id)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var bill = await _db.Bills
                    .Include(b => b.Stocks)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (bill == null)
                    return NotFound(new { message = "Bill not found." });

                foreach (var stock in bill.Stocks.ToList())
                {
                    var brandAmount = await _db.BrandAmounts
                        .FirstOrDefaultAsync(ba => ba.BrandId == stock.BrandId && ba.ClinicId == bill.ClinicId);

                    if (brandAmount == null) continue;

                    // Remaining = units still in stock (not yet consumed)
                    // consumed = stock.Quantity - brandAmount.Count (approximation)
                    int remaining   = Math.Min(stock.Quantity, Math.Max(0, brandAmount.Count));
                    int consumed    = stock.Quantity - remaining;

                    if (remaining <= 0)
                    {
                        // All units already consumed — nothing to remove from stock
                        // Just reduce the stocks.Quantity to the consumed amount (no change needed)
                        continue;
                    }

                    // Reduce BrandAmount.Count by remaining units being deleted
                    brandAmount.Count = Math.Max(0, brandAmount.Count - remaining);

                    // Shrink stocks.Quantity to only the consumed amount
                    if (consumed > 0)
                    {
                        stock.Quantity = consumed;
                        _db.Entry(stock).State = EntityState.Modified;
                    }
                    else
                    {
                        // Zero consumed — remove the stock row entirely
                        _db.Stocks.Remove(stock);
                    }

                    // Recalculate weighted average from all remaining stocks
                    var allStocks = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == stock.BrandId
                                 && s.Bill.ClinicId == bill.ClinicId
                                 && s.Id != stock.Id
                                 && s.Quantity > 0)
                        .ToListAsync();

                    // Include the updated stock row if it still exists (consumed > 0)
                    decimal totalCost = allStocks.Sum(s => (decimal)s.StockAmount * s.Quantity);
                    int     totalQty  = allStocks.Sum(s => s.Quantity);
                    if (consumed > 0)
                    {
                        totalCost += (decimal)stock.StockAmount * consumed;
                        totalQty  += consumed;
                    }

                    brandAmount.PurchasedAmt = totalQty > 0
                        ? Math.Round(totalCost / totalQty, 2)
                        : 0;
                    _db.Entry(brandAmount).State = EntityState.Modified;

                    // Remove batch-specific adjuststocks for the deleted remaining units
                    if (!string.IsNullOrWhiteSpace(stock.BatchLot))
                    {
                        var orphanedAdjs = await _db.AdjustStocks
                            .Where(a => a.BrandId == stock.BrandId
                                     && a.ClinicId == bill.ClinicId
                                     && a.BatchLot != null
                                     && a.BatchLot.Trim() == stock.BatchLot.Trim())
                            .ToListAsync();
                        _db.AdjustStocks.RemoveRange(orphanedAdjs);
                    }
                }

                // Recalculate bill total based on remaining consumed stocks
                var updatedStocks = await _db.Stocks
                    .Where(s => s.BillId == id && s.Quantity > 0)
                    .ToListAsync();

                decimal newBillTotal = updatedStocks.Sum(s => (decimal)s.StockAmount * s.Quantity);

                // Adjust supplier payment — reduce to new bill total, remove overpayment
                var supplierPayments = await _db.SupplierPayments
                    .Where(p => p.BillId == id)
                    .ToListAsync();

                decimal existingPaid = supplierPayments.Sum(p => p.Amount);

                if (existingPaid > newBillTotal)
                {
                    // Overpaid — reduce the payment record to match new bill total
                    // Doctor received cash back for returned vaccines
                    if (supplierPayments.Any())
                    {
                        var pmt = supplierPayments.First();
                        if (newBillTotal <= 0)
                        {
                            _db.SupplierPayments.RemoveRange(supplierPayments);
                        }
                        else
                        {
                            pmt.Amount = newBillTotal;
                            _db.Entry(pmt).State = EntityState.Modified;
                            // Remove any additional payment records beyond the first
                            if (supplierPayments.Count > 1)
                                _db.SupplierPayments.RemoveRange(supplierPayments.Skip(1));
                        }
                    }
                }

                // Update bill AmountPaid and IsPaid
                bill.AmountPaid = existingPaid > 0 ? Math.Min(existingPaid, newBillTotal) : bill.AmountPaid;
                bill.IsPaid     = bill.AmountPaid.HasValue && bill.AmountPaid.Value >= newBillTotal && newBillTotal > 0;
                _db.Entry(bill).State = EntityState.Modified;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    message      = "Remaining stock deleted. Bill adjusted to consumed units.",
                    NewBillTotal = newBillTotal,
                    IsPaid       = bill.IsPaid
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = $"Error: {ex.Message}" });
            }
        }

        [HttpDelete("{id}/reverse")]
        public async Task<Response<BillDTO>> DeleteWithReversal(int id)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var bill = await _db.Bills
                    .Include(b => b.Stocks)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (bill == null)
                    return new Response<BillDTO>(false, "Bill not found", null);

                foreach (var stock in bill.Stocks.ToList())
                {
                    var brandAmount = await _db.BrandAmounts
                        .FirstOrDefaultAsync(ba => ba.BrandId == stock.BrandId && ba.ClinicId == bill.ClinicId);

                    if (brandAmount != null)
                    {
                        // Remaining units for THIS stock row = its current stocks.Quantity,
                        // which is decremented by every operation (gives, sales, transfers, losses).
                        // Using stock.Quantity (original purchased qty) was wrong for multi-bill
                        // scenarios — it never reflects what was consumed from this specific row.
                        int remaining = Math.Max(0, Math.Min(stock.Quantity, brandAmount.Count));
                        brandAmount.Count = Math.Max(0, brandAmount.Count - remaining);
                        _db.Entry(brandAmount).State = EntityState.Modified;

                        // Recalculate weighted average from remaining stocks after deletion
                        var otherStocks = await _db.Stocks
                            .Include(s => s.Bill)
                            .Where(s => s.BrandId == stock.BrandId
                                     && s.Bill.ClinicId == bill.ClinicId
                                     && s.Id != stock.Id
                                     && s.Quantity > 0)
                            .ToListAsync();

                        if (otherStocks.Any())
                        {
                            var totalQty  = otherStocks.Sum(s => s.Quantity);
                            var totalCost = otherStocks.Sum(s => (decimal)s.StockAmount * s.Quantity);
                            brandAmount.PurchasedAmt = totalQty > 0
                                ? Math.Round(totalCost / totalQty, 2)
                                : 0;
                        }
                        else
                        {
                            brandAmount.PurchasedAmt = 0;
                        }
                    }

                    // Remove any adjuststocks records anchored to this stock row's bill+brand
                    var orphanedAdjs = await _db.AdjustStocks
                        .Where(a => a.BrandId == stock.BrandId && a.ClinicId == bill.ClinicId
                                 && !string.IsNullOrWhiteSpace(a.BatchLot)
                                 && (stock.BatchLot != null && a.BatchLot.Trim() == stock.BatchLot.Trim()))
                        .ToListAsync();
                    _db.AdjustStocks.RemoveRange(orphanedAdjs);

                    _db.Stocks.Remove(stock);
                }

                // Reverse supplier payments linked to this bill
                var supplierPayments = await _db.SupplierPayments
                    .Where(p => p.BillId == id)
                    .ToListAsync();
                _db.SupplierPayments.RemoveRange(supplierPayments);

                _db.Bills.Remove(bill);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new Response<BillDTO>(true, "Bill deleted and stock reversed successfully", null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new Response<BillDTO>(false, $"Error: {ex.Message}", null);
            }
        }

        [HttpGet("Suppliers")]
        public Response<IEnumerable<string>> GetSupplierNames()
        {
            try
            {
                var supplierNames = _db.Suppliers
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.Name)
                    .Select(s => s.Name)
                    .ToList();

                if (!supplierNames.Any())
                    return new Response<IEnumerable<string>>(false, "No suppliers found", null);

                return new Response<IEnumerable<string>>(true, "Suppliers retrieved successfully", supplierNames);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving suppliers: {ex.Message}");
                return new Response<IEnumerable<string>>(false, "An error occurred while retrieving suppliers", null);
            }
        }

        [HttpPatch("{id}/ispaapprove")]
        public async Task<IActionResult> PatchIsPAApprove(long id)
        {
            try
            {
                var Bill = await _db.Bills.FirstOrDefaultAsync(s => s.Id == id);
                if (Bill == null)
                {
                    return NotFound(new { message = "Bill not found." });
                }
                Bill.IsPAApprove = true;
                await _db.SaveChangesAsync();
                return Ok(new { message = "IsPAApprove updated successfully.", Bill });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> EditBill(long id, [FromBody] BillDTO billDTO)
        {
            if (id != billDTO.Id)
            {
                return BadRequest(new { message = "ID mismatch between route and body." });
            }

            try
            {
                if (billDTO.ClinicId != default)
                {
                    var clinic = await _db.Clinics.FindAsync(billDTO.ClinicId);
                    if (clinic == null)
                    {
                        return NotFound(
                            new { message = $"Clinic with ID {billDTO.ClinicId} not found." }
                        );
                    }
                }

                var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == id);
                if (bill == null)
                {
                    return NotFound(new { message = "Bill not found." });
                }

                bill.BillNo = billDTO.BillNo ?? bill.BillNo;
                bill.Supplier = billDTO.Supplier?.Trim() ?? bill.Supplier;
                bill.BillDate = billDTO.BillDate != default ? billDTO.BillDate : bill.BillDate;
                bill.IsPaid = billDTO.IsPaid;
                bill.PaidDate = billDTO.PaidDate != default ? billDTO.PaidDate : bill.PaidDate;
                bill.ClinicId = billDTO.ClinicId != default ? billDTO.ClinicId : bill.ClinicId;
                await _db.SaveChangesAsync();

                return Ok(new { message = "Bill updated successfully.", Bill = bill });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost("{id}/payment")]
        public async Task<IActionResult> MakePayment(long id, [FromBody] BillPaymentDTO dto)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var bill = await _db.Bills
                    .Include(b => b.Stocks)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (bill == null)
                    return NotFound(new { message = "Bill not found." });

                if (dto.Amount <= 0)
                    return BadRequest(new { message = "Payment amount must be greater than zero." });

                // Total payable = sum of stock amounts (AWT already included in StockAmount)
                decimal totalPayable = bill.Stocks.Sum(s => s.StockAmount * s.Quantity);
                decimal alreadyPaid  = bill.AmountPaid ?? 0m;
                decimal newTotalPaid = alreadyPaid + dto.Amount;

                bill.AmountPaid    = newTotalPaid;
                bill.IsPaid        = newTotalPaid >= totalPayable;
                bill.PaidDate      = bill.IsPaid ? DateTime.Now : bill.PaidDate;
                bill.PaymentMethod = dto.PaymentMethod ?? bill.PaymentMethod ?? "Cash";
                _db.Entry(bill).State = EntityState.Modified;

                // Record supplier payment if supplier is linked
                if (bill.SupplierId.HasValue)
                {
                    _db.SupplierPayments.Add(new SupplierPayment
                    {
                        SupplierId    = bill.SupplierId.Value,
                        ClinicId      = bill.ClinicId,
                        Amount        = dto.Amount,
                        PaymentDate   = DateTime.Now,
                        PaymentMethod = dto.PaymentMethod ?? "Cash",
                        Notes         = dto.Notes?.Trim(),
                        BillId        = bill.Id
                    });
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new {
                    message       = bill.IsPaid ? "Bill fully paid." : "Partial payment recorded.",
                    AmountPaid    = newTotalPaid,
                    IsPaid        = bill.IsPaid,
                    TotalPayable  = totalPayable,
                    Outstanding   = Math.Max(0, totalPayable - newTotalPaid)
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = $"Error: {ex.Message}" });
            }
        }

        public class PdfFooter : PdfPageEventHelper
        {
            public override void OnEndPage(PdfWriter writer, Document document)
            {
                TimeZoneInfo pakistanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
                DateTime pakistanTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pakistanTimeZone);
                string dateTimeStamp = pakistanTime.ToString("yyyy-MM-dd hh:mm tt");
                Font footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                PdfPTable footerTable = new PdfPTable(1);
                footerTable.TotalWidth =
                    document.PageSize.Width - document.LeftMargin - document.RightMargin;
                footerTable.DefaultCell.Border = Rectangle.NO_BORDER;
                footerTable.DefaultCell.HorizontalAlignment = Element.ALIGN_CENTER;
                footerTable.AddCell(new Phrase($"Printed on: {dateTimeStamp}", footerFont));
                footerTable.WriteSelectedRows(0,-1,document.LeftMargin,document.BottomMargin - 10,writer.DirectContent);
            }
        }
        
        [HttpGet("brand-stock-report-pdf")]
        public IActionResult GenerateBrandStockReportPdf(
            [FromQuery] long clinicId,
            [FromQuery] long? brandId,
            [FromQuery] string fromDate,
            [FromQuery] string toDate
        )
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);

                var clinic = _db
                    .Clinics.Include(c => c.Doctor)
                    .FirstOrDefault(c => c.Id == clinicId);
                if (clinic == null)
                {
                    return NotFound("Clinic not found.");
                }

                // Check if we're generating report for all brands or a specific brand
                bool isAllBrands = !brandId.HasValue || brandId.Value == 0;
                
                Brand? brand = null;
                if (!isAllBrands)
                {
                    brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
                    if (brand == null)
                    {
                        return NotFound("Brand not found.");
                    }
                }

                var doctorName = clinic.Doctor?.DisplayName ?? "Unknown Doctor";
                var additionalInfo = clinic.Doctor?.AdditionalInfo ?? "No additional info";
                var clinicName = clinic.Name ?? "Unknown Clinic";
                var monogramImage = clinic.MonogramImage ?? "default-monogram.png";
                var address = clinic.Address ?? "Unknown Address";
                var phoneNumber = clinic.PhoneNumber ?? "Unknown Phone Number";
                var today = DateTime.Today;

                if (isAllBrands)
                {
                    return GenerateAllBrandsStockReportPdf(clinicId, parsedFromDate, parsedToDate, clinic, doctorName, additionalInfo, clinicName, monogramImage, address, phoneNumber, today);
                }

                var brandAmount = _db.BrandAmounts.FirstOrDefault(b =>
                    b.BrandId == brandId && b.ClinicId == clinicId);
                if (brandAmount == null)
                    return NotFound("Brand amount not found.");

                int todaysInventory = brandAmount.Count;

                var schedules = _db
                    .Schedules.Where(s =>
                        s.BrandId == brandId
                        && s.GivenDate >= parsedFromDate
                        && s.GivenDate <= today
                        && s.Child.ClinicId == clinicId)
                    .ToList();

                var directSaleGroups = _db.DirectSales
                    .Where(ds =>
                        ds.BrandId == brandId
                        && ds.ClinicId == clinicId
                        && ds.SaleDate >= parsedFromDate
                        && ds.SaleDate <= today
                    )
                    .AsEnumerable()
                    .GroupBy(ds => ds.SaleDate.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(ds => ds.Quantity));

                var stockPurchases = _db
                    .Stocks.Join(
                        _db.Bills,
                        stock => stock.BillId,
                        bill => bill.Id,
                        (stock, bill) => new { stock, bill }
                    )
                    .Where(sb =>
                        sb.stock.BrandId == brandId
                        && sb.bill.BillDate >= parsedFromDate
                        && sb.bill.BillDate <= parsedToDate
                        && sb.bill.ClinicId == clinicId
                        && !sb.bill.BillNo.StartsWith("XFER-")
                    )
                    .GroupBy(sb => sb.bill.BillDate.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

                // Net transfer per day: positive = received, negative = sent
                var transferGroups = _db.StockTransfers
                    .Where(t =>
                        t.BrandId == brandId
                        && t.CreatedAt >= parsedFromDate
                        && t.CreatedAt <= today
                        && (t.ToClinicId == clinicId || t.FromClinicId == clinicId)
                    )
                    .AsEnumerable()
                    .GroupBy(t => t.CreatedAt.Date)
                    .ToDictionary(g => g.Key, g =>
                        g.Sum(t => t.ToClinicId == clinicId ? t.Quantity : -t.Quantity));

                var vaccineGroups = schedules
                    .Where(s => s.GivenDate.HasValue)
                    .GroupBy(s => s.GivenDate!.Value.Date)
                    .ToDictionary(g => g.Key, g => g.Count());

               var stockAdjustments = _db
    .AdjustStocks
    .Where(a =>
        a.BrandId == brandId &&
        a.Date >= parsedFromDate &&
        a.Date <= parsedToDate &&
        a.ClinicId == clinicId
    )
    .AsEnumerable()
    .GroupBy(a => a.Date.Date)
    .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

                var allDates = Enumerable
                    .Range(0, (parsedToDate - parsedFromDate).Days + 1)
                    .Select(offset => parsedFromDate.AddDays(offset))
                    .ToList();

                // Calculate opening stock (stock at the beginning of the date range)
                // Opening Stock = Today's Inventory + Sold(from firstDate to today) - Purchased(from firstDate to today) - Adjusted(from firstDate to today)
                int totalVaccinesFromFirstDate = schedules
                    .Where(s => s.GivenDate >= parsedFromDate && s.GivenDate <= today)
                    .Count();

                int totalDirectSalesFromFirstDate = directSaleGroups.Sum(kvp => kvp.Value);

                int totalPurchasesFromFirstDate = stockPurchases
                    .Where(kvp => kvp.Key >= parsedFromDate && kvp.Key <= today)
                    .Sum(kvp => kvp.Value);

                int totalAdjustmentsFromFirstDate = stockAdjustments
                    .Where(kvp => kvp.Key >= parsedFromDate && kvp.Key <= today)
                    .Sum(kvp => kvp.Value);

                // Net transfers in range (positive = received, negative = sent)
                int totalNetTransferFromFirstDate = transferGroups.Sum(kvp => kvp.Value);

                // Calculate initial opening stock for the first date in range
                int initialOpeningStock = todaysInventory + totalVaccinesFromFirstDate + totalDirectSalesFromFirstDate - totalNetTransferFromFirstDate - totalPurchasesFromFirstDate - totalAdjustmentsFromFirstDate;

                var reportData =
                    new List<(
                        DateTime Date,
                        int OpeningStock,
                        int VaccinesDone,
                        int DirectSaleQty,
                        int NetTransfer,
                        int StockPurchased,
                        int StockAdjusted,
                        int StockInHand
                    )>();

                int currentStock = initialOpeningStock;

                foreach (var date in allDates)
                {
                    int vaccinesDoneToday = vaccineGroups.ContainsKey(date)
                        ? vaccineGroups[date]
                        : 0;
                    int directSaleTodayQty = directSaleGroups.ContainsKey(date.Date)
                        ? directSaleGroups[date.Date]
                        : 0;
                    int netTransferToday = transferGroups.ContainsKey(date.Date)
                        ? transferGroups[date.Date]
                        : 0;
                    int stockPurchasedToday = stockPurchases.ContainsKey(date)
                        ? stockPurchases[date]
                        : 0;
                    int stockAdjustedToday = stockAdjustments.ContainsKey(date.Date)
                        ? stockAdjustments[date.Date]
                        : 0;

                    int openingStock = currentStock;
                    int stockInHand = openingStock - vaccinesDoneToday - directSaleTodayQty + netTransferToday + stockPurchasedToday + stockAdjustedToday;

                    reportData.Add(
                        (
                            date,
                            openingStock,
                            vaccinesDoneToday,
                            directSaleTodayQty,
                            netTransferToday,
                            stockPurchasedToday,
                            stockAdjustedToday,
                            stockInHand
                        )
                    );

                    // Update current stock for next iteration
                    currentStock = stockInHand;
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    writer.PageEvent = new PdfFooter(); // Custom footer if you have one
                    document.Open();

                    PdfPTable upperTable = new PdfPTable(2);
                    float[] upperTableWidths = new float[] { 350f, 160f };
                    upperTable.HorizontalAlignment = 0;
                    upperTable.TotalWidth = 510f;
                    upperTable.LockedWidth = true;
                    upperTable.SetWidths(upperTableWidths);

                    Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    Phrase phrase = new Phrase();
                    phrase.Add(new Chunk(doctorName + "\n", boldFont));
                    phrase.Add(new Chunk(additionalInfo + "\n", regularFont));
                    phrase.Add(new Chunk(clinicName + "\n", boldFont));
                    phrase.Add(new Chunk(address + "\n", regularFont));
                    phrase.Add(new Chunk(phoneNumber, regularFont));

                    PdfPCell leftCell = new PdfPCell(phrase)
                    {
                        Border = 0,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 5,
                    };
                    upperTable.AddCell(leftCell);

                    var imageCell = new PdfPCell(new Phrase(""))
                    {
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    };

                    if (!string.IsNullOrEmpty(monogramImage))
                    {
                        var logoPath = Path.Combine(_host.ContentRootPath, monogramImage);
                        if (System.IO.File.Exists(logoPath))
                        {
                            var img = Image.GetInstance(logoPath);
                            img.ScaleAbsolute(160f, 50f);
                            imageCell = new PdfPCell(img, false)
                            {
                                Border = 0,
                                FixedHeight = 50f,
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                            };
                        }
                    }

                    upperTable.AddCell(imageCell);
                    document.Add(upperTable);
                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    document.Add(
                        new Paragraph($"ITEM NAME: {brand?.Name ?? "Unknown"}", normalFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );

                    document.Add(
                        new Paragraph("ITEM REPORT", headerFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );

                    document.Add(
                        new Paragraph(
                            $"FROM {parsedFromDate:dd-MM-yyyy} TO {parsedToDate:dd-MM-yyyy}",
                            normalFont
                        )
                        {
                            Alignment = Element.ALIGN_CENTER,
                            SpacingAfter = 10f,
                        }
                    );
                    PdfPTable table = new PdfPTable(8) { WidthPercentage = 100 };
                    table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f, 2f, 2f, 2f });

                    string[] headers =
                    {
                        "Date",
                        "Opening Stock",
                        "Sold",
                        "Direct Sale",
                        "Transfer",
                        "Purchased",
                        "Adjusted",
                        "Stock In Hand",
                    };
                    foreach (var header in headers)
                    {
                        table.AddCell(
                            new PdfPCell(new Phrase(header, headerFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = BaseColor.LightGray,
                                Padding = 5,
                            }
                        );
                    }

                    foreach (var row in reportData)
                    {
                        if (
                            row.VaccinesDone == 0
                            && row.StockPurchased == 0
                            && row.StockAdjusted == 0
                        )
                        {
                            continue;
                        }
                        table.AddCell(
                            new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.OpeningStock.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.DirectSaleQty.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.NetTransfer == 0 ? "0" : (row.NetTransfer > 0 ? $"+{row.NetTransfer}" : row.NetTransfer.ToString()), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.StockInHand.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                    }

                    // Filter to only rows that were displayed (have activity)
                    var displayedRows = reportData
                        .Where(r => r.VaccinesDone != 0 || r.DirectSaleQty != 0 || r.NetTransfer != 0 || r.StockPurchased != 0 || r.StockAdjusted != 0)
                        .ToList();

                    // Check if there's any data to display
                    if (!displayedRows.Any())
                    {
                        return NotFound("No stock activity found for the specified period.");
                    }

                    if (displayedRows.Any())
                    {
                        int totalSold = displayedRows.Sum(r => r.VaccinesDone);
                        int totalDirectSale = displayedRows.Sum(r => r.DirectSaleQty);
                        int totalNetTransfer = displayedRows.Sum(r => r.NetTransfer);
                        int totalPurchased = displayedRows.Sum(r => r.StockPurchased);
                        int totalAdjusted = displayedRows.Sum(r => r.StockAdjusted);
                        int totalOpeningStock = displayedRows.First().OpeningStock;
                        int totalStockInHand = displayedRows.Last().StockInHand;
                        string transferTotalStr = totalNetTransfer == 0 ? "0" : (totalNetTransfer > 0 ? $"+{totalNetTransfer}" : totalNetTransfer.ToString());

                        table.AddCell(new PdfPCell(new Phrase("Totals", boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(totalOpeningStock.ToString(), boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(totalSold.ToString(), boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(totalDirectSale.ToString(), boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(transferTotalStr, boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(totalPurchased.ToString(), boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(totalAdjusted.ToString(), boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                        table.AddCell(new PdfPCell(new Phrase(totalStockInHand.ToString(), boldFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            BackgroundColor = BaseColor.LightGray,
                            Padding = 5
                        });
                    }
                  
                    document.Add(table);
                    document.Close();

                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        $"BrandStockReport_Clinic_{clinicId}_Brand_{brandId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
                    );
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private IActionResult GenerateAllBrandsStockReportPdf(
            long clinicId,
            DateTime parsedFromDate,
            DateTime parsedToDate,
            Clinic clinic,
            string doctorName,
            string additionalInfo,
            string clinicName,
            string monogramImage,
            string address,
            string phoneNumber,
            DateTime today
        )
        {
            try
            {
                // Get all brands for this clinic
                var brandAmounts = _db.BrandAmounts
                    .Include(ba => ba.Brand)
                    .Where(ba => ba.ClinicId == clinicId && ba.Count > 0)
                    .ToList();

                if (!brandAmounts.Any())
                {
                    return NotFound("No brands found for this clinic.");
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30); // Landscape for more columns
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    writer.PageEvent = new PdfFooter();
                    document.Open();

                    // Header
                    PdfPTable upperTable = new PdfPTable(2);
                    float[] upperTableWidths = new float[] { 500f, 200f };
                    upperTable.HorizontalAlignment = 0;
                    upperTable.TotalWidth = 750f;
                    upperTable.LockedWidth = true;
                    upperTable.SetWidths(upperTableWidths);

                    Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    Phrase phrase = new Phrase();
                    phrase.Add(new Chunk(doctorName + "\n", boldFont));
                    phrase.Add(new Chunk(additionalInfo + "\n", regularFont));
                    phrase.Add(new Chunk(clinicName + "\n", boldFont));
                    phrase.Add(new Chunk(address + "\n", regularFont));
                    phrase.Add(new Chunk(phoneNumber, regularFont));

                    PdfPCell leftCell = new PdfPCell(phrase)
                    {
                        Border = 0,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 5,
                    };
                    upperTable.AddCell(leftCell);

                    var imageCell = new PdfPCell(new Phrase(""))
                    {
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    };

                    if (!string.IsNullOrEmpty(monogramImage))
                    {
                        var logoPath = Path.Combine(_host.ContentRootPath, monogramImage);
                        if (System.IO.File.Exists(logoPath))
                        {
                            var img = Image.GetInstance(logoPath);
                            img.ScaleAbsolute(160f, 50f);
                            imageCell = new PdfPCell(img, false)
                            {
                                Border = 0,
                                FixedHeight = 50f,
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                            };
                        }
                    }

                    upperTable.AddCell(imageCell);
                    document.Add(upperTable);

                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    document.Add(
                        new Paragraph("ALL BRANDS STOCK REPORT", headerFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );

                    document.Add(
                        new Paragraph(
                            $"FROM {parsedFromDate:dd-MM-yyyy} TO {parsedToDate:dd-MM-yyyy}",
                            normalFont
                        )
                        {
                            Alignment = Element.ALIGN_CENTER,
                            SpacingAfter = 10f,
                        }
                    );

                    // Create table with 9 columns (Brand Name + 8 data columns)
                    PdfPTable table = new PdfPTable(9) { WidthPercentage = 100 };
                    table.SetWidths(new float[] { 2.5f, 1.8f, 1.8f, 1.8f, 1.8f, 1.8f, 1.8f, 1.8f, 1.8f });

                    string[] headers =
                    {
                        "Brand Name",
                        "Date",
                        "Opening Stock",
                        "Sold",
                        "Direct Sale",
                        "Transfer",
                        "Purchased",
                        "Adjusted",
                        "Stock In Hand",
                    };
                    foreach (var header in headers)
                    {
                        table.AddCell(
                            new PdfPCell(new Phrase(header, headerFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = BaseColor.LightGray,
                                Padding = 5,
                            }
                        );
                    }

                    int grandTotalSold = 0;
                    int grandTotalDirectSale = 0;
                    int grandTotalPurchased = 0;
                    int grandTotalAdjusted = 0;
                    int grandTotalOpeningStock = 0;
                    int grandTotalClosingStock = 0;
                    bool hasAnyData = false;

                    // Process each brand
                    foreach (var brandAmount in brandAmounts.OrderBy(ba => ba.Brand.Name))
                    {
                        var brandId = brandAmount.BrandId;
                        var brandName = brandAmount.Brand?.Name ?? "Unknown";
                        int todaysInventory = brandAmount.Count;

                        var schedules = _db
                            .Schedules.Where(s =>
                                s.BrandId == brandId
                                && s.GivenDate >= parsedFromDate
                                && s.GivenDate <= today
                                && s.Child.ClinicId == clinicId)
                            .ToList();

                        var directSaleGroups = _db.DirectSales
                            .Where(ds =>
                                ds.BrandId == brandId
                                && ds.ClinicId == clinicId
                                && ds.SaleDate >= parsedFromDate
                                && ds.SaleDate <= today
                            )
                            .AsEnumerable()
                            .GroupBy(ds => ds.SaleDate.Date)
                            .ToDictionary(g => g.Key, g => g.Sum(ds => ds.Quantity));

                        var stockPurchases = _db
                            .Stocks.Join(
                                _db.Bills,
                                stock => stock.BillId,
                                bill => bill.Id,
                                (stock, bill) => new { stock, bill }
                            )
                            .Where(sb =>
                                sb.stock.BrandId == brandId
                                && sb.bill.BillDate >= parsedFromDate
                                && sb.bill.BillDate <= parsedToDate
                                && sb.bill.ClinicId == clinicId
                                && !sb.bill.BillNo.StartsWith("XFER-")
                            )
                            .GroupBy(sb => sb.bill.BillDate.Date)
                            .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

                        var transferGroups = _db.StockTransfers
                            .Where(t =>
                                t.BrandId == brandId
                                && t.CreatedAt >= parsedFromDate
                                && t.CreatedAt <= today
                                && (t.ToClinicId == clinicId || t.FromClinicId == clinicId)
                            )
                            .AsEnumerable()
                            .GroupBy(t => t.CreatedAt.Date)
                            .ToDictionary(g => g.Key, g =>
                                g.Sum(t => t.ToClinicId == clinicId ? t.Quantity : -t.Quantity));

                        var vaccineGroups = schedules
                            .Where(s => s.GivenDate.HasValue)
                            .GroupBy(s => s.GivenDate!.Value.Date)
                            .ToDictionary(g => g.Key, g => g.Count());

                        var stockAdjustments = _db
                            .AdjustStocks
                            .Where(a =>
                                a.BrandId == brandId
                                && a.Date >= parsedFromDate
                                && a.Date <= parsedToDate
                                && a.ClinicId == clinicId
                            )
                            .AsEnumerable()
                            .GroupBy(a => a.Date.Date)
                            .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

                        var allDates = Enumerable
                            .Range(0, (parsedToDate - parsedFromDate).Days + 1)
                            .Select(offset => parsedFromDate.AddDays(offset))
                            .ToList();

                        // Calculate opening stock
                        int totalVaccinesFromFirstDate = schedules
                            .Where(s => s.GivenDate >= parsedFromDate && s.GivenDate <= today)
                            .Count();

                        int totalDirectSalesFromFirstDate = directSaleGroups.Sum(kvp => kvp.Value);

                        int totalPurchasesFromFirstDate = stockPurchases
                            .Where(kvp => kvp.Key >= parsedFromDate && kvp.Key <= today)
                            .Sum(kvp => kvp.Value);

                        int totalAdjustmentsFromFirstDate = stockAdjustments
                            .Where(kvp => kvp.Key >= parsedFromDate && kvp.Key <= today)
                            .Sum(kvp => kvp.Value);

                        int totalNetTransferFromFirstDate = transferGroups.Sum(kvp => kvp.Value);

                        int initialOpeningStock = todaysInventory + totalVaccinesFromFirstDate + totalDirectSalesFromFirstDate
                            - totalNetTransferFromFirstDate - totalPurchasesFromFirstDate - totalAdjustmentsFromFirstDate;

                        var reportData = new List<(
                            DateTime Date,
                            int OpeningStock,
                            int VaccinesDone,
                            int DirectSaleQty,
                            int NetTransfer,
                            int StockPurchased,
                            int StockAdjusted,
                            int StockInHand
                        )>();

                        int currentStock = initialOpeningStock;

                        foreach (var date in allDates)
                        {
                            int vaccinesDoneToday = vaccineGroups.ContainsKey(date)
                                ? vaccineGroups[date]
                                : 0;
                            int directSaleTodayQty = directSaleGroups.ContainsKey(date.Date)
                                ? directSaleGroups[date.Date]
                                : 0;
                            int netTransferToday = transferGroups.ContainsKey(date.Date)
                                ? transferGroups[date.Date]
                                : 0;
                            int stockPurchasedToday = stockPurchases.ContainsKey(date)
                                ? stockPurchases[date]
                                : 0;
                            int stockAdjustedToday = stockAdjustments.ContainsKey(date.Date)
                                ? stockAdjustments[date.Date]
                                : 0;

                            int openingStock = currentStock;
                            int stockInHand = openingStock - vaccinesDoneToday - directSaleTodayQty + netTransferToday + stockPurchasedToday + stockAdjustedToday;

                            reportData.Add(
                                (
                                    date,
                                    openingStock,
                                    vaccinesDoneToday,
                                    directSaleTodayQty,
                                    netTransferToday,
                                    stockPurchasedToday,
                                    stockAdjustedToday,
                                    stockInHand
                                )
                            );

                            currentStock = stockInHand;
                        }

                        // Filter to only rows with activity
                        var displayedRows = reportData
                            .Where(r => r.VaccinesDone != 0 || r.DirectSaleQty != 0 || r.NetTransfer != 0 || r.StockPurchased != 0 || r.StockAdjusted != 0)
                            .ToList();

                        if (displayedRows.Any())
                        {
                            hasAnyData = true;
                            bool firstRowForBrand = true;

                            foreach (var row in displayedRows)
                            {
                                // Brand name (only on first row for this brand)
                                if (firstRowForBrand)
                                {
                                    table.AddCell(
                                        new PdfPCell(new Phrase(brandName, normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                            Rowspan = displayedRows.Count,
                                            VerticalAlignment = Element.ALIGN_MIDDLE,
                                        }
                                    );
                                    firstRowForBrand = false;
                                }

                                table.AddCell(
                                    new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.OpeningStock.ToString(), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.DirectSaleQty.ToString(), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.NetTransfer == 0 ? "0" : (row.NetTransfer > 0 ? $"+{row.NetTransfer}" : row.NetTransfer.ToString()), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase(row.StockInHand.ToString(), normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_CENTER,
                                    }
                                );
                            }

                            // Add brand totals
                            int brandTotalSold = displayedRows.Sum(r => r.VaccinesDone);
                            int brandTotalDirectSale = displayedRows.Sum(r => r.DirectSaleQty);
                            int brandTotalNetTransfer = displayedRows.Sum(r => r.NetTransfer);
                            int brandTotalPurchased = displayedRows.Sum(r => r.StockPurchased);
                            int brandTotalAdjusted = displayedRows.Sum(r => r.StockAdjusted);
                            int brandOpeningStock = displayedRows.First().OpeningStock;
                            int brandClosingStock = displayedRows.Last().StockInHand;
                            string brandTransferStr = brandTotalNetTransfer == 0 ? "0" : (brandTotalNetTransfer > 0 ? $"+{brandTotalNetTransfer}" : brandTotalNetTransfer.ToString());

                            grandTotalSold += brandTotalSold;
                            grandTotalDirectSale += brandTotalDirectSale;
                            grandTotalPurchased += brandTotalPurchased;
                            grandTotalAdjusted += brandTotalAdjusted;

                            if (grandTotalOpeningStock == 0) grandTotalOpeningStock = brandOpeningStock;
                            grandTotalClosingStock += brandClosingStock;

                            // Brand subtotal row
                            table.AddCell(new PdfPCell(new Phrase($"Subtotal: {brandName}", boldFont))
                            {
                                Colspan = 2,
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandOpeningStock.ToString(), boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandTotalSold.ToString(), boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandTotalDirectSale.ToString(), boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandTransferStr, boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandTotalPurchased.ToString(), boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandTotalAdjusted.ToString(), boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                            table.AddCell(new PdfPCell(new Phrase(brandClosingStock.ToString(), boldFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = new BaseColor(240, 240, 240),
                                Padding = 5
                            });
                        }
                    }

                    if (!hasAnyData)
                    {
                        return NotFound("No stock activity found for any brand in the specified period.");
                    }

                    // Grand totals row
                    PdfPCell grandTotalLabelCell = new PdfPCell(new Phrase("GRAND TOTAL", boldFont))
                    {
                        Colspan = 2,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    };
                    table.AddCell(grandTotalLabelCell);

                    table.AddCell(new PdfPCell(new Phrase("-", boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });
                    table.AddCell(new PdfPCell(new Phrase(grandTotalSold.ToString(), boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });
                    table.AddCell(new PdfPCell(new Phrase(grandTotalDirectSale.ToString(), boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });
                    table.AddCell(new PdfPCell(new Phrase("-", boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });
                    table.AddCell(new PdfPCell(new Phrase(grandTotalPurchased.ToString(), boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });
                    table.AddCell(new PdfPCell(new Phrase(grandTotalAdjusted.ToString(), boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });
                    table.AddCell(new PdfPCell(new Phrase(grandTotalClosingStock.ToString(), boldFont))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = BaseColor.LightGray,
                        Padding = 5
                    });

                    document.Add(table);
                    document.Close();

                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        $"AllBrandsStockReport_Clinic_{clinicId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
                    );
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating all brands PDF: {ex.Message}\n{ex.StackTrace}");
            }
        }

        [HttpGet("item-purchase-report-pdf")]
        public IActionResult GenerateItemPurchaseReportPdf(
            [FromQuery] long clinicId,
            [FromQuery] long brandId,
            [FromQuery] string fromDate,
            [FromQuery] string toDate
        )
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate).Date;
                var parsedToDate = DateTime.Parse(toDate).Date;

                var clinic = _db
                    .Clinics.Include(c => c.Doctor)
                    .FirstOrDefault(c => c.Id == clinicId);
                if (clinic == null)
                {
                    return NotFound("Clinic not found.");
                }

                var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
                if (brand == null)
                {
                    return NotFound("Brand not found.");
                }

                var result = _db
                    .Stocks.Join(
                        _db.Bills,
                        stock => stock.BillId,
                        bill => bill.Id,
                        (stock, bill) => new { stock, bill }
                    )
                    .Where(sb =>
                        sb.stock.BrandId == brandId
                        && sb.bill.ClinicId == clinicId
                        && sb.bill.BillDate >= parsedFromDate
                        && sb.bill.BillDate <= parsedToDate
                    )
                    .Select(sb => new
                    {
                        sb.bill.BillDate,
                        sb.bill.BillNo,
                        sb.bill.Id, 
                        sb.bill.Supplier,
                        sb.stock.Quantity,
                        sb.stock.StockAmount,
                    })
                    .ToList();

                if (!result.Any())
                {
                    return NotFound("No data found for the specified criteria.");
                }

                result = result.OrderBy(r => r.BillDate).ToList();

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    writer.PageEvent = new PdfFooter(); // Custom footer
                    document.Open();

                    PdfPTable upperTable = new PdfPTable(2);
                    upperTable.TotalWidth = 510f;
                    upperTable.LockedWidth = true;
                    upperTable.SetWidths(new float[] { 350f, 160f });

                    Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    Phrase phrase = new Phrase();
                    phrase.Add(
                        new Chunk($"{clinic.Doctor?.DisplayName ?? "Unknown Doctor"}\n", boldFont)
                    );
                    phrase.Add(
                        new Chunk(
                            $"{clinic.Doctor?.AdditionalInfo ?? "No additional info"}\n",
                            regularFont
                        )
                    );
                    phrase.Add(new Chunk($"{clinic.Name ?? "Unknown Clinic"}\n", boldFont));
                    phrase.Add(new Chunk($"{clinic.Address ?? "Unknown Address"}\n", regularFont));
                    phrase.Add(
                        new Chunk($"{clinic.PhoneNumber ?? "Unknown Phone Number"}", regularFont)
                    );

                    PdfPCell leftCell = new PdfPCell(phrase)
                    {
                        Border = 0,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 5,
                    };
                    upperTable.AddCell(leftCell);

                    var imageCell = new PdfPCell(new Phrase(""))
                    {
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    };

                    if (!string.IsNullOrEmpty(clinic.MonogramImage))
                    {
                        var logoPath = Path.Combine(_host.ContentRootPath, clinic.MonogramImage);
                        if (System.IO.File.Exists(logoPath))
                        {
                            var img = Image.GetInstance(logoPath);
                            img.ScaleAbsolute(160f, 50f);
                            imageCell = new PdfPCell(img, false)
                            {
                                Border = 0,
                                FixedHeight = 50f,
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                            };
                        }
                    }

                    upperTable.AddCell(imageCell);
                    document.Add(upperTable);

                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    document.Add(
                        new Paragraph("PURCHASE REPORT (ITEM WISE)", headerFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                            // SpacingAfter = 10f,
                        }
                    );

                    document.Add(
                        new Paragraph($"{brand.Name}", normalFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );

                    document.Add(
                        new Paragraph(
                            $"FROM {parsedFromDate:dd-MM-yyyy} TO {parsedToDate:dd-MM-yyyy}",
                            normalFont
                        )
                        {
                            Alignment = Element.ALIGN_CENTER,
                            SpacingAfter = 10f,
                        }
                    );

                    PdfPTable table = new PdfPTable(6) { WidthPercentage = 100 };
                    table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f, 2f });

                    string[] headers =
                    {
                        "Date",
                        "Invoice",
                        "Supplier",
                        "Quantity",
                        "Rate",
                        "Total Amount",
                    };
                    foreach (var header in headers)
                    {
                        table.AddCell(
                            new PdfPCell(new Phrase(header, headerFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = BaseColor.LightGray,
                                Padding = 5,
                            }
                        );
                    }

                    decimal totalAmount = result.Sum(r => r.StockAmount * r.Quantity);
                    decimal averageRate = result.Any()
                        ? result.Sum(r => r.StockAmount) / result.Count
                        : 0;

                    foreach (var row in result)
                    {
                        table.AddCell(
                            new PdfPCell(
                                new Phrase(row.BillDate.ToString("dd-MM-yyyy"), normalFont)
                            )
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.BillNo.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.Supplier ?? "Unknown", normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.Quantity.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.StockAmount.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase((row.StockAmount*row.Quantity).ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                    }
                    PdfPCell summaryCell = new PdfPCell(
                        new Phrase(
                            $"Total Amount: {totalAmount:F2} | Average Rate: {averageRate:F2}",
                            boldFont
                        )
                    )
                    {
                        Colspan = 6, // Span across all columns
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5,
                        BackgroundColor = BaseColor.LightGray,
                    };
                    table.AddCell(summaryCell);
                  
                    document.Add(table);
                    document.Close();

                    return File(ms.ToArray(),"application/pdf",
                        $"ItemPurchaseReport_Clinic_{clinicId}_Brand_{brandId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
