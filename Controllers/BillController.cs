using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillController : ControllerBase
    {
        private readonly Context _db;
        public BillController(Context db) { _db = db; }

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
                decimal totalAmount = b.Stocks.Sum(s => s.Quantity * s.StockAmount);
                decimal awtPercent = b.AwtPercent;
                decimal awtAmount = Math.Round(totalAmount * awtPercent / 100, 2);
                decimal totalPayable = totalAmount + awtAmount;
                string supplierName = b.SupplierRef != null ? b.SupplierRef.Name : (b.Supplier ?? "");

                decimal paid = b.AmountPaid ?? 0;
                decimal pending = Math.Round(totalPayable - paid, 2);
                if (pending < 0) pending = 0;
                string status = paid >= totalPayable && totalPayable > 0 ? "Paid"
                              : paid > 0 ? "Partial"
                              : "Unpaid";

                return new BillListDTO
                {
                    Id = b.Id,
                    BillNo = b.BillNo,
                    BillDate = b.BillDate,
                    SupplierName = supplierName,
                    TotalAmount = Math.Round(totalAmount, 2),
                    AwtPercent = awtPercent,
                    AwtAmount = awtAmount,
                    TotalPayable = Math.Round(totalPayable, 2),
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

            decimal totalAmount = bill.Stocks.Sum(s => s.Quantity * s.StockAmount);
            decimal awtPercent = bill.AwtPercent;
            decimal awtAmount = Math.Round(totalAmount * awtPercent / 100, 2);
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
                    Quantity = s.Quantity,
                    UnitPrice = s.StockAmount,
                    LineTotal = Math.Round(s.Quantity * s.StockAmount, 2)
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
                TotalAmount = Math.Round(totalAmount, 2),
                TotalPayable = Math.Round(totalAmount + awtAmount, 2),
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

                decimal amountPaid = dto.AmountPaid < 0 ? 0 : dto.AmountPaid;
                decimal totalPayable = totalAmount + awtAmount;
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
                    var stock = new Stock
                    {
                        BrandId = line.BrandId,
                        BillId = bill.Id,
                        Quantity = line.Quantity,
                        OriginalQuantity = line.Quantity,
                        StockAmount = line.UnitPrice,
                        BatchLot = line.BatchLot,
                        Expiry = line.Expiry
                    };
                    _db.Stocks.Add(stock);

                    // Increment BrandAmount.Count only — no weighted avg
                    var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                        x.BrandId == line.BrandId &&
                        x.DoctorId == dto.DoctorId &&
                        x.ClinicId == dto.ClinicId);
                    if (ba != null)
                        ba.Count += line.Quantity;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Bill saved", ResponseData = new { bill.Id, bill.BillNo } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = ex.Message });
            }
        }

        // DELETE /api/bill/{id}/reverse
        [HttpDelete("{id}/reverse")]
        public async Task<IActionResult> Reverse(int id)
        {
            var bill = await _db.Bills
                .Include(b => b.Stocks)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return Ok(new { IsSuccess = false, Message = "Bill not found" });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var stock in bill.Stocks)
                {
                    var ba = await _db.BrandAmounts.FirstOrDefaultAsync(x =>
                        x.BrandId == stock.BrandId &&
                        x.DoctorId == bill.DoctorId &&
                        x.ClinicId == bill.ClinicId);
                    if (ba != null)
                        ba.Count = Math.Max(0, ba.Count - stock.Quantity);

                    _db.Stocks.Remove(stock);
                }

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
