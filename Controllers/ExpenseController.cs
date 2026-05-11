using System;
using System.IO;
using System.Net.Http.Headers;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExpenseController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ExpenseController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("doctor/{doctorId}")]
        public ActionResult<Response<IEnumerable<ExpenseDTO>>> GetByDoctor(
            long doctorId,
            [FromQuery] long? clinicId,
            [FromQuery] long? categoryId,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var q = _db.Expenses
                .Include(e => e.Clinic)
                .Include(e => e.Category)
                .Where(e => e.DoctorId == doctorId);

            if (clinicId.HasValue)   q = q.Where(e => e.ClinicId == clinicId.Value);
            if (categoryId.HasValue) q = q.Where(e => e.CategoryId == categoryId.Value);
            if (fromDate.HasValue)   q = q.Where(e => e.ExpenseDate >= fromDate.Value.Date);
            if (toDate.HasValue)     q = q.Where(e => e.ExpenseDate <= toDate.Value.Date);

            var list = q.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id).ToList();
            return Ok(new Response<IEnumerable<ExpenseDTO>>(true, null, _mapper.Map<List<ExpenseDTO>>(list)));
        }

        [HttpGet("{id}")]
        public ActionResult<Response<ExpenseDTO>> GetById(long id)
        {
            var e = _db.Expenses
                .Include(x => x.Clinic)
                .Include(x => x.Category)
                .FirstOrDefault(x => x.Id == id);
            if (e == null)
                return NotFound(new Response<ExpenseDTO>(false, "Not found", null));
            return Ok(new Response<ExpenseDTO>(true, null, _mapper.Map<ExpenseDTO>(e)));
        }

        [HttpPost]
        public ActionResult<Response<ExpenseDTO>> Create([FromBody] ExpenseDTO dto)
        {
            if (dto.Amount <= 0)
                return BadRequest(new Response<ExpenseDTO>(false, "Amount must be greater than zero", null));
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequest(new Response<ExpenseDTO>(false, "Description is required", null));

            var expense = _mapper.Map<Expense>(dto);
            expense.Id = 0;
            expense.CreatedAt = DateTime.UtcNow;
            _db.Expenses.Add(expense);
            _db.SaveChanges();

            var saved = _db.Expenses
                .Include(x => x.Clinic)
                .Include(x => x.Category)
                .FirstOrDefault(x => x.Id == expense.Id);
            return Ok(new Response<ExpenseDTO>(true, "Expense saved", _mapper.Map<ExpenseDTO>(saved)));
        }

        [HttpPut("{id}")]
        public ActionResult<Response<ExpenseDTO>> Update(long id, [FromBody] ExpenseDTO dto)
        {
            var expense = _db.Expenses.FirstOrDefault(x => x.Id == id);
            if (expense == null)
                return NotFound(new Response<ExpenseDTO>(false, "Not found", null));

            expense.ClinicId = dto.ClinicId;
            expense.CategoryId = dto.CategoryId;
            expense.ExpenseDate = dto.ExpenseDate;
            expense.Amount = dto.Amount;
            expense.Description = dto.Description;
            expense.PaymentMode = dto.PaymentMode;
            expense.ExpenseType = dto.ExpenseType;
            expense.Notes = dto.Notes;
            if (!string.IsNullOrEmpty(dto.ReceiptPath))
                expense.ReceiptPath = dto.ReceiptPath;
            expense.UpdatedAt = DateTime.UtcNow;
            _db.SaveChanges();

            var saved = _db.Expenses
                .Include(x => x.Clinic)
                .Include(x => x.Category)
                .FirstOrDefault(x => x.Id == id);
            return Ok(new Response<ExpenseDTO>(true, "Updated", _mapper.Map<ExpenseDTO>(saved)));
        }

        [HttpDelete("{id}")]
        public ActionResult<Response<object>> Delete(long id)
        {
            var expense = _db.Expenses.FirstOrDefault(x => x.Id == id);
            if (expense == null)
                return NotFound(new Response<object>(false, "Not found", null));
            _db.Expenses.Remove(expense);
            _db.SaveChanges();
            return Ok(new Response<object>(true, "Deleted", null));
        }

        [HttpPost("upload-receipt")]
        [DisableRequestSizeLimit]
        public IActionResult UploadReceipt()
        {
            try
            {
                if (Request.Form?.Files == null || Request.Form.Files.Count == 0)
                    return BadRequest("No file uploaded.");

                var file = Request.Form.Files[0];
                var folderName = Path.Combine("Resources", "Expenses", "receipts");
                var pathToSave = Path.Combine(Directory.GetCurrentDirectory(), folderName);
                Directory.CreateDirectory(pathToSave);

                if (file.Length == 0) return BadRequest("Empty file.");

                var ext = Path.GetExtension(file.FileName);
                var fileName = $"{Guid.NewGuid()}{ext}";
                var fullPath = Path.Combine(pathToSave, fileName);
                var dbPath = Path.Combine(folderName, fileName).Replace("\\", "/");

                using (var stream = new FileStream(fullPath, FileMode.Create))
                    file.CopyTo(stream);

                return Ok(new { dbPath });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Upload failed: " + ex.Message);
            }
        }
    }
}
