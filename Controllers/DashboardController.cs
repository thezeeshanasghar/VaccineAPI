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

        // GET: api/dashboard
        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;
                var currentMonthChildCount = await _db.Childs
                    .CountAsync(c => c.DOB.Month == currentMonth && c.DOB.Year == currentYear);
                var dashboardData = new DashboardDTO
                {
                    CurrentMonthChildCount = currentMonthChildCount
                };
                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the dashboard data.");
            }
        }

        [HttpGet("doctor/{doctorId}")]
        public async Task<IActionResult> GetDoctorTotalChildCount(int doctorId)
        {
            try
            {
                var totalChildCount = await _db.Childs
                    .CountAsync(c => c.Clinic.DoctorId == doctorId);
                var dashboardData = new DashboardDTO
                {
                    TotalChildCount = totalChildCount
                };

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the dashboard data.");
            }
        }

        [HttpGet("alerts")]
        public async Task<IActionResult> GetDoctorAlerts([FromQuery] int doctorId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;
                var currentDate = DateTime.Now.Date;
                DateTime startOfMonth = new DateTime(currentYear, currentMonth, 1);
                var totalAlertsCount = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Date.Date >= startOfMonth.Date && s.Date.Date <= currentDate) // Filter for current month up to today
                    .Where(s => s.Child.Clinic.DoctorId == doctorId)
                    .Where(s => s.IsDone != true && s.IsSkip != true && s.Child.IsInactive != true)
                    .Select(s => s.Child.Id)
                    .Distinct()
                    .CountAsync();

                return Ok(new { TotalAlertsCount = totalAlertsCount });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the alerts data.");
            }
        }

        [HttpGet("future-alerts")]
        public async Task<IActionResult> GetDoctorFutureAlerts([FromQuery] int doctorId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;
                var currentDate = DateTime.Now.Date;
                DateTime startOfMonth = new DateTime(currentYear, currentMonth, 1);
                DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
                var futureAlertsCount = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Date.Date > currentDate && s.Date.Date <= endOfMonth.Date)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId)
                    .Where(s => s.IsDone != true && s.IsSkip != true && s.Child.IsInactive != true)
                    .Select(s => s.Child.Id)
                    .Distinct()
                    .CountAsync();
                return Ok(new { FutureAlertsCount = futureAlertsCount });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the future alerts data.");
            }
        }

        [HttpGet("current-month-given-doses/{doctorId}")]
        public async Task<IActionResult> GetCurrentMonthGivenDosesCount(long doctorId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;

                // Count the doses marked as done in the current month for a specific doctor's patients
                var givenDosesCount = await _db.Schedules
                    .Include(s => s.Child.Clinic)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId &&
                                s.IsDone == true &&
                                s.GivenDate.HasValue &&
                                s.GivenDate.Value.Month == currentMonth &&
                                s.GivenDate.Value.Year == currentYear)
                    .CountAsync();

                return Ok(new { GivenDosesCount = givenDosesCount });
            }
            catch (Exception ex)
            {
                // Log the exception and return a 500 response
                Console.WriteLine($"Error in GetCurrentMonthGivenDosesCount: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the given doses count for the current month.");
            }
        }

        [HttpGet("current-month-revenue/{doctorId}")]
        public async Task<IActionResult> GetCurrentMonthRevenue(long doctorId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;
                var totalRevenue = await _db.Schedules
                    .Include(s => s.Child.Clinic)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId &&
                                s.IsDone == true &&
                                s.GivenDate.HasValue &&
                                s.GivenDate.Value.Month == currentMonth &&
                                s.GivenDate.Value.Year == currentYear)
                    .SumAsync(s => s.Amount ?? 0);

                return Ok(new { TotalRevenue = totalRevenue });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCurrentMonthRevenue: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the revenue for the current month.");
            }
        }
    }
}
