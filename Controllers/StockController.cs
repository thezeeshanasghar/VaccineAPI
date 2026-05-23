using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockController : ControllerBase
    {
        private readonly Context _db;
        public StockController(Context db) { _db = db; }

        // GET /api/stock/batch-lots?brandId=X&clinicId=Y
        // Returns all in-stock lots for a brand at a clinic, sorted by expiry ascending (FEFO)
        [HttpGet("batch-lots")]
        public async Task<IActionResult> GetBatchLots([FromQuery] long brandId, [FromQuery] long clinicId)
        {
            var stocks = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Bill.ClinicId == clinicId && s.Quantity > 0)
                .OrderBy(s => s.Expiry)
                .Select(s => new
                {
                    s.BatchLot,
                    s.Expiry,
                    s.Quantity,
                    s.BrandId
                })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = stocks });
        }
    }
}
