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
    public class BrandInventoryController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandInventoryController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
       public Response<List<BrandInventoryDTO>> GetTest(int Id)
        {
            List<BrandInventory> vaccineInventoryDBs = _db.BrandInventorys.Include("Brand").Include("Doctor").Where(x => x.DoctorId == Id).ToList();
                    if (vaccineInventoryDBs == null || vaccineInventoryDBs.Count() == 0)
                        return new Response<List<BrandInventoryDTO>>(false, "brand inventory not found", null);

                    List<BrandInventoryDTO> VaccineInventoryDTOs = _mapper.Map<List<BrandInventoryDTO>>(vaccineInventoryDBs);
                    foreach (BrandInventoryDTO brandInventoryDTO in VaccineInventoryDTOs)
                        brandInventoryDTO.VaccineName = _db.Brands.Where(x => x.Id == brandInventoryDTO.BrandId).FirstOrDefault().Vaccine.Name;
                    return new Response<List<BrandInventoryDTO>>(true, null, VaccineInventoryDTOs);
           
        }

        [HttpGet("{Id}")]
       public Response<List<BrandInventoryDTO>> Get(int Id)
        {
            List<BrandInventory> vaccineInventoryDBs = _db.BrandInventorys.Include("Brand").Include("Doctor").Where(x => x.DoctorId == Id).ToList();
                    if (vaccineInventoryDBs == null || vaccineInventoryDBs.Count() == 0)
                        return new Response<List<BrandInventoryDTO>>(false, "brand inventory not found", null);

                    List<BrandInventoryDTO> VaccineInventoryDTOs = _mapper.Map<List<BrandInventoryDTO>>(vaccineInventoryDBs);
                    foreach (BrandInventoryDTO brandInventoryDTO in VaccineInventoryDTOs)
                        brandInventoryDTO.VaccineName = _db.Brands.Include(x=>x.Vaccine).Where(x => x.Id == brandInventoryDTO.BrandId).FirstOrDefault().Vaccine.Name;
                    return new Response<List<BrandInventoryDTO>>(true, null, VaccineInventoryDTOs);
           
        }

        // [HttpPost]
        // public async Task<ActionResult<BrandInventory>> Post(BrandInventory BrandInventory)
        // {
        //     _db.BrandInventorys.Update(BrandInventory);
        //     await _db.SaveChangesAsync();

        //     return CreatedAtAction(nameof(GetSingle), new { id = BrandInventory.Id }, BrandInventory);
        // }

        [HttpPut("{id}")]
       public Response<List<BrandInventoryDTO>> Put([FromBody] List<BrandInventoryDTO> vaccineInventoryDTOs)
        {
             foreach (var vaccineInventoryDTO in vaccineInventoryDTOs)
                    {
                        var vaccineInventoryDB = _db.BrandInventorys.Where(c => c.Id == vaccineInventoryDTO.Id).FirstOrDefault();
                        vaccineInventoryDB.Count = vaccineInventoryDTO.Count;
                        _db.SaveChanges();
                    }


                    return new Response<List<BrandInventoryDTO>>(true, null, vaccineInventoryDTOs);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.BrandInventorys.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.BrandInventorys.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
