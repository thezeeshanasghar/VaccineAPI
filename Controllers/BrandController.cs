using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;

        }

        [HttpGet]
        public async Task<Response<List<BrandDTO>>> GetAll()
        {
            var list = await _db.Brands.OrderBy(x => x.Name).ToListAsync();
            List<BrandDTO> listDTO = _mapper.Map<List<BrandDTO>>(list);

            return new Response<List<BrandDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<BrandDTO>> GetSingle(long id)
        {
            var dbbrand = await _db.Brands.FirstOrDefaultAsync(x => x.Id == id);

            BrandDTO brandDTO = _mapper.Map<BrandDTO>(dbbrand);

            if (dbbrand == null)
                return new Response<BrandDTO>(false, "Not Found", null);

            return new Response<BrandDTO>(true, null, brandDTO);
        }

        // [HttpPost("{vaccineId}")]
        // public Response<BrandDTO> Post(BrandDTO vaccineBrandDTO)
        // {
        //      Brand dbVaccineBrand = _mapper.Map<Brand>(vaccineBrandDTO);
        //             _db.Brands.Add(dbVaccineBrand);
        //             // for each doctor
        //             // _db.BrancdAcmout.add()
        //             _db.SaveChanges();
        //             vaccineBrandDTO.Id = dbVaccineBrand.Id;
        //             return new Response<BrandDTO>(true, null, vaccineBrandDTO);
        // }
        [HttpPost]
        [HttpPost("{vaccineId}")]
        public async Task<Response<BrandDTO>> Post(BrandDTO vaccineBrandDTO, long? vaccineId = null)
        {
            // Map the DTO to the Brand entity
            Brand dbVaccineBrand = _mapper.Map<Brand>(vaccineBrandDTO);

            // Add the new brand to the database
            _db.Brands.Add(dbVaccineBrand);
            await _db.SaveChangesAsync();

            // Retrieve all doctors and their associated clinics
            var doctors = await _db.Doctors.Include(d => d.Clinics).ToListAsync();
            List<BrandAmount> brandAmounts = new List<BrandAmount>();

            // Loop through each doctor and their clinics
            foreach (var doctor in doctors)
            {
                foreach (var clinic in doctor.Clinics)
                {
                    // Create a BrandAmount entry for each clinic
                    BrandAmount newBrandAmount = new BrandAmount
                    {
                        ClinicId = clinic.Id, // Use the clinic's ID
                        DoctorId = doctor.Id, // Use the doctor's ID
                        BrandId = dbVaccineBrand.Id, // Use the newly created brand's ID
                        Amount = 0,
                        Count = 0,
                    };
                    brandAmounts.Add(newBrandAmount);
                }
            }

            // Add all BrandAmount entries to the database
            _db.BrandAmounts.AddRange(brandAmounts);

            // Save changes to the database
            await _db.SaveChangesAsync();

            // Update the DTO with the new brand's ID
            vaccineBrandDTO.Id = dbVaccineBrand.Id;

            return new Response<BrandDTO>(true, null, vaccineBrandDTO);
        }


        [HttpPut("{id}")]
        public Response<BrandDTO> Put(int Id, BrandDTO vaccineBrandDTO)
        {
            var dbVaccineBrand = _db.Brands.Where(c => c.Id == Id).FirstOrDefault();
            if (dbVaccineBrand == null)
            {
                return new Response<BrandDTO>(false, "Brand not found", null);
            }
            dbVaccineBrand.Name = vaccineBrandDTO.Name;
            dbVaccineBrand.Manufacturer = vaccineBrandDTO.Manufacturer;
            dbVaccineBrand.MinAge = vaccineBrandDTO.MinAge;
            _db.SaveChanges();
            return new Response<BrandDTO>(true, null, vaccineBrandDTO);
        }

        [HttpDelete("{id}")]
        public async Task<Response<string>> Delete(long id)
        {
            var dbVaccineBrand = await _db.Brands.Where(c => c.Id == id).FirstOrDefaultAsync();
            if (dbVaccineBrand == null)
            {
                return new Response<string>(false, "Brand not found", null);
            }

            var brandAmounts = await _db.BrandAmounts.Where(ba => ba.BrandId == id).ToListAsync();

            _db.BrandAmounts.RemoveRange(brandAmounts);
            _db.Brands.Remove(dbVaccineBrand);

            await _db.SaveChangesAsync();
            return new Response<string>(true, null, "Brand and related BrandAmounts deleted");
        }

    }
}
