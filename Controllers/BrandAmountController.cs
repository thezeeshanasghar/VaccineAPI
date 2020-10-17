using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandAmountController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandAmountController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("{Id}")]
      public Response<List<BrandAmountDTO>> Get(int Id)
       {
                    List<BrandAmount> brandAmountDBs = _db.BrandAmounts.Include("Brand").Include("Doctor").Where(x => x.DoctorId == Id).ToList();
                    if (brandAmountDBs == null || brandAmountDBs.Count() == 0)
                        return new Response<List<BrandAmountDTO>>(false, "Brand not found", null);
                    List<BrandAmountDTO> brandAmountDTOs = _mapper.Map<List<BrandAmountDTO>>(brandAmountDBs);
                    foreach (BrandAmountDTO baDTO in brandAmountDTOs)
                        baDTO.VaccineName = _db.Brands.Include(x=>x.Vaccine).Where(x => x.Id == baDTO.BrandId).First().Vaccine.Name;
                    return new Response<List<BrandAmountDTO>>(true, null, brandAmountDTOs);
                }


        // [HttpPost]
        // public async Task<ActionResult<BrandAmount>> Post(BrandAmount BrandAmount)
        // {
        //     _db.BrandAmounts.Update(BrandAmount);
        //     await _db.SaveChangesAsync();

        //     return CreatedAtAction(nameof(GetSingle), new { id = BrandAmount.Id }, BrandAmount);
        // }

        [HttpPut]
       public Response<List<BrandAmountDTO>> Put([FromBody] List<BrandAmountDTO> brandAmountDTOs)
       
            {
                    foreach (var brandAmountDTO in brandAmountDTOs)
                    {
                        var brandAmoundDB = _db.BrandAmounts.Where(b => b.Id == brandAmountDTO.Id).FirstOrDefault();
                        brandAmoundDB.Amount = brandAmountDTO.Amount;
                        _db.SaveChanges();
                    }
                    return new Response<List<BrandAmountDTO>>(true, null, brandAmountDTOs);
                }
       

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.BrandAmounts.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.BrandAmounts.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
