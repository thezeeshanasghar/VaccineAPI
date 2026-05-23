using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockOverviewController : ControllerBase
    {
        private readonly Context _db;
        public StockOverviewController(Context db) { _db = db; }

        // GET /api/stockoverview?doctorId=X&clinicId=Y
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            // Load all BrandAmounts for this doctor+clinic
            var brandAmounts = await _db.BrandAmounts
                .Include(ba => ba.Brand)
                .Where(ba => ba.DoctorId == doctorId && ba.ClinicId == clinicId)
                .ToListAsync();

            var brandIds = brandAmounts.Select(ba => ba.BrandId).ToList();

            // Load VaccineBrand join for vaccine names
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            // Load all stock rows for these brands at this clinic
            // Stock is clinic-scoped via Bill.ClinicId
            var stockRows = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => brandIds.Contains(s.BrandId) && s.Bill.ClinicId == clinicId && s.Quantity > 0)
                .OrderBy(s => s.Expiry == null ? 1 : 0)
                .ThenBy(s => s.Expiry)
                .ThenBy(s => s.Id)
                .ToListAsync();

            var result = brandAmounts
                .OrderBy(ba =>
                {
                    var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == ba.BrandId);
                    return vb != null && vb.Vaccine != null ? vb.Vaccine.Name : ba.Brand.Name;
                })
                .Select(ba =>
                {
                    var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == ba.BrandId);
                    var batches = stockRows
                        .Where(s => s.BrandId == ba.BrandId)
                        .Select(s => new
                        {
                            s.Id,
                            s.BatchLot,
                            Expiry = s.Expiry.HasValue ? s.Expiry.Value.ToString("yyyy-MM-dd") : null,
                            s.Quantity,
                            UnitPrice = s.StockAmount,
                            LineTotal = s.Quantity * s.StockAmount
                        })
                        .ToList();

                    return new
                    {
                        BrandId = ba.BrandId,
                        BrandName = ba.Brand != null ? ba.Brand.Name : "",
                        VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                        TotalCount = ba.Count,
                        SalePrice = ba.Amount,
                        Batches = batches
                    };
                })
                .Where(x => x.TotalCount > 0 || x.Batches.Count > 0)
                .ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }
    }
}
