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
    public class SupplierPaymentController : ControllerBase
    {
        private readonly Context _db;
        public SupplierPaymentController(Context db) { _db = db; }

        // GET /api/supplierpayment/ledger?supplierId=X&clinicId=Y
        [HttpGet("ledger")]
        public async Task<IActionResult> GetLedger([FromQuery] long supplierId, [FromQuery] long clinicId)
        {
            var supplier = await _db.Suppliers.FindAsync(supplierId);
            if (supplier == null)
                return Ok(new { IsSuccess = false, Message = "Supplier not found" });

            // Bills for this supplier at this clinic
            var bills = await _db.Bills
                .Include(b => b.Stocks)
                .Where(b => b.SupplierId == supplierId && b.ClinicId == clinicId)
                .OrderBy(b => b.BillDate)
                .ThenBy(b => b.Id)
                .ToListAsync();

            // Standalone payments for this supplier at this clinic
            var payments = await _db.SupplierPayments
                .Where(p => p.SupplierId == supplierId && p.ClinicId == clinicId && p.BillId == null)
                .OrderBy(p => p.PaymentDate)
                .ThenBy(p => p.Id)
                .ToListAsync();

            // Build ledger entries: opening balance + bills (debit) + payments (credit), sorted by date
            var entries = new List<object>();
            decimal balance = 0;

            // Opening balance row
            decimal openingBalance = supplier.OpeningBalance ?? 0;
            if (openingBalance != 0)
            {
                balance += openingBalance;
                entries.Add(new
                {
                    Type = "OpeningBalance",
                    Date = (DateTime?)null,
                    Description = "Opening Balance",
                    Debit = openingBalance > 0 ? openingBalance : 0m,
                    Credit = openingBalance < 0 ? Math.Abs(openingBalance) : 0m,
                    Balance = balance,
                    ReferenceId = 0L
                });
            }

            // Merge bills and payments into chronological order
            var billItems = bills.Select(b =>
            {
                // Stock.StockAmount is already AWT-inclusive — do not add AwtAmount again
                decimal totalPayable = b.Stocks.Sum(s => (decimal)s.Quantity * s.StockAmount);
                return new { Date = b.BillDate, SortKey = (long)b.Id, IsBill = true, Bill = b, TotalPayable = totalPayable };
            }).ToList();

            var paymentItems = payments.Select(p =>
                new { Date = p.PaymentDate, SortKey = (long)p.Id, IsBill = false, Payment = p }
            ).ToList();

            // Interleave by date
            var allDates = billItems.Select(x => (x.Date, x.SortKey, IsBill: true))
                .Concat(paymentItems.Select(x => (x.Date, x.SortKey, IsBill: false)))
                .OrderBy(x => x.Date)
                .ThenBy(x => x.IsBill ? 0 : 1) // bills before payments on same day
                .ThenBy(x => x.SortKey)
                .ToList();

            foreach (var item in allDates)
            {
                if (item.IsBill)
                {
                    var b = billItems.First(x => x.SortKey == item.SortKey).Bill;
                    // Stock.StockAmount is already AWT-inclusive — do not add AwtAmount again
                    decimal totalPayable = b.Stocks.Sum(s => (decimal)s.Quantity * s.StockAmount);
                    balance += totalPayable;
                    entries.Add(new
                    {
                        Type = "Bill",
                        Date = (DateTime?)b.BillDate,
                        Description = b.BillNo,
                        Debit = totalPayable,
                        Credit = 0m,
                        Balance = balance,
                        ReferenceId = (long)b.Id
                    });

                    // Bill payment row if partly/fully paid at creation
                    decimal paidOnBill = b.AmountPaid ?? 0;
                    if (paidOnBill > 0)
                    {
                        balance -= paidOnBill;
                        entries.Add(new
                        {
                            Type = "Payment",
                            Date = (DateTime?)b.BillDate,
                            Description = "Payment on " + b.BillNo,
                            Debit = 0m,
                            Credit = paidOnBill,
                            Balance = balance,
                            ReferenceId = 0L
                        });
                    }
                }
                else
                {
                    var p = paymentItems.First(x => x.SortKey == item.SortKey).Payment;
                    balance -= p.Amount;
                    entries.Add(new
                    {
                        Type = "Payment",
                        Date = (DateTime?)p.PaymentDate,
                        Description = p.PaymentMethod + (string.IsNullOrWhiteSpace(p.Notes) ? "" : " — " + p.Notes),
                        Debit = 0m,
                        Credit = p.Amount,
                        Balance = balance,
                        ReferenceId = p.Id
                    });
                }
            }

            // Stock.StockAmount is already AWT-inclusive — do not add AwtAmount again
            decimal totalBills = bills.Sum(b => b.Stocks.Sum(s => (decimal)s.Quantity * s.StockAmount));
            decimal totalAWT = bills.Sum(b => b.AwtAmount ?? 0);
            decimal totalPaidOnBills = bills.Sum(b => b.AmountPaid ?? 0);
            decimal totalStandalonePayments = payments.Sum(p => p.Amount);
            decimal totalPaid = totalPaidOnBills + totalStandalonePayments;

            return Ok(new
            {
                IsSuccess = true,
                ResponseData = new
                {
                    SupplierId = supplierId,
                    SupplierName = supplier.Name,
                    Phone = supplier.Phone,
                    TotalBills = totalBills,
                    TotalAWT = totalAWT,
                    TotalPaid = totalPaid,
                    OutstandingBalance = balance,
                    Entries = entries
                }
            });
        }

        // POST /api/supplierpayment — standalone payment (not tied to a bill)
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SupplierPaymentCreateDTO dto)
        {
            if (dto.Amount <= 0)
                return Ok(new { IsSuccess = false, Message = "Amount must be greater than 0" });

            var payment = new SupplierPayment
            {
                SupplierId = dto.SupplierId ?? 0,
                ClinicId = dto.ClinicId,
                Amount = dto.Amount,
                PaymentDate = dto.PaymentDate == default ? DateTime.Today : dto.PaymentDate,
                PaymentMethod = dto.PaymentMethod ?? "Cash",
                Notes = dto.Notes
            };

            _db.SupplierPayments.Add(payment);
            await _db.SaveChangesAsync();

            return Ok(new { IsSuccess = true, Message = "Payment recorded", ResponseData = new SupplierPaymentDTO
            {
                Id = payment.Id,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                PaymentDate = payment.PaymentDate,
                Notes = payment.Notes
            }});
        }

        // DELETE /api/supplierpayment/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var payment = await _db.SupplierPayments.FindAsync(id);
            if (payment == null)
                return Ok(new { IsSuccess = false, Message = "Payment not found" });

            _db.SupplierPayments.Remove(payment);
            await _db.SaveChangesAsync();

            return Ok(new { IsSuccess = true, Message = "Payment deleted" });
        }
    }
}
