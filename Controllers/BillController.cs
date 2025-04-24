using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BillController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public Response<List<BillDTO>> Get()
        {
            var bills = _db.Bills.ToList();
            if (!bills.Any())
                return new Response<List<BillDTO>>(false, "No bills found", null);

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        [HttpGet("doctor/{doctorId}")]  // Changed route to avoid conflict
        public Response<List<BillDTO>> GetByDoctor(long doctorId)
        {
            var bills = _db.Bills
                .Include(b => b.Doctor)
                    .ThenInclude(d => d.User)
                .Include(b => b.Stocks)
                    .ThenInclude(s => s.Brand)
                .Where(b => b.DoctorId == doctorId)
                .ToList();

            if (!bills.Any())
                return new Response<List<BillDTO>>(false, $"No bills found for doctor ID {doctorId}", null);

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        // [HttpGet("{id:int}")]  // Added constraint to differentiate from doctorId
        // public Response<BillDTO> Getbyid(int id)
        // {
        //     var bill = _db.Bills
        //         .Include(b => b.Doctor)
        //             .ThenInclude(d => d.User)
        //         .FirstOrDefault(b => b.Id == id);

        //     if (bill == null)
        //         return new Response<BillDTO>(false, "Bill not found", null);

        //     var billDTO = _mapper.Map<BillDTO>(bill);
        //     return new Response<BillDTO>(true, null, billDTO);
        // }

        [HttpGet("{id}")]
        public Response<BillDTO> Getbyid(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            var billDTO = _mapper.Map<BillDTO>(bill);
            return new Response<BillDTO>(true, null, billDTO);
        }

        // [HttpGet("suppliers")]
        // public Response<List<SupplierDTO>> GetSuppliers()
        // {
        //     try
        //     {
        //         var suppliers = _db.Bills
        //             .Where(b => !string.IsNullOrEmpty(b.Supplier))
        //             .Select(b => new SupplierDTO { Name = b.Supplier })
        //             .Distinct()
        //             .OrderBy(s => s.Name)
        //             .ToList();

        //         if (!suppliers.Any())
        //         {
        //             return new Response<List<SupplierDTO>>(false, "No suppliers found", null);
        //         }
                
        //         return new Response<List<SupplierDTO>>(true, null, suppliers);
        //     }
        //     catch (Exception ex)
        //     {
        //         return new Response<List<SupplierDTO>>(
        //             false,
        //             $"Error retrieving suppliers: {ex.Message}",
        //             null
        //         );
        //     }
        // }

        [HttpPost]
        public Response<BillDTO> Post(BillDTO billDTO)
        {
            var bill = _mapper.Map<Bill>(billDTO);
            _db.Bills.Add(bill);
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill created successfully", billDTO);
        }

        [HttpPut("{id}")]
        public Response<BillDTO> Put(int id, BillDTO billDTO)
        {
            if (id != billDTO.Id)
                return new Response<BillDTO>(false, "ID mismatch", null);

            var bill = _mapper.Map<Bill>(billDTO);
            _db.Entry(bill).State = EntityState.Modified;
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill updated successfully", billDTO);
        }

        [HttpDelete("{id}")]
        public Response<BillDTO> Delete(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            _db.Bills.Remove(bill);
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill deleted successfully", null);
        }

        [HttpGet("Suppliers")]
        public Response<IEnumerable<string>> GetSupplierNames()
        {
            try
            {
                // Fetch distinct agent names where Agent is not null/empty and matches the given DoctorId
                var supplierNames = _db.Bills
                    .Where(c => !string.IsNullOrEmpty(c.Supplier) )
                    .Select(c => c.Supplier)
                    .Distinct()
                    .ToList();

                if (!supplierNames.Any())
                {
                    return new Response<IEnumerable<string>>(false, "No suppliers found for the specified doctor", null);
                }

                return new Response<IEnumerable<string>>(true, "suppliers retrieved successfully", supplierNames);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving suppliers: {ex.Message}");
                return new Response<IEnumerable<string>>(false, "An error occurred while retrieving suppliers", null);
            }
        }
    }
}