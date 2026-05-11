using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExpenseCategoryController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ExpenseCategoryController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("doctor/{doctorId}")]
        public ActionResult<Response<IEnumerable<ExpenseCategoryDTO>>> GetByDoctor(long doctorId)
        {
            var cats = _db.ExpenseCategories
                .Where(c => c.DoctorId == doctorId && c.IsActive)
                .OrderBy(c => c.Name)
                .ToList();
            return Ok(new Response<IEnumerable<ExpenseCategoryDTO>>(true, null, _mapper.Map<List<ExpenseCategoryDTO>>(cats)));
        }

        [HttpPost]
        public ActionResult<Response<ExpenseCategoryDTO>> Create([FromBody] ExpenseCategoryDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new Response<ExpenseCategoryDTO>(false, "Name is required", null));
            var cat = new ExpenseCategory
            {
                DoctorId = dto.DoctorId,
                Name = dto.Name.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.ExpenseCategories.Add(cat);
            _db.SaveChanges();
            return Ok(new Response<ExpenseCategoryDTO>(true, "Category created", _mapper.Map<ExpenseCategoryDTO>(cat)));
        }

        [HttpDelete("{id}")]
        public ActionResult<Response<object>> Delete(long id)
        {
            var cat = _db.ExpenseCategories.FirstOrDefault(c => c.Id == id);
            if (cat == null)
                return NotFound(new Response<object>(false, "Not found", null));
            cat.IsActive = false;
            _db.SaveChanges();
            return Ok(new Response<object>(true, "Deleted", null));
        }
    }
}
