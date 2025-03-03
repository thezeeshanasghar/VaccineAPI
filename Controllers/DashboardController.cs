using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
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

                // Current Month Revenue
                var totalRevenue = await _db.Schedules
                    .Include(s => s.Child.Clinic)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId &&
                                s.IsDone == true &&
                                s.GivenDate.HasValue &&
                                s.GivenDate.Value.Month == currentMonth &&
                                s.GivenDate.Value.Year == currentYear)
                    .SumAsync(s => s.Amount ?? 0);

                var dashboardData = new DashboardDTO
                {
                    CurrentMonthChildCount = currentMonthChildCount,
                    TotalChildCount = totalChildCount,
                    TotalAlertsCount = totalAlertsCount,
                    FutureAlertsCount = futureAlertsCount,
                    GivenDosesCount = givenDosesCount,
                    TotalRevenue = totalRevenue
                };

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the combined dashboard data.");
            }
        }
    }
}