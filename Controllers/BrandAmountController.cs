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
            // Ensure every brand has a BrandAmount row for this doctor+clinic
            var allBrands = await _db.Brands.ToListAsync();
            var existingBas = await _db.BrandAmounts
                .Where(ba => ba.DoctorId == doctorId && ba.ClinicId == clinicId)
                .ToListAsync();
            var existingBrandIds = new HashSet<long>(existingBas.Select(ba => ba.BrandId));

            var toAdd = allBrands
                .Where(b => !existingBrandIds.Contains(b.Id))
                .Select(b => new BrandAmount
                {
                    BrandId = b.Id,
                    DoctorId = doctorId,
                    ClinicId = clinicId,
                    Amount = 0,
                    Count = 0,
                    PurchasedAmt = 0
                })
                .ToList();

            if (toAdd.Count > 0)
            {
                _db.BrandAmounts.AddRange(toAdd);
                await _db.SaveChangesAsync();
                existingBas.AddRange(toAdd);
            }

            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .ToListAsync();

            var result = existingBas
                .Join(allBrands,
                    ba => ba.BrandId,
                    b => b.Id,
                    (ba, b) => new BrandAmountDTO
                    {
                        Id = ba.Id,
                        BrandId = ba.BrandId,
                        BrandName = b.Name,
                        VaccineName = "",
                        Amount = ba.Amount
                    })
                .OrderBy(d => d.BrandName)
                .ToList();

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
