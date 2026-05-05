using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SupplierPaymentController : ControllerBase
    {
        private readonly Context _db;

        public SupplierPaymentController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public ActionResult<Response<List<SupplierPaymentDTO>>> GetPayments(
            [FromQuery] long supplierId,
            [FromQuery] long clinicId)
        {
            var payments = _db.SupplierPayments
                .Include(p => p.Supplier)
                .Include(p => p.Bill)
                .Where(p => p.SupplierId == supplierId && p.ClinicId == clinicId)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new SupplierPaymentDTO
                {
                    Id = p.Id,
                    SupplierId = p.SupplierId,
                    SupplierName = p.Supplier.Name,
                    ClinicId = p.ClinicId,
                    Amount = p.Amount,
                    PaymentDate = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod,
                    Notes = p.Notes,
                    BillId = p.BillId,
                    BillNo = p.Bill != null ? p.Bill.BillNo : null
                })
                .ToList();

            return Ok(new Response<List<SupplierPaymentDTO>>(true, null, payments));
        }

        [HttpPost]
        public async Task<ActionResult<Response<SupplierPaymentDTO>>> Create([FromBody] SupplierPaymentDTO dto)
        {
            if (dto.SupplierId <= 0)
                return BadRequest(new Response<SupplierPaymentDTO>(false, "SupplierId is required", null));
            if (dto.ClinicId <= 0)
                return BadRequest(new Response<SupplierPaymentDTO>(false, "ClinicId is required", null));
            if (dto.Amount <= 0)
                return BadRequest(new Response<SupplierPaymentDTO>(false, "Amount must be greater than zero", null));

            var supplier = await _db.Suppliers.FindAsync(dto.SupplierId);
            if (supplier == null)
                return NotFound(new Response<SupplierPaymentDTO>(false, "Supplier not found", null));

            var payment = new SupplierPayment
            {
                SupplierId = dto.SupplierId,
                ClinicId = dto.ClinicId,
                Amount = dto.Amount,
                PaymentDate = dto.PaymentDate == default ? DateTime.Now : dto.PaymentDate,
                PaymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "Cash" : dto.PaymentMethod,
                Notes = dto.Notes?.Trim(),
                BillId = dto.BillId > 0 ? dto.BillId : null
            };

            _db.SupplierPayments.Add(payment);
            await _db.SaveChangesAsync();

            dto.Id = payment.Id;
            dto.SupplierName = supplier.Name;
            return Ok(new Response<SupplierPaymentDTO>(true, "Payment recorded", dto));
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<Response<object>>> Delete(long id)
        {
            var payment = await _db.SupplierPayments.FindAsync(id);
            if (payment == null)
                return NotFound(new Response<object>(false, "Payment not found", null));

            _db.SupplierPayments.Remove(payment);
            await _db.SaveChangesAsync();
            return Ok(new Response<object>(true, "Payment deleted", null));
        }

        [HttpGet("ledger")]
        public ActionResult<Response<SupplierLedgerDTO>> GetLedger(
            [FromQuery] long supplierId,
            [FromQuery] long clinicId)
        {
            var supplier = _db.Suppliers.FirstOrDefault(s => s.Id == supplierId);
            if (supplier == null)
                return NotFound(new Response<SupplierLedgerDTO>(false, "Supplier not found", null));

            var bills = _db.Bills
                .Include(b => b.Stocks)
                .Where(b => b.SupplierId == supplierId && b.ClinicId == clinicId)
                .OrderBy(b => b.BillDate)
                .ToList();

            var payments = _db.SupplierPayments
                .Where(p => p.SupplierId == supplierId && p.ClinicId == clinicId)
                .OrderBy(p => p.PaymentDate)
                .ToList();

            var openingBalance = supplier.OpeningBalance ?? 0m;
            var entries = new List<LedgerEntryDTO>();

            if (openingBalance != 0)
            {
                entries.Add(new LedgerEntryDTO
                {
                    Date = DateTime.MinValue,
                    Type = "OpeningBalance",
                    Description = "Opening Balance",
                    Debit = openingBalance > 0 ? openingBalance : 0,
                    Credit = openingBalance < 0 ? Math.Abs(openingBalance) : 0,
                    Balance = 0,
                    ReferenceId = null
                });
            }

            // Build a set of bill IDs that already have a SupplierPayment record
            var paidBillIds = new HashSet<int>(
                payments.Where(p => p.BillId.HasValue).Select(p => p.BillId!.Value)
            );

            decimal legacyPaidTotal = 0m;

            foreach (var bill in bills)
            {
                var billTotal = bill.Stocks.Sum(s => s.StockAmount * s.Quantity);
                entries.Add(new LedgerEntryDTO
                {
                    Date = bill.BillDate,
                    Type = "Bill",
                    Description = $"Purchase Bill #{bill.BillNo}",
                    Debit = billTotal,
                    Credit = 0,
                    Balance = 0,
                    ReferenceId = bill.Id
                });

                if (bill.AwtAmount.HasValue && bill.AwtAmount.Value > 0)
                {
                    entries.Add(new LedgerEntryDTO
                    {
                        Date = bill.BillDate,
                        Type = "AWT",
                        Description = $"AWT on Bill #{bill.BillNo}",
                        Debit = 0,
                        Credit = bill.AwtAmount.Value,
                        Balance = 0,
                        ReferenceId = bill.Id
                    });
                }

                // Legacy: bill was marked paid via old IsPaid checkbox — no SupplierPayment exists
                if (bill.IsPaid && !bill.AmountPaid.HasValue && !paidBillIds.Contains(bill.Id))
                {
                    decimal legacyCredit = billTotal - (bill.AwtAmount ?? 0m);
                    legacyPaidTotal += legacyCredit;
                    entries.Add(new LedgerEntryDTO
                    {
                        Date = bill.PaidDate ?? bill.BillDate,
                        Type = "Payment",
                        Description = $"Payment — Bill #{bill.BillNo}",
                        Debit = 0,
                        Credit = legacyCredit,
                        Balance = 0,
                        ReferenceId = bill.Id
                    });
                }
            }

            foreach (var p in payments)
            {
                entries.Add(new LedgerEntryDTO
                {
                    Date = p.PaymentDate,
                    Type = "Payment",
                    Description = $"{p.PaymentMethod} Payment{(string.IsNullOrEmpty(p.Notes) ? "" : " — " + p.Notes)}",
                    Debit = 0,
                    Credit = p.Amount,
                    Balance = 0,
                    ReferenceId = p.Id
                });
            }

            // Sort: opening balance first, then chronologically, AWT always after its bill
            entries = entries
                .OrderBy(e => e.Type == "OpeningBalance" ? 0 : 1)
                .ThenBy(e => e.Date)
                .ThenBy(e => e.Type == "AWT" ? 1 : 0)
                .ToList();

            // Calculate running balance (positive = we owe supplier)
            decimal balance = openingBalance;
            foreach (var entry in entries)
            {
                if (entry.Type == "OpeningBalance")
                {
                    entry.Balance = openingBalance;
                    continue;
                }
                balance += entry.Debit - entry.Credit;
                entry.Balance = balance;
            }

            var totalBills = bills.Sum(b => b.Stocks.Sum(s => s.StockAmount * s.Quantity));
            var totalAwt = bills.Where(b => b.AwtAmount.HasValue).Sum(b => b.AwtAmount!.Value);
            var totalPaid = payments.Sum(p => p.Amount) + legacyPaidTotal;
            var outstanding = openingBalance + totalBills - totalAwt - totalPaid;

            var ledger = new SupplierLedgerDTO
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,
                ClinicId = clinicId,
                OpeningBalance = openingBalance,
                TotalBills = totalBills,
                TotalAWT = totalAwt,
                TotalPaid = totalPaid,
                OutstandingBalance = outstanding,
                Entries = entries
            };

            return Ok(new Response<SupplierLedgerDTO>(true, null, ledger));
        }
    }
}
