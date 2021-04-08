using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VaccineController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        //   public static string GetMessageFromExceptionObject(Exception ex)
        //         {           
        //             String message = ex.Message;
        //             if(ex is DbEntityValidationException)
        //             {
        //                 var errorMessages = ((DbEntityValidationException)ex).EntityValidationErrors.SelectMany(x => x.ValidationErrors).Select(x => x.ErrorMessage);
        //                 var fullErrorMessage = string.Join("; ", errorMessages);
        //                 return string.Concat(ex.Message, "<br />The validation errors are: ", fullErrorMessage);
        //             }
        //             message += (ex.InnerException != null) ? ("<br />" + ex.InnerException.Message) : "";
        //             message += (ex.InnerException != null && ex.InnerException.InnerException != null) ? ("<br />" + ex.InnerException.InnerException.Message) : "";
        //             return message;
        //         }
        public VaccineController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<VaccineDTO>>> GetAll()
        {
            var list = await _db.Vaccines.Include(x => x.Brands).Include(x => x.Doses).OrderBy(x => x.MinAge).ToListAsync();
            List<VaccineDTO> listDTO = _mapper.Map<List<VaccineDTO>>(list);
            foreach (var dto in listDTO)
            {
                var vaccine = _db.Vaccines.Where(x => x.Id == dto.Id).First();
                if (vaccine.Brands != null)
                    dto.NumOfBrands = vaccine.Brands.Count();
                else
                    dto.NumOfBrands = 0;

                if (vaccine.Doses != null)
                    dto.NumOfDoses = vaccine.Doses.Count();
                else
                    dto.NumOfDoses = 0;
                // dto.NumOfDoses = _db.Vaccines.Where(x => x.Id == dto.Id).First().Doses.Count();
            }
            return new Response<List<VaccineDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<VaccineDTO>> GetSingle(long id)
        {


            var dbvaccine = await _db.Vaccines.Include(X => X.Doses).Include(x => x.Brands).Where(x => x.Id == id).FirstOrDefaultAsync();
            VaccineDTO vaccineDTO = _mapper.Map<VaccineDTO>(dbvaccine);
            vaccineDTO.NumOfBrands = dbvaccine.Brands.Count();
            vaccineDTO.NumOfDoses = dbvaccine.Doses.Count();
            if (dbvaccine == null)
                return new Response<VaccineDTO>(false, "Not Found", null);

            return new Response<VaccineDTO>(true, null, vaccineDTO);
        }

        [HttpGet("{id}/dosses")]
        public async Task<Response<List<DoseDTO>>> GetDosses(long id)
        {


            var dbvaccine = await _db.Vaccines.Include(x => x.Doses).FirstOrDefaultAsync(x => x.Id == id);

            if (dbvaccine == null)
                return new Response<List<DoseDTO>>(false, "Vaccine Not Found", null);

            else
            {
                var dbDosses = dbvaccine.Doses;
                var dossesDTOs = _mapper.Map<List<DoseDTO>>(dbDosses);
                return new Response<List<DoseDTO>>(true, null, dossesDTOs);
            }
        }

        [HttpGet("{id}/brands")]
        public async Task<Response<List<BrandDTO>>> GetBrands(long id)
        {


            var dbvaccine = await _db.Vaccines.Include(x => x.Brands).FirstOrDefaultAsync(x => x.Id == id);

            if (dbvaccine == null)
                return new Response<List<BrandDTO>>(false, "Vaccine Not Found", null);

            else
            {
                var dbBrands = dbvaccine.Brands;
                var brandDTOs = _mapper.Map<List<BrandDTO>>(dbBrands);
                return new Response<List<BrandDTO>>(true, null, brandDTOs);
            }
        }

        [HttpPost]
        public Response<VaccineDTO> Post(VaccineDTO vaccineDTO)

        {
            Vaccine vaccinedb = _mapper.Map<Vaccine>(vaccineDTO);
            _db.Vaccines.Add(vaccinedb);
            _db.SaveChanges();
            vaccineDTO.Id = vaccinedb.Id;
            //add vaccine in brand
            Brand dbBrand = new Brand();
            dbBrand.VaccineId = vaccinedb.Id;
            dbBrand.Name = "Local";
            _db.Brands.Add(dbBrand);
            _db.SaveChanges();

            return new Response<VaccineDTO>(true, null, vaccineDTO);

        }

        [HttpPut("{id}")]
        public Response<VaccineDTO> Put(long id, VaccineDTO vaccineDTO)
        {
            var dbVaccine = _db.Vaccines.Where(x => x.Id == id).FirstOrDefault();
            //VaccineDTO vaccineDTOs = _mapper.Map<VaccineDTO>(dbVaccine);
            //  dbVaccine = Mapper.Map<VaccineDTO, Vaccine>(vaccineDTO, dbVaccine);
            dbVaccine.Name = vaccineDTO.Name;
            dbVaccine.MinAge = vaccineDTO.MinAge;
            dbVaccine.MaxAge = vaccineDTO.MaxAge;
            _db.SaveChanges();
            return new Response<VaccineDTO>(true, null, vaccineDTO);

        }



        [HttpDelete("{id}")]
        public Response<string> Delete(long id)

        {
            //try {
            var dbVaccine = _db.Vaccines.Include(X => X.Doses).Include(x => x.Brands).Where(x => x.Id == id).FirstOrDefault();
            if (dbVaccine.Brands.Count > 0)
                return new Response<string>(false, "Cannot delete vaccine because it's brands exists. Delete the brands first", null);
            else if (dbVaccine.Doses.Count > 0)
                return new Response<string>(false, "Cannot delete vaccine because it's Doses exists. Delete the Doses first", null);
            _db.Vaccines.Remove(dbVaccine);
            _db.SaveChanges();
            return new Response<string>(true, null, "record deleted");
            //    }
            //    catch (Exception ex)
            // {
            // if (ex.InnerException.InnerException.Message.Contains("The DELETE statement conflicted with the REFERENCE constraint"))
            //     return new Response<string>(false, "Cannot delete vaccine because it's doses exists. Delete the doses first.", null);

            // }

        }

        public static string sendRequest(string url)
        {
            System.Net.Http.HttpClient c = new System.Net.Http.HttpClient();
            var content = c.GetStringAsync(url).Result;
            return content.ToString();
        }

    }

}

