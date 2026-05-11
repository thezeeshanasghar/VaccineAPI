using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PnLController : ControllerBase
    {
        private readonly Context _db;

        public PnLController(Context context)
        {
            _db = context;
        }

        // GET api/PnL/doctor/{doctorId}?year=2025&month=5&clinicId=3
        [HttpGet("doctor/{doctorId}")]
        public ActionResult GetPnL(
            long doctorId,
            [FromQuery] int year,
            [FromQuery] int month,
            [FromQuery] long? clinicId)
        {
            if (year == 0 || month == 0)
                return BadRequest(new { IsSuccess = false, Message = "Year and month are required" });

            var from = new DateTime(year, month, 1);
            var to   = from.AddMonths(1).AddDays(-1);

            // All clinics for this doctor
            var clinics = _db.Clinics
                .Where(c => c.DoctorId == doctorId)
                .Select(c => new { c.Id, c.Name })
                .ToList();

            var targetClinics = clinicId.HasValue
                ? clinics.Where(c => c.Id == clinicId.Value).ToList()
                : clinics;

            var results = new List<object>();

            foreach (var clinic in targetClinics)
            {
                // 1. Vaccination fee income: schedules where IsDone=true, GivenDate in range, Child.ClinicId = clinic
                var doneSchedules = _db.Schedules
                    .Include(s => s.Child)
                    .Include(s => s.Brand)
                    .Where(s => s.IsDone
                        && s.GivenDate.HasValue
                        && s.GivenDate.Value.Date >= from.Date
                        && s.GivenDate.Value.Date <= to.Date
                        && s.Child.ClinicId == clinic.Id)
                    .ToList();

                decimal vaccinationFeeIncome = doneSchedules
                    .Where(s => s.Amount.HasValue)
                    .Sum(s => s.Amount.Value);

                // 2. Vaccine profit: fee - purchase price (BrandAmount.PurchasedAmt weighted avg)
                var brandIds = doneSchedules
                    .Where(s => s.BrandId.HasValue)
                    .Select(s => s.BrandId.Value)
                    .Distinct()
                    .ToList();

                var brandAmounts = _db.BrandAmounts
                    .Where(ba => ba.ClinicId == clinic.Id && brandIds.Contains(ba.BrandId))
                    .ToDictionary(ba => ba.BrandId, ba => ba.PurchasedAmt);

                decimal vaccineProfit = doneSchedules
                    .Where(s => s.BrandId.HasValue && s.Amount.HasValue)
                    .Sum(s =>
                    {
                        var purchase = brandAmounts.ContainsKey(s.BrandId.Value)
                            ? brandAmounts[s.BrandId.Value]
                            : 0;
                        return s.Amount.Value - purchase;
                    });

                // 3. Direct sale income & profit
                var directSales = _db.DirectSales
                    .Where(d => d.ClinicId == clinic.Id
                        && d.SaleDate.Date >= from.Date
                        && d.SaleDate.Date <= to.Date)
                    .ToList();

                decimal directSaleRevenue = directSales.Sum(d => d.TotalSaleValue);
                decimal directSaleProfit  = directSales.Sum(d => d.Profit);

                decimal totalIncome = vaccinationFeeIncome + directSaleRevenue;
                decimal totalProfit = vaccineProfit + directSaleProfit;

                // 4. Expenses
                var expenses = _db.Expenses
                    .Include(e => e.Category)
                    .Where(e => e.ClinicId == clinic.Id
                        && e.ExpenseDate >= from.Date
                        && e.ExpenseDate <= to.Date)
                    .ToList();

                decimal recurringExpenses = expenses
                    .Where(e => e.ExpenseType == "Recurring")
                    .Sum(e => e.Amount);

                decimal capitalExpenses = expenses
                    .Where(e => e.ExpenseType == "Capital")
                    .Sum(e => e.Amount);

                decimal totalExpenses = recurringExpenses + capitalExpenses;

                // Expense breakdown by category
                var expenseByCategory = expenses
                    .GroupBy(e => e.Category != null ? e.Category.Name : "Uncategorised")
                    .Select(g => new { Category = g.Key, Amount = g.Sum(x => x.Amount) })
                    .OrderByDescending(g => g.Amount)
                    .ToList();

                // 5. P&L levels
                decimal operationalPnL  = totalIncome - recurringExpenses;
                decimal fullCapitalPnL  = totalIncome - totalExpenses;

                // Monthly depreciation for this clinic's active assets
                decimal monthlyDeprn = _db.FixedAssets
                    .Where(a => a.ClinicId == clinic.Id && !a.IsDisposed)
                    .Sum(a => a.MonthlyDeprn);
                decimal depreciatedPnL = totalIncome - recurringExpenses - monthlyDeprn;

                results.Add(new
                {
                    ClinicId   = clinic.Id,
                    ClinicName = clinic.Name,
                    Income = new
                    {
                        VaccinationFee   = vaccinationFeeIncome,
                        VaccineProfit    = vaccineProfit,
                        DirectSaleRevenue = directSaleRevenue,
                        DirectSaleProfit  = directSaleProfit,
                        TotalRevenue     = totalIncome,
                        TotalProfit      = totalProfit
                    },
                    Expenses = new
                    {
                        Recurring   = recurringExpenses,
                        Capital     = capitalExpenses,
                        Total       = totalExpenses,
                        ByCategory  = expenseByCategory
                    },
                    PnL = new
                    {
                        Operational  = operationalPnL,
                        FullCapital  = fullCapitalPnL,
                        Depreciated  = depreciatedPnL,
                        MonthlyDeprn = monthlyDeprn
                    }
                });
            }

            return Ok(new
            {
                IsSuccess = true,
                Message   = (string)null,
                ResponseData = new
                {
                    Year     = year,
                    Month    = month,
                    Clinics  = results,
                    Totals   = new
                    {
                        TotalRevenue  = results.Sum(r => (decimal)((dynamic)r).Income.TotalRevenue),
                        TotalExpenses = results.Sum(r => (decimal)((dynamic)r).Expenses.Total),
                        OperationalPnL = results.Sum(r => (decimal)((dynamic)r).PnL.Operational),
                        FullCapitalPnL = results.Sum(r => (decimal)((dynamic)r).PnL.FullCapital),
                        DepreciatedPnL = results.Sum(r => (decimal)((dynamic)r).PnL.Depreciated)
                    }
                }
            });
        }

        // GET api/PnL/doctor/{doctorId}/income-summary?year=2025&clinicId=3
        // Returns month-by-month income for a full year (for charts)
        [HttpGet("doctor/{doctorId}/income-summary")]
        public ActionResult GetIncomeSummary(long doctorId, [FromQuery] int year, [FromQuery] long? clinicId)
        {
            if (year == 0) year = DateTime.UtcNow.Year;

            var months = new List<object>();
            for (int m = 1; m <= 12; m++)
            {
                var from = new DateTime(year, m, 1);
                var to   = from.AddMonths(1).AddDays(-1);

                var clinicFilter = clinicId.HasValue
                    ? _db.Clinics.Where(c => c.DoctorId == doctorId && c.Id == clinicId.Value).Select(c => c.Id).ToList()
                    : _db.Clinics.Where(c => c.DoctorId == doctorId).Select(c => c.Id).ToList();

                decimal fee = _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.IsDone && s.GivenDate.HasValue
                        && s.GivenDate.Value.Date >= from.Date
                        && s.GivenDate.Value.Date <= to.Date
                        && clinicFilter.Contains(s.Child.ClinicId)
                        && s.Amount.HasValue)
                    .Sum(s => (decimal?)s.Amount) ?? 0;

                decimal sales = _db.DirectSales
                    .Where(d => clinicFilter.Contains(d.ClinicId)
                        && d.SaleDate.Date >= from.Date
                        && d.SaleDate.Date <= to.Date)
                    .Sum(d => (decimal?)d.TotalSaleValue) ?? 0;

                decimal expenses = _db.Expenses
                    .Where(e => clinicFilter.Contains(e.ClinicId)
                        && e.ExpenseDate >= from.Date
                        && e.ExpenseDate <= to.Date)
                    .Sum(e => (decimal?)e.Amount) ?? 0;

                months.Add(new
                {
                    Month    = m,
                    Income   = fee + sales,
                    Expenses = expenses,
                    Net      = (fee + sales) - expenses
                });
            }

            return Ok(new { IsSuccess = true, Message = (string)null, ResponseData = months });
        }
    }
}
