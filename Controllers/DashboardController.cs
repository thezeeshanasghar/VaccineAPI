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
        // Log the error (use a proper logging mechanism in production)
        Console.WriteLine($"Error: {ex.Message}");
        return StatusCode(500, "An error occurred while fetching the dashboard data.");
    }
}

    }
}
