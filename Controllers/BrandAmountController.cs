using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandAmountController : ControllerBase
    {
        private readonly Context _db;

        public BrandAmountController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<Response<List<BrandAmountDTO>>> GetByDoctorClinic(
            [FromQuery] long doctorId,
            [FromQuery] long clinicId)
        {
            var result = await _db.BrandAmounts
                .Where(ba => ba.DoctorId == doctorId && ba.ClinicId == clinicId)
                .Join(_db.Brands,
                    ba => ba.BrandId,
                    b => b.Id,
                    (ba, b) => new { ba, b })
                .GroupJoin(_db.VaccineBrands,
                    x => x.b.Id,
                    vb => vb.BrandId,
                    (x, vbs) => new { x.ba, x.b, vbs })
                .SelectMany(
                    x => x.vbs.DefaultIfEmpty(),
                    (x, vb) => new { x.ba, x.b, vb })
                .GroupJoin(_db.Vaccines,
                    x => x.vb != null ? x.vb.VaccineId : (long?)null,
                    v => v.Id,
                    (x, vs) => new { x.ba, x.b, vs })
                .SelectMany(
                    x => x.vs.DefaultIfEmpty(),
                    (x, v) => new BrandAmountDTO
                    {
                        Id = x.ba.Id,
                        BrandId = x.ba.BrandId,
                        BrandName = x.b.Name,
                        VaccineName = v != null ? v.Name : x.b.Name,
                        Amount = x.ba.Amount
                    })
                .OrderBy(d => d.VaccineName)
                .ThenBy(d => d.BrandName)
                .ToListAsync();

            // Remove duplicates — a brand linked to multiple vaccines produces multiple rows; keep one per brand
            var seen = new HashSet<long>();
            result = result.Where(r => seen.Add(r.BrandId)).ToList();

            return new Response<List<BrandAmountDTO>>(true, null, result);
        }

        [HttpPut]
        public async Task<Response<string>> UpdateAmounts([FromBody] List<BrandAmountDTO> dtos)
        {
            foreach (var dto in dtos)
            {
                var ba = await _db.BrandAmounts.FindAsync(dto.Id);
                if (ba != null)
                {
                    ba.Amount = dto.Amount;
                }
            }
            await _db.SaveChangesAsync();
            return new Response<string>(true, null, "Prices updated");
        }
    }
}
