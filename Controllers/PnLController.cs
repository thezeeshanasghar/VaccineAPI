using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PnLController : ControllerBase
    {
        private readonly Context _db;

        public PnLController(Context db)
        {
            _db = db;
        }

        // GET /api/pnl?doctorId=X&clinicId=Y&fromDate=...&toDate=...&expenseMode=operational|fullCapital|depreciated
        [HttpGet]
        public async Task<IActionResult> GetPnL(
            [FromQuery] long doctorId,
            [FromQuery] long? clinicId,
            [FromQuery] DateTime fromDate,
            [FromQuery] DateTime toDate,
            [FromQuery] string? expenseMode)
        {
            var mode = (expenseMode ?? "operational").ToLower();
            if (mode != "operational" && mode != "fullcapital" && mode != "depreciated")
                mode = "operational";

            var from = fromDate.Date;
            var to = toDate.Date;
            if (from > to)
                return Ok(new { IsSuccess = false, Message = "Invalid date range" });

            var clinics = await _db.Clinics
                .Where(c => c.DoctorId == doctorId && (clinicId == null || c.Id == clinicId))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();

            if (clinics.Count == 0)
                return Ok(new { IsSuccess = false, Message = "No clinics found" });

            var clinicIds = clinics.Select(c => c.Id).ToList();

            // --- Vaccine revenue/cost: Schedules given in range, grouped by clinic ---
            var scheduleRows = await _db.Schedules
                .Where(s => clinicIds.Contains(s.Child.ClinicId)
                    && s.IsDone == true
                    && s.GivenDate.HasValue
                    && s.GivenDate.Value.Date >= from
                    && s.GivenDate.Value.Date <= to)
                .Select(s => new { ClinicId = s.Child.ClinicId, Amount = s.Amount ?? 0, VaccineCost = s.VaccineCost ?? 0 })
                .ToListAsync();

            // --- Consultation income: InvoiceSubmissions in range, excluding cancelled/reversed ---
            var invoiceRows = await _db.InvoiceSubmissions
                .Where(x => clinicIds.Contains(x.ClinicId ?? 0)
                    && x.InvoiceDate.Date >= from
                    && x.InvoiceDate.Date <= to
                    && x.InvoiceStatus != "Cancelled"
                    && x.InvoiceStatus != "UngiveReversal")
                .Select(x => new { ClinicId = x.ClinicId ?? 0, x.ConsultationFee })
                .ToListAsync();

            // --- Direct sales in range ---
            var directSaleRows = await _db.DirectSales
                .Where(d => clinicIds.Contains(d.ClinicId)
                    && d.SaleDate.Date >= from
                    && d.SaleDate.Date <= to)
                .Select(d => new { d.ClinicId, d.TotalSaleValue, d.TotalCostValue, d.Profit })
                .ToListAsync();

            // --- Expenses: direct (per-clinic, not shared) ---
            var directExpenseRows = await _db.Expenses
                .Where(e => e.DoctorId == doctorId
                    && e.IsShared == false
                    && e.ClinicId.HasValue
                    && clinicIds.Contains(e.ClinicId.Value)
                    && e.ExpenseDate.Date >= from
                    && e.ExpenseDate.Date <= to)
                .ToListAsync();

            // --- Expenses: shared (doctor-level) ---
            var sharedExpenseRows = await _db.Expenses
                .Where(e => e.DoctorId == doctorId
                    && e.IsShared == true
                    && e.ExpenseDate.Date >= from
                    && e.ExpenseDate.Date <= to)
                .ToListAsync();

            // Apply expense-mode filtering/adjustment (capital handling)
            decimal ExpenseEffectiveAmount(Expense e)
            {
                if (e.ExpenseType != "Capital")
                    return e.Amount;

                if (mode == "operational")
                    return 0m;

                if (mode == "fullcapital")
                    return e.Amount;

                // depreciated: prorate within [from, to] based on monthly depreciation
                var lifeMonths = (e.ExpectedLifeYrs.HasValue && e.ExpectedLifeYrs.Value > 0)
                    ? e.ExpectedLifeYrs.Value * 12m
                    : 12m;
                var monthlyDeprn = e.Amount / lifeMonths;

                var assetStart = e.ExpenseDate.Date;
                var overlapStart = assetStart > from ? assetStart : from;
                if (overlapStart > to) return 0m;

                var totalLifeMonthsCapped = lifeMonths;
                var assetEnd = assetStart.AddMonths((int)Math.Ceiling((double)totalLifeMonthsCapped));
                var overlapEnd = assetEnd < to ? assetEnd : to;
                if (overlapEnd < overlapStart) return 0m;

                var overlapDays = (overlapEnd - overlapStart).Days + 1;
                var dailyDeprn = monthlyDeprn / 30.4375m; // average days per month
                return Math.Round(dailyDeprn * overlapDays, 2);
            }

            // --- Build per-clinic breakdown ---
            var breakdowns = new List<PnLClinicBreakdownDTO>();

            foreach (var clinic in clinics)
            {
                var dto = new PnLClinicBreakdownDTO
                {
                    ClinicId = clinic.Id,
                    ClinicName = clinic.Name
                };

                var clinicSchedules = scheduleRows.Where(s => s.ClinicId == clinic.Id);
                dto.VaccineRevenue = clinicSchedules.Sum(s => s.Amount);
                dto.VaccineCost = clinicSchedules.Sum(s => s.VaccineCost);
                dto.VaccineProfit = dto.VaccineRevenue - dto.VaccineCost;

                dto.ConsultationIncome = invoiceRows.Where(x => x.ClinicId == clinic.Id).Sum(x => x.ConsultationFee);

                var clinicDirectSales = directSaleRows.Where(d => d.ClinicId == clinic.Id);
                dto.DirectSaleRevenue = clinicDirectSales.Sum(d => d.TotalSaleValue);
                dto.DirectSaleCost = clinicDirectSales.Sum(d => d.TotalCostValue);
                dto.DirectSaleProfit = clinicDirectSales.Sum(d => d.Profit);

                dto.TotalIncome = dto.VaccineProfit + dto.ConsultationIncome + dto.DirectSaleProfit;

                var clinicDirectExpenses = directExpenseRows.Where(e => e.ClinicId == clinic.Id).ToList();
                var categoryGroups = clinicDirectExpenses
                    .GroupBy(e => e.Category)
                    .Select(g => new PnLCategoryAmountDTO
                    {
                        Category = g.Key,
                        Amount = g.Sum(e => ExpenseEffectiveAmount(e))
                    })
                    .Where(c => c.Amount != 0)
                    .ToList();
                dto.ExpensesByCategory = categoryGroups;
                dto.DirectExpenses = categoryGroups.Sum(c => c.Amount);

                breakdowns.Add(dto);
            }

            // --- Shared expense allocation by revenue share ---
            var sharedExpensesTotal = sharedExpenseRows.Sum(e => ExpenseEffectiveAmount(e));
            var totalIncomeAllClinics = breakdowns.Sum(b => b.TotalIncome);

            foreach (var dto in breakdowns)
            {
                decimal sharePercent;
                if (totalIncomeAllClinics != 0)
                {
                    sharePercent = dto.TotalIncome / totalIncomeAllClinics;
                }
                else
                {
                    sharePercent = breakdowns.Count > 0 ? 1m / breakdowns.Count : 0m;
                }

                dto.SharedExpenseSharePercent = Math.Round(sharePercent * 100m, 2);
                dto.SharedExpenseShare = Math.Round(sharedExpensesTotal * sharePercent, 2);
                dto.TotalExpenses = dto.DirectExpenses + dto.SharedExpenseShare;
                dto.NetPnL = dto.TotalIncome - dto.TotalExpenses;
            }

            var sharedExpensesByCategory = sharedExpenseRows
                .GroupBy(e => e.Category)
                .Select(g => new PnLCategoryAmountDTO
                {
                    Category = g.Key,
                    Amount = g.Sum(e => ExpenseEffectiveAmount(e))
                })
                .Where(c => c.Amount != 0)
                .ToList();

            var consolidated = new PnLTotalsDTO
            {
                TotalVaccineRevenue = breakdowns.Sum(b => b.VaccineRevenue),
                TotalVaccineCost = breakdowns.Sum(b => b.VaccineCost),
                TotalVaccineProfit = breakdowns.Sum(b => b.VaccineProfit),
                TotalConsultationIncome = breakdowns.Sum(b => b.ConsultationIncome),
                TotalDirectSaleRevenue = breakdowns.Sum(b => b.DirectSaleRevenue),
                TotalDirectSaleCost = breakdowns.Sum(b => b.DirectSaleCost),
                TotalDirectSaleProfit = breakdowns.Sum(b => b.DirectSaleProfit),
                TotalIncome = totalIncomeAllClinics,
                TotalDirectExpenses = breakdowns.Sum(b => b.DirectExpenses),
                SharedExpensesTotal = sharedExpensesTotal,
                SharedExpensesByCategory = sharedExpensesByCategory,
                TotalExpenses = breakdowns.Sum(b => b.DirectExpenses) + sharedExpensesTotal,
                NetPnL = breakdowns.Sum(b => b.NetPnL)
            };

            var response = new PnLResponseDTO
            {
                FromDate = from,
                ToDate = to,
                ExpenseMode = mode,
                Clinics = breakdowns,
                Consolidated = consolidated
            };

            return Ok(new { IsSuccess = true, ResponseData = response });
        }
    }
}
