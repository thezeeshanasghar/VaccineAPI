using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SupplierController : ControllerBase
    {
        private readonly Context _db;

        public SupplierController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<Response<List<SupplierDTO>>> GetAll([FromQuery] long doctorId)
        {
            var suppliers = await _db.Suppliers
                .Where(s => s.DoctorId == doctorId)
                .OrderBy(s => s.Name)
                .Select(s => new SupplierDTO
                {
                    Id = s.Id,
                    DoctorId = s.DoctorId,
                    Name = s.Name,
                    ContactPerson = s.ContactPerson,
                    Phone = s.Phone,
                    Email = s.Email,
                    Address = s.Address,
                    BankAccount = s.BankAccount,
                    OpeningBalance = s.OpeningBalance,
                    IsActive = s.IsActive
                })
                .ToListAsync();

            return new Response<List<SupplierDTO>>(true, null, suppliers);
        }

        [HttpGet("{id}")]
        public async Task<Response<SupplierDTO>> GetById(long id)
        {
            var s = await _db.Suppliers.FindAsync(id);
            if (s == null)
                return new Response<SupplierDTO>(false, "Not found", null);

            return new Response<SupplierDTO>(true, null, new SupplierDTO
            {
                Id = s.Id,
                DoctorId = s.DoctorId,
                Name = s.Name,
                ContactPerson = s.ContactPerson,
                Phone = s.Phone,
                Email = s.Email,
                Address = s.Address,
                BankAccount = s.BankAccount,
                OpeningBalance = s.OpeningBalance,
                IsActive = s.IsActive
            });
        }

        [HttpPost]
        public async Task<Response<SupplierDTO>> Create([FromBody] SupplierDTO dto)
        {
            var supplier = new Supplier
            {
                DoctorId = dto.DoctorId,
                Name = dto.Name,
                ContactPerson = dto.ContactPerson,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address,
                BankAccount = dto.BankAccount,
                OpeningBalance = dto.OpeningBalance,
                IsActive = dto.IsActive
            };

            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync();
            dto.Id = supplier.Id;
            return new Response<SupplierDTO>(true, null, dto);
        }

        [HttpPut("{id}")]
        public async Task<Response<SupplierDTO>> Update(long id, [FromBody] SupplierDTO dto)
        {
            var supplier = await _db.Suppliers.FindAsync(id);
            if (supplier == null)
                return new Response<SupplierDTO>(false, "Not found", null);

            supplier.Name = dto.Name;
            supplier.ContactPerson = dto.ContactPerson;
            supplier.Phone = dto.Phone;
            supplier.Email = dto.Email;
            supplier.Address = dto.Address;
            supplier.BankAccount = dto.BankAccount;
            supplier.OpeningBalance = dto.OpeningBalance;
            supplier.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            dto.Id = supplier.Id;
            return new Response<SupplierDTO>(true, null, dto);
        }
    }
}
