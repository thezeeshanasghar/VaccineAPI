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
    public class DashboardController : ControllerBase
    {
        private readonly Context _db;

        public DashboardController(Context context)
        {
            _db = context;
        }

        [HttpGet("combined-data/{doctorId}")]
        public async Task<IActionResult> GetCombinedDashboardData(int doctorId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;
                var currentDate = DateTime.Now.Date;
                DateTime startOfMonth = new DateTime(currentYear, currentMonth, 1);
                DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

                // Current Month Child Count
                var currentMonthChildCount = await _db.Childs
                    .CountAsync(c => c.DOB.Month == currentMonth
                                    && c.DOB.Year == currentYear
                                    && c.Clinic.DoctorId == doctorId);

                // Total Child Count
                var totalChildCount = await _db.Childs
                    .CountAsync(c => c.Clinic.DoctorId == doctorId);

                // Alerts Count
                var totalAlertsCount = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Date.Date >= startOfMonth.Date && s.Date.Date <= currentDate)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId)
                    .Where(s => s.IsDone != true && s.IsSkip != true && s.Child.IsInactive != true)
                    .Select(s => s.Child.Id)
                    .Distinct()
                    .CountAsync();

                // Future Alerts Count
                var futureAlertsCount = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Date.Date > currentDate && s.Date.Date <= endOfMonth.Date)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId)
                    .Where(s => s.IsDone != true && s.IsSkip != true && s.Child.IsInactive != true)
                    .Select(s => s.Child.Id)
                    .Distinct()
                    .CountAsync();

                // Current Month Given Doses Count
                var givenDosesCount = await _db.Schedules
                    .Include(s => s.Child.Clinic)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId &&
                                s.IsDone == true &&
                                s.GivenDate.HasValue &&
                                s.GivenDate.Value.Month == currentMonth &&
                                s.GivenDate.Value.Year == currentYear)
                    .CountAsync();

                var totalRevenue =await _db.Schedules.Include(s => s.Child.Clinic)
                        .Where(s =>s.Child.Clinic.DoctorId == doctorId
                            && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Month == currentMonth
                            && s.GivenDate.Value.Year == currentYear)
                        .SumAsync(s => s.Amount ?? 0) 
                    + await _db.AdjustStocks.Where(sa => _db.Clinics.Any(c => c.Id == sa.ClinicId && c.DoctorId == doctorId)
                            && sa.Date.Month == currentMonth
                            && sa.Date.Year == currentYear
                            && sa.Adjustment < 0)
                        .SumAsync(sa => sa.Price); 

                var dashboardData = new DashboardDTO
                {
                    CurrentMonthChildCount = currentMonthChildCount,
                    TotalChildCount = totalChildCount,
                    TotalAlertsCount = totalAlertsCount,
                    FutureAlertsCount = futureAlertsCount,
                    GivenDosesCount = givenDosesCount,
                    TotalRevenue = (totalRevenue),
                    // TotalIncreasedStock = totalIncreasedStock,
                };

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the combined dashboard data.");
            }
        }

        // GET /api/dashboard/analytics/{doctorId}
        [HttpGet("analytics/{doctorId}")]
        public async Task<IActionResult> GetAnalytics(int doctorId)
        {
            try
            {
                var now = DateTime.Now;
                var months = Enumerable.Range(0, 6)
                    .Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-5 + i))
                    .ToList();

                var clinicIds = await _db.Clinics
                    .Where(c => c.DoctorId == doctorId)
                    .Select(c => c.Id)
                    .ToListAsync();

                // Monthly revenue: sum of schedule amounts given per month (last 6 months)
                var revenueRaw = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                             && s.IsDone == true
                             && s.GivenDate.HasValue
                             && s.GivenDate.Value >= months.First()
                             && s.GivenDate.Value < months.Last().AddMonths(1))
                    .GroupBy(s => new { s.GivenDate.Value.Year, s.GivenDate.Value.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(s => s.Amount ?? 0) })
                    .ToListAsync();

                // Monthly doses given (last 6 months)
                var dosesRaw = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                             && s.IsDone == true
                             && s.GivenDate.HasValue
                             && s.GivenDate.Value >= months.First()
                             && s.GivenDate.Value < months.Last().AddMonths(1))
                    .GroupBy(s => new { s.GivenDate.Value.Year, s.GivenDate.Value.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                    .ToListAsync();

                // Monthly new patients registered (last 6 months) — using Child.CreatedAt or DOB fallback
                var patientsRaw = await _db.Childs
                    .Where(c => c.Clinic.DoctorId == doctorId
                             && c.DOB >= months.First()
                             && c.DOB < months.Last().AddMonths(1))
                    .GroupBy(c => new { c.DOB.Year, c.DOB.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                    .ToListAsync();

                // Top 10 vaccines given all-time
                var topVaccinesRaw = await _db.Schedules
                    .Include(s => s.Brand)
                    .Include(s => s.Child)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                             && s.IsDone == true
                             && s.BrandId != null)
                    .GroupBy(s => new { s.BrandId, BrandName = s.Brand.Name })
                    .Select(g => new { g.Key.BrandName, Count = g.Count() })
                    .OrderByDescending(g => g.Count)
                    .Take(10)
                    .ToListAsync();

                // Map to month-labelled series, filling zeros for missing months
                var monthlyRevenue = months.Select(m => new MonthlyStatDTO
                {
                    Month = m.ToString("MMM yy"),
                    Value = revenueRaw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Total ?? 0
                }).ToList();

                var monthlyDoses = months.Select(m => new MonthlyStatDTO
                {
                    Month = m.ToString("MMM yy"),
                    Value = dosesRaw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Count ?? 0
                }).ToList();

                var monthlyPatients = months.Select(m => new MonthlyStatDTO
                {
                    Month = m.ToString("MMM yy"),
                    Value = patientsRaw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Count ?? 0
                }).ToList();

                var topVaccines = topVaccinesRaw.Select(v => new BrandStatDTO
                {
                    BrandName = v.BrandName ?? "Unknown",
                    Count = v.Count
                }).ToList();

                return Ok(new AnalyticsDTO
                {
                    MonthlyRevenue = monthlyRevenue,
                    MonthlyDoses = monthlyDoses,
                    MonthlyNewPatients = monthlyPatients,
                    TopVaccines = topVaccines
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analytics error: {ex.Message}");
                return StatusCode(500, "Error fetching analytics data.");
            }
        }
    }
}