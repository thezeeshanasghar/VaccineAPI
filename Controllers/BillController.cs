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

        [HttpGet("{id}")]
        public Response<BillDTO> Get(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            var billDTO = _mapper.Map<BillDTO>(bill);
            return new Response<BillDTO>(true, null, billDTO);
        }

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
    }
}