using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SupplierController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public SupplierController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public ActionResult<Response<IEnumerable<SupplierDTO>>> GetAll()
        {
            var suppliers = _db.Suppliers.OrderBy(s => s.Name).ToList();
            var dtos = _mapper.Map<List<SupplierDTO>>(suppliers);
            return Ok(new Response<IEnumerable<SupplierDTO>>(true, null, dtos));
        }

        [HttpGet("{id}")]
        public ActionResult<Response<SupplierDTO>> GetById(long id)
        {
            var supplier = _db.Suppliers.FirstOrDefault(s => s.Id == id);
            if (supplier == null)
                return NotFound(new Response<SupplierDTO>(false, "Supplier not found", null));
            return Ok(new Response<SupplierDTO>(true, null, _mapper.Map<SupplierDTO>(supplier)));
        }

        [HttpPost]
        public ActionResult<Response<SupplierDTO>> Create([FromBody] SupplierDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new Response<SupplierDTO>(false, "Name is required", null));
            var supplier = _mapper.Map<Supplier>(dto);
            supplier.Id = 0;
            _db.Suppliers.Add(supplier);
            _db.SaveChanges();
            return Ok(new Response<SupplierDTO>(true, "Supplier created", _mapper.Map<SupplierDTO>(supplier)));
        }

        [HttpPut("{id}")]
        public ActionResult<Response<SupplierDTO>> Update(long id, [FromBody] SupplierDTO dto)
        {
            var supplier = _db.Suppliers.FirstOrDefault(s => s.Id == id);
            if (supplier == null)
                return NotFound(new Response<SupplierDTO>(false, "Supplier not found", null));
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new Response<SupplierDTO>(false, "Name is required", null));

            supplier.Name = dto.Name.Trim();
            supplier.ContactPerson = dto.ContactPerson != null ? dto.ContactPerson.Trim() : null;
            supplier.Phone = dto.Phone != null ? dto.Phone.Trim() : null;
            supplier.Email = dto.Email != null ? dto.Email.Trim() : null;
            supplier.Address = dto.Address != null ? dto.Address.Trim() : null;
            supplier.NTN = dto.NTN != null ? dto.NTN.Trim() : null;
            supplier.STRN = dto.STRN != null ? dto.STRN.Trim() : null;
            supplier.OpeningBalance = dto.OpeningBalance;
            supplier.IsActive = dto.IsActive;
            _db.SaveChanges();
            return Ok(new Response<SupplierDTO>(true, "Supplier updated", _mapper.Map<SupplierDTO>(supplier)));
        }
    }
}
