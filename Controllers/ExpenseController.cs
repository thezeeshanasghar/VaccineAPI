using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExpenseController : ControllerBase
    {
        private readonly Context _db;
        private readonly IWebHostEnvironment _host;

        public ExpenseController(Context db, IWebHostEnvironment host)
        {
            _db = db;
            _host = host;
        }

        // GET /api/expense?doctorId=X
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] long doctorId)
        {
            var expenses = await _db.Expenses
                .Where(e => e.DoctorId == doctorId)
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.Id)
                .Select(e => new ExpenseResponseDTO
                {
                    Id              = e.Id,
                    DoctorId        = e.DoctorId,
                    ClinicId        = e.ClinicId,
                    IsShared        = e.IsShared,
                    ExpenseDate     = e.ExpenseDate,
                    Amount          = e.Amount,
                    Description     = e.Description,
                    Category        = e.Category,
                    ExpenseType     = e.ExpenseType,
                    PaymentMode     = e.PaymentMode,
                    Notes           = e.Notes,
                    AssetName       = e.AssetName,
                    ExpectedLifeYrs = e.ExpectedLifeYrs,
                    WarrantyExpiry  = e.WarrantyExpiry,
                    ReceiptImagePath  = e.ReceiptImagePath,
                    WarrantyImagePath = e.WarrantyImagePath,
                    CreatedAt       = e.CreatedAt
                })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = expenses });
        }

        // GET /api/expense/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var e = await _db.Expenses.FindAsync(id);
            if (e == null)
                return Ok(new { IsSuccess = false, Message = "Expense not found" });

            return Ok(new { IsSuccess = true, ResponseData = ToDto(e) });
        }

        // POST /api/expense
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ExpenseCreateDTO dto)
        {
            if (dto.Amount <= 0)
                return Ok(new { IsSuccess = false, Message = "Amount must be greater than 0" });
            if (string.IsNullOrWhiteSpace(dto.Description))
                return Ok(new { IsSuccess = false, Message = "Description is required" });
            if ((dto.ExpenseType ?? "Recurring") == "Recurring" && string.IsNullOrWhiteSpace(dto.Category))
                return Ok(new { IsSuccess = false, Message = "Category is required" });

            var expense = new Expense
            {
                DoctorId    = dto.DoctorId,
                ClinicId    = dto.ClinicId,
                IsShared    = dto.IsShared,
                ExpenseDate = dto.ExpenseDate == default ? DateTime.Today : dto.ExpenseDate,
                Amount      = dto.Amount,
                Description = dto.Description.Trim(),
                Category    = (dto.ExpenseType == "Capital") ? "Capital" : (dto.Category ?? ""),
                ExpenseType = dto.ExpenseType ?? "Recurring",
                PaymentMode = dto.PaymentMode ?? "Cash",
                Notes       = dto.Notes?.Trim(),
                CreatedAt   = DateTime.UtcNow
            };

            if (expense.ExpenseType == "Capital")
            {
                expense.AssetName       = dto.AssetName?.Trim();
                expense.ExpectedLifeYrs = dto.ExpectedLifeYrs;
                expense.WarrantyExpiry  = dto.WarrantyExpiry;
            }

            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync();

            // Save images after we have the Id
            if (expense.ExpenseType == "Capital")
            {
                var receiptsFolder  = Path.Combine(_host.ContentRootPath, "Resources", "Expenses", "receipts");
                var warrantiesFolder = Path.Combine(_host.ContentRootPath, "Resources", "Expenses", "warranties");
                Directory.CreateDirectory(receiptsFolder);
                Directory.CreateDirectory(warrantiesFolder);

                if (!string.IsNullOrWhiteSpace(dto.ReceiptImage))
                {
                    expense.ReceiptImagePath = SaveBase64Image(dto.ReceiptImage, receiptsFolder, $"receipt_{expense.Id}");
                }

                if (!string.IsNullOrWhiteSpace(dto.WarrantyImage))
                {
                    expense.WarrantyImagePath = SaveBase64Image(dto.WarrantyImage, warrantiesFolder, $"warranty_{expense.Id}");
                }

                if (expense.ReceiptImagePath != null || expense.WarrantyImagePath != null)
                {
                    _db.Entry(expense).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }

            return Ok(new { IsSuccess = true, Message = "Expense saved", ResponseData = ToDto(expense) });
        }

        // PUT /api/expense/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, [FromBody] ExpenseCreateDTO dto)
        {
            var expense = await _db.Expenses.FindAsync(id);
            if (expense == null)
                return Ok(new { IsSuccess = false, Message = "Expense not found" });

            expense.ClinicId    = dto.ClinicId;
            expense.IsShared    = dto.IsShared;
            expense.ExpenseDate = dto.ExpenseDate == default ? expense.ExpenseDate : dto.ExpenseDate;
            expense.Amount      = dto.Amount;
            expense.Description = dto.Description?.Trim() ?? expense.Description;
            expense.Category    = dto.Category ?? expense.Category;
            expense.ExpenseType = dto.ExpenseType ?? expense.ExpenseType;
            expense.PaymentMode = dto.PaymentMode ?? expense.PaymentMode;
            expense.Notes       = dto.Notes?.Trim();

            if (expense.ExpenseType == "Capital")
            {
                expense.AssetName       = dto.AssetName?.Trim();
                expense.ExpectedLifeYrs = dto.ExpectedLifeYrs;
                expense.WarrantyExpiry  = dto.WarrantyExpiry;

                var receiptsFolder   = Path.Combine(_host.ContentRootPath, "Resources", "Expenses", "receipts");
                var warrantiesFolder = Path.Combine(_host.ContentRootPath, "Resources", "Expenses", "warranties");
                Directory.CreateDirectory(receiptsFolder);
                Directory.CreateDirectory(warrantiesFolder);

                if (!string.IsNullOrWhiteSpace(dto.ReceiptImage))
                    expense.ReceiptImagePath = SaveBase64Image(dto.ReceiptImage, receiptsFolder, $"receipt_{expense.Id}");

                if (!string.IsNullOrWhiteSpace(dto.WarrantyImage))
                    expense.WarrantyImagePath = SaveBase64Image(dto.WarrantyImage, warrantiesFolder, $"warranty_{expense.Id}");
            }

            _db.Entry(expense).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return Ok(new { IsSuccess = true, Message = "Expense updated", ResponseData = ToDto(expense) });
        }

        // DELETE /api/expense/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var expense = await _db.Expenses.FindAsync(id);
            if (expense == null)
                return Ok(new { IsSuccess = false, Message = "Expense not found" });

            _db.Expenses.Remove(expense);
            await _db.SaveChangesAsync();

            return Ok(new { IsSuccess = true, Message = "Expense deleted" });
        }

        private static string? SaveBase64Image(string dataUrl, string folder, string fileNameWithoutExt)
        {
            try
            {
                // Strip "data:image/jpeg;base64," prefix
                var base64 = dataUrl.Contains(',') ? dataUrl.Split(',')[1] : dataUrl;
                var bytes = Convert.FromBase64String(base64);
                var fileName = fileNameWithoutExt + ".jpg";
                var filePath = Path.Combine(folder, fileName);
                System.IO.File.WriteAllBytes(filePath, bytes);
                return $"Resources/Expenses/{Path.GetFileName(folder)}/{fileName}";
            }
            catch
            {
                return null;
            }
        }

        private static ExpenseResponseDTO ToDto(Expense e) => new ExpenseResponseDTO
        {
            Id              = e.Id,
            DoctorId        = e.DoctorId,
            ClinicId        = e.ClinicId,
            IsShared        = e.IsShared,
            ExpenseDate     = e.ExpenseDate,
            Amount          = e.Amount,
            Description     = e.Description,
            Category        = e.Category,
            ExpenseType     = e.ExpenseType,
            PaymentMode     = e.PaymentMode,
            Notes           = e.Notes,
            AssetName       = e.AssetName,
            ExpectedLifeYrs = e.ExpectedLifeYrs,
            WarrantyExpiry  = e.WarrantyExpiry,
            ReceiptImagePath  = e.ReceiptImagePath,
            WarrantyImagePath = e.WarrantyImagePath,
            CreatedAt       = e.CreatedAt
        };
    }
}
