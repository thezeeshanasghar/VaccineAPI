using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryTransactionService _inventory;
        public BillController(Context db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        // GET /api/bill?doctorId=X&clinicId=Y
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            var bills = await _db.Bills
                .Where(b => b.DoctorId == doctorId && b.ClinicId == clinicId)
                .Include(b => b.Stocks)
                .Include(b => b.SupplierRef)
                .OrderByDescending(b => b.BillDate)
                .ToListAsync();

            var result = bills.Select(b =>
            {
                // Stock.StockAmount is already AWT-inclusive (UnitPrice * (1 + AwtPercent/100)),
                // so this sum IS the total payable — do not re-apply AWT% on top of it.
                decimal totalPayable = Math.Round(b.Stocks.Sum(s => s.OriginalQuantity * s.StockAmount), 2);
                decimal awtPercent = b.AwtPercent;
                decimal totalAmount = awtPercent > 0 ? Math.Round(totalPayable / (1 + awtPercent / 100), 2) : totalPayable;
                decimal awtAmount = totalPayable - totalAmount;
                string supplierName = b.SupplierRef != null ? b.SupplierRef.Name : (b.Supplier ?? "");

                decimal paid = b.AmountPaid ?? 0;
                decimal pending = Math.Round(totalPayable - paid, 2);
                if (pending < 0) pending = 0;
                string status = pending == 0 && paid > 0 ? "Paid"
                              : paid > 0 ? "Partial"
                              : "Unpaid";

                return new BillListDTO
                {
                    Id = b.Id,
                    BillNo = b.BillNo,
                    BillDate = b.BillDate,
                    SupplierName = supplierName,
                    TotalAmount = totalAmount,
                    AwtPercent = awtPercent,
                    AwtAmount = awtAmount,
                    TotalPayable = totalPayable,
                    AmountPaid = Math.Round(paid, 2),
                    PendingAmount = pending,
                    PaymentStatus = status,
                    IsPaid = b.IsPaid,
                    LineCount = b.Stocks.Count
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // GET /api/bill/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var bill = await _db.Bills
                .Include(b => b.Stocks).ThenInclude(s => s.Brand)
                .Include(b => b.SupplierRef)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return Ok(new { IsSuccess = false, Message = "Bill not found" });

            var brandIds = bill.Stocks.Select(s => s.BrandId).Distinct().ToList();
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            // Stock.StockAmount is already AWT-inclusive (UnitPrice * (1 + AwtPercent/100)),
            // so this sum IS the total payable — do not re-apply AWT% on top of it.
            decimal totalPayable = Math.Round(bill.Stocks.Sum(s => s.OriginalQuantity * s.StockAmount), 2);
            decimal awtPercent = bill.AwtPercent;
            decimal totalAmount = awtPercent > 0 ? Math.Round(totalPayable / (1 + awtPercent / 100), 2) : totalPayable;
            decimal awtAmount = totalPayable - totalAmount;
            string supplierName = bill.SupplierRef != null ? bill.SupplierRef.Name : (bill.Supplier ?? "");

            var lines = bill.Stocks.Select(s =>
            {
                var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == s.BrandId);
                return new BillLineDetailDTO
                {
                    StockId = s.Id,
                    BrandId = s.BrandId,
                    BrandName = s.Brand != null ? s.Brand.Name : "",
                    VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                    BatchLot = s.BatchLot ?? "",
                    Expiry = s.Expiry,
                    Quantity = s.OriginalQuantity,
                    UnitPrice = s.StockAmount,
                    LineTotal = Math.Round(s.OriginalQuantity * s.StockAmount, 2)
                };
            }).ToList();

            var dto = new BillDetailDTO
            {
                Id = bill.Id,
                BillNo = bill.BillNo,
                BillDate = bill.BillDate,
                SupplierId = bill.SupplierId,
                SupplierName = supplierName,
                AwtPercent = awtPercent,
                AwtAmount = awtAmount,
                TotalAmount = totalAmount,
                TotalPayable = totalPayable,
                IsPaid = bill.IsPaid,
                Lines = lines
            };

            return Ok(new { IsSuccess = true, ResponseData = dto });
        }

        // POST /api/bill
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BillCreateDTO dto)
        {
            if (dto == null || dto.Lines == null || dto.Lines.Count == 0)
                return Ok(new { IsSuccess = false, Message = "At least one line item is required" });

            if (dto.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice <= 0))
                return Ok(new { IsSuccess = false, Message = "Each line item must have quantity and price greater than 0. Remove the row instead of zeroing it." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Auto-generate BillNo if blank — doctor-wide uniqueness across all clinics
                string billNo = dto.BillNo;
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    string prefix = $"BILL-{dto.BillDate.Year}-";
                    var usedNos = await _db.Bills
                        .Where(b => b.DoctorId == dto.DoctorId && b.BillNo.StartsWith(prefix))
                        .Select(b => b.BillNo)
                        .ToListAsync();
                    int seq = 1;
                    while (usedNos.Contains($"{prefix}{seq:D4}")) seq++;
                    billNo = $"{prefix}{seq:D4}";
                }

                decimal totalAmount = dto.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal awtAmount = Math.Round(totalAmount * dto.AwtPercent / 100, 2);

                string supplierName = "";
                if (!string.IsNullOrWhiteSpace(dto.SupplierName))
                    supplierName = dto.SupplierName;
                else if (dto.SupplierId.HasValue)
                {
                    var sup = await _db.Suppliers.FindAsync(dto.SupplierId.Value);
                    if (sup != null) supplierName = sup.Name;
                }

                decimal totalPayable = totalAmount + awtAmount;
                decimal amountPaid = dto.AmountPaid < 0 ? 0 : (dto.AmountPaid > totalPayable ? totalPayable : dto.AmountPaid);
                bool isPaid = amountPaid >= totalPayable && totalPayable > 0;

                var bill = new Bill
                {
                    BillNo = billNo,
                    BillDate = dto.BillDate,
                    Supplier = supplierName,
                    SupplierId = dto.SupplierId,
                    DoctorId = dto.DoctorId,
                    ClinicId = dto.ClinicId,
                    AwtPercent = dto.AwtPercent,
                    AwtAmount = awtAmount,
                    AmountPaid = amountPaid,
                    PaymentMethod = amountPaid > 0 ? dto.PaymentMethod : null,
                    IsPaid = isPaid,
                    PaidDate = isPaid ? (DateTime?)DateTime.Now : null,
                    IsPAApprove = false
                };
                _db.Bills.Add(bill);
                await _db.SaveChangesAsync();

                foreach (var line in dto.Lines)
                {
                    decimal stockAmount = Math.Round(line.UnitPrice * (1 + dto.AwtPercent / 100), 4);
                    await _inventory.PostPurchaseLine(dto.DoctorId, dto.ClinicId, bill.Id, line.BrandId,
                        line.Quantity, stockAmount, line.BatchLot, line.Expiry, bill.BillDate);
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Bill saved", ResponseData = new { bill.Id, bill.BillNo } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                var inner = ex.InnerException != null ? ex.InnerException.Message : "";
                return Ok(new { IsSuccess = false, Message = ex.Message + " | INNER: " + inner });
            }
        }

        // PUT /api/bill/{id} — full edit: reverse old stock, re-create with new lines
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] BillUpdateDTO dto)
        {
            if (dto == null || dto.Lines == null || dto.Lines.Count == 0)
                return Ok(new { IsSuccess = false, Message = "At least one line item is required" });

            if (dto.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice <= 0))
                return Ok(new { IsSuccess = false, Message = "Each line item must have quantity and price greater than 0. Remove the row instead of zeroing it." });

            var bill = await _db.Bills
                .Include(b => b.Stocks)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return Ok(new { IsSuccess = false, Message = "Bill not found" });

            // Any old line being removed (not present in dto.Lines by Brand+Batch+Expiry) that still
            // has consumed units must be split off via /split-consumed first — otherwise that
            // purchase-cost history would be silently lost.
            var removedWithConsumption = bill.Stocks.Where(stock =>
                stock.Quantity < stock.OriginalQuantity &&
                !dto.Lines.Any(l => l.BrandId == stock.BrandId && l.BatchLot == stock.BatchLot && l.Expiry == stock.Expiry)
            ).ToList();
            if (removedWithConsumption.Count > 0)
            {
                var ids = string.Join(", ", removedWithConsumption.Select(s => s.Id));
                return Ok(new { IsSuccess = false, Message = $"Line(s) with stock id(s) {ids} have already-consumed units. Use split-consumed before removing them.", ResponseData = removedWithConsumption.Select(s => s.Id).ToList() });
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Reverse old stock rows
                foreach (var stock in bill.Stocks.ToList())
                {
                    await _inventory.ReverseBillLine(bill.DoctorId, bill.ClinicId, stock, bill.Id, bill.BillDate);
                }
                await _db.SaveChangesAsync();

                // Update bill header
                string supplierName = "";
                if (!string.IsNullOrWhiteSpace(dto.SupplierName))
                    supplierName = dto.SupplierName;
                else if (dto.SupplierId.HasValue)
                {
                    var sup = await _db.Suppliers.FindAsync(dto.SupplierId.Value);
                    if (sup != null) supplierName = sup.Name;
                }

                decimal totalAmount = dto.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal awtAmount = Math.Round(totalAmount * dto.AwtPercent / 100, 2);
                decimal totalPayable = totalAmount + awtAmount;

                bill.BillNo = string.IsNullOrWhiteSpace(dto.BillNo) ? bill.BillNo : dto.BillNo;
                bill.BillDate = dto.BillDate;
                bill.Supplier = supplierName;
                bill.SupplierId = dto.SupplierId;
                bill.AwtPercent = dto.AwtPercent;
                bill.AwtAmount = awtAmount;
                // Reclamp AmountPaid down if the edit shrank TotalPayable below what's already paid
                decimal paid = bill.AmountPaid ?? 0;
                if (paid > totalPayable)
                    paid = totalPayable;
                bill.AmountPaid = paid;
                bill.IsPaid = paid >= totalPayable && totalPayable > 0;
                bill.PaidDate = bill.IsPaid && bill.PaidDate == null ? (DateTime?)DateTime.Now : bill.PaidDate;

                // Create new stock rows (consolidate only against a row already on THIS bill —
                // old rows for this bill were just removed above, so this only matters if
                // dto.Lines itself contains duplicate Brand+Batch+Expiry entries)
                foreach (var line in dto.Lines)
                {
                    decimal stockAmount = Math.Round(line.UnitPrice * (1 + dto.AwtPercent / 100), 4);
                    await _inventory.PostPurchaseLine(bill.DoctorId, bill.ClinicId, bill.Id, line.BrandId,
                        line.Quantity, stockAmount, line.BatchLot, line.Expiry, bill.BillDate);
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Bill updated", ResponseData = new { bill.Id, bill.BillNo } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // POST /api/bill/{id}/payment — record a payment instalment
        [HttpPost("{id}/payment")]
        public async Task<IActionResult> AddPayment(int id, [FromBody] SupplierPaymentCreateDTO dto)
        {
            var bill = await _db.Bills
                .Include(b => b.Stocks)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return Ok(new { IsSuccess = false, Message = "Bill not found" });

            if (dto.Amount <= 0)
                return Ok(new { IsSuccess = false, Message = "Payment amount must be greater than 0" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Stock.StockAmount is already AWT-inclusive — do not add AwtAmount again
                decimal totalPayable = Math.Round(bill.Stocks.Sum(s => s.OriginalQuantity * s.StockAmount), 2);

                decimal alreadyPaid = bill.AmountPaid ?? 0;
                decimal remaining = totalPayable - alreadyPaid;
                if (remaining <= 0)
                    return Ok(new { IsSuccess = false, Message = "Bill is already fully paid" });
                if (dto.Amount > remaining)
                    return Ok(new { IsSuccess = false, Message = $"Payment of Rs {dto.Amount:N2} exceeds remaining balance of Rs {remaining:N2}" });

                // Add payment record
                var payment = new SupplierPayment
                {
                    BillId = id,
                    SupplierId = bill.SupplierId ?? dto.SupplierId ?? 0,
                    ClinicId = bill.ClinicId,
                    Amount = dto.Amount,
                    PaymentMethod = dto.PaymentMethod,
                    Notes = dto.Notes,
                    PaymentDate = dto.PaymentDate
                };
                _db.SupplierPayments.Add(payment);

                // Update bill AmountPaid
                decimal newPaid = alreadyPaid + dto.Amount;
                bill.AmountPaid = newPaid;
                bill.PaymentMethod = dto.PaymentMethod;
                bill.IsPaid = newPaid >= totalPayable && totalPayable > 0;
                if (bill.IsPaid && bill.PaidDate == null)
                    bill.PaidDate = DateTime.Now;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Payment recorded", ResponseData = new { AmountPaid = newPaid, IsPaid = bill.IsPaid } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // GET /api/bill/{id}/payments — payment history for a bill
        [HttpGet("{id}/payments")]
        public async Task<IActionResult> GetPayments(int id)
        {
            var payments = await _db.SupplierPayments
                .Where(p => p.BillId == id)
                .OrderBy(p => p.PaymentDate)
                .Select(p => new SupplierPaymentDTO
                {
                    Id = p.Id,
                    Amount = p.Amount,
                    PaymentMethod = p.PaymentMethod,
                    PaymentDate = p.PaymentDate,
                    Notes = p.Notes
                })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = payments });
        }

        // GET /api/bill/{billId}/line/{stockId}/consumed-check
        [HttpGet("{billId}/line/{stockId}/consumed-check")]
        public async Task<IActionResult> ConsumedCheck(int billId, int stockId)
        {
            var stock = await _db.Stocks
                .Include(s => s.Brand)
                .FirstOrDefaultAsync(s => s.Id == stockId && s.BillId == billId);

            if (stock == null)
                return Ok(new { IsSuccess = false, Message = "Stock line not found for this bill" });

            int consumed = stock.OriginalQuantity - stock.Quantity;

            var result = new ConsumedCheckDTO
            {
                StockId = stock.Id,
                Quantity = stock.Quantity,
                OriginalQuantity = stock.OriginalQuantity,
                Consumed = consumed,
                UnitPrice = stock.StockAmount,
                ConsumedAmount = Math.Round(consumed * stock.StockAmount, 2),
                BrandName = stock.Brand != null ? stock.Brand.Name : "",
                BatchLot = stock.BatchLot ?? ""
            };

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // POST /api/bill/{billId}/line/{stockId}/split-consumed
        // Splits off the already-consumed portion of a stock line into a new, fully-paid bill
        // (preserves purchase-cost history), leaving the original line holding only the
        // unconsumed remainder (Quantity == OriginalQuantity afterward).
        [HttpPost("{billId}/line/{stockId}/split-consumed")]
        public async Task<IActionResult> SplitConsumed(int billId, int stockId)
        {
            var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == billId);
            if (bill == null)
                return Ok(new { IsSuccess = false, Message = "Bill not found" });

            var stock = await _db.Stocks.FirstOrDefaultAsync(s => s.Id == stockId && s.BillId == billId);
            if (stock == null)
                return Ok(new { IsSuccess = false, Message = "Stock line not found for this bill" });

            int consumed = stock.OriginalQuantity - stock.Quantity;
            if (consumed <= 0)
                return Ok(new { IsSuccess = false, Message = "No consumed units on this line" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                decimal consumedAmount = Math.Round(consumed * stock.StockAmount, 2);

                // Auto-generate BillNo — doctor-wide uniqueness across all clinics (same pattern as Create)
                string prefix = $"BILL-{bill.BillDate.Year}-";
                var usedNos = await _db.Bills
                    .Where(b => b.DoctorId == bill.DoctorId && b.BillNo.StartsWith(prefix))
                    .Select(b => b.BillNo)
                    .ToListAsync();
                int seq = 1;
                while (usedNos.Contains($"{prefix}{seq:D4}")) seq++;
                string newBillNo = $"{prefix}{seq:D4}";

                var newBill = new Bill
                {
                    BillNo = newBillNo,
                    BillDate = bill.BillDate,
                    Supplier = bill.Supplier,
                    SupplierId = bill.SupplierId,
                    DoctorId = bill.DoctorId,
                    ClinicId = bill.ClinicId,
                    AwtPercent = bill.AwtPercent,
                    AwtAmount = 0,
                    AmountPaid = consumedAmount,
                    PaymentMethod = bill.PaymentMethod,
                    IsPaid = true,
                    PaidDate = DateTime.Now,
                    IsPAApprove = false
                };
                _db.Bills.Add(newBill);
                await _db.SaveChangesAsync();

                var newStock = new Stock
                {
                    BrandId = stock.BrandId,
                    BillId = newBill.Id,
                    Quantity = consumed,
                    OriginalQuantity = consumed,
                    StockAmount = stock.StockAmount,
                    BatchLot = stock.BatchLot,
                    Expiry = stock.Expiry
                };
                _db.Stocks.Add(newStock);
                await _db.SaveChangesAsync(); // need newStock.Id for the ledger row

                // Shrink the original line to the unconsumed remainder
                stock.OriginalQuantity = stock.Quantity;

                _inventory.LogSplitConsumed(bill.DoctorId, bill.ClinicId, stock.BrandId, stock.Id, newStock.Id,
                    stock.BatchLot, stock.Expiry, consumed, stock.StockAmount, bill.Id, newBill.Id, bill.BillDate);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                var result = new SplitConsumedResultDTO
                {
                    NewBillId = newBill.Id,
                    NewBillNo = newBill.BillNo,
                    ConsumedAmount = consumedAmount
                };

                return Ok(new { IsSuccess = true, Message = "Consumed units moved to new paid bill", ResponseData = result });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // DELETE /api/bill/{id}/reverse
        [HttpDelete("{id}/reverse")]
        public async Task<IActionResult> Reverse(int id, [FromQuery] bool force = false)
        {
            var bill = await _db.Bills
                .Include(b => b.Stocks)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return Ok(new { IsSuccess = false, Message = "Bill not found" });

            // Legacy-data guard: a bill with money paid but no Stock rows of its own has nothing
            // here to structurally reverse (its purchase was never recorded against this bill —
            // pre-existing data issue). Deleting it would erase the paid amount with no stock
            // correction anywhere, so require explicit confirmation instead of silently succeeding.
            if (bill.Stocks.Count == 0 && (bill.AmountPaid ?? 0) > 0 && !force)
            {
                return Ok(new
                {
                    IsSuccess = false,
                    Message = $"This bill has Rs {bill.AmountPaid:N2} paid but no recorded line items — there is no stock to reverse. " +
                              "Reversing will only delete the bill and its payment record. Confirm to proceed anyway.",
                    ResponseData = new { RequiresForce = true }
                });
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var stock in bill.Stocks.ToList())
                {
                    await _inventory.ReverseBillStock(bill.DoctorId, bill.ClinicId, stock, bill.Id, bill.BillDate);
                }

                var payments = await _db.SupplierPayments.Where(p => p.BillId == id).ToListAsync();
                _db.SupplierPayments.RemoveRange(payments);

                _db.Bills.Remove(bill);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Bill reversed" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }
    }
}
