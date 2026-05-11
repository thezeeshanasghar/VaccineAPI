using System;
using System.IO;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FixedAssetController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public FixedAssetController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("doctor/{doctorId}")]
        public ActionResult<Response<IEnumerable<FixedAssetDTO>>> GetByDoctor(
            long doctorId,
            [FromQuery] long? clinicId,
            [FromQuery] bool? includeDisposed)
        {
            var q = _db.FixedAssets
                .Include(a => a.Clinic)
                .Where(a => a.DoctorId == doctorId);

            if (clinicId.HasValue) q = q.Where(a => a.ClinicId == clinicId.Value);
            if (includeDisposed != true) q = q.Where(a => !a.IsDisposed);

            var list = q.OrderBy(a => a.AssetName).ToList();
            return Ok(new Response<IEnumerable<FixedAssetDTO>>(true, null, _mapper.Map<List<FixedAssetDTO>>(list)));
        }

        [HttpGet("{id}")]
        public ActionResult<Response<FixedAssetDTO>> GetById(long id)
        {
            var asset = _db.FixedAssets.Include(a => a.Clinic).FirstOrDefault(a => a.Id == id);
            if (asset == null)
                return NotFound(new Response<FixedAssetDTO>(false, "Not found", null));
            return Ok(new Response<FixedAssetDTO>(true, null, _mapper.Map<FixedAssetDTO>(asset)));
        }

        [HttpPost]
        public ActionResult<Response<FixedAssetDTO>> Create([FromBody] FixedAssetDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.AssetName))
                return BadRequest(new Response<FixedAssetDTO>(false, "Asset name is required", null));
            if (dto.PurchaseCost <= 0)
                return BadRequest(new Response<FixedAssetDTO>(false, "Purchase cost must be greater than zero", null));
            if (dto.ExpectedLifeYrs <= 0)
                return BadRequest(new Response<FixedAssetDTO>(false, "Expected life must be greater than zero", null));

            var asset = _mapper.Map<FixedAsset>(dto);
            asset.Id = 0;
            asset.MonthlyDeprn = Math.Round(dto.PurchaseCost / (dto.ExpectedLifeYrs * 12), 2);
            asset.AccumDeprn = ComputeAccumDeprn(dto.PurchaseDate, asset.MonthlyDeprn);
            asset.BookValue = Math.Max(0, dto.PurchaseCost - asset.AccumDeprn);
            asset.IsDisposed = false;
            asset.CreatedAt = DateTime.UtcNow;

            _db.FixedAssets.Add(asset);
            _db.SaveChanges();

            var saved = _db.FixedAssets.Include(a => a.Clinic).FirstOrDefault(a => a.Id == asset.Id);
            return Ok(new Response<FixedAssetDTO>(true, "Asset saved", _mapper.Map<FixedAssetDTO>(saved)));
        }

        [HttpPut("{id}")]
        public ActionResult<Response<FixedAssetDTO>> Update(long id, [FromBody] FixedAssetDTO dto)
        {
            var asset = _db.FixedAssets.FirstOrDefault(a => a.Id == id);
            if (asset == null)
                return NotFound(new Response<FixedAssetDTO>(false, "Not found", null));

            asset.AssetName = dto.AssetName;
            asset.Category = dto.Category;
            asset.ClinicId = dto.ClinicId;
            asset.ExpenseId = dto.ExpenseId;
            asset.PurchaseDate = dto.PurchaseDate;
            asset.PurchaseCost = dto.PurchaseCost;
            asset.ExpectedLifeYrs = dto.ExpectedLifeYrs;
            asset.MonthlyDeprn = Math.Round(dto.PurchaseCost / (dto.ExpectedLifeYrs * 12), 2);
            asset.AccumDeprn = ComputeAccumDeprn(dto.PurchaseDate, asset.MonthlyDeprn);
            asset.BookValue = Math.Max(0, dto.PurchaseCost - asset.AccumDeprn);
            asset.WarrantyExpiry = dto.WarrantyExpiry;
            if (!string.IsNullOrEmpty(dto.WarrantyImagePath))
                asset.WarrantyImagePath = dto.WarrantyImagePath;
            asset.Notes = dto.Notes;

            _db.SaveChanges();
            var saved = _db.FixedAssets.Include(a => a.Clinic).FirstOrDefault(a => a.Id == id);
            return Ok(new Response<FixedAssetDTO>(true, "Updated", _mapper.Map<FixedAssetDTO>(saved)));
        }

        [HttpPatch("{id}/dispose")]
        public ActionResult<Response<FixedAssetDTO>> Dispose(long id, [FromBody] FixedAssetDTO dto)
        {
            var asset = _db.FixedAssets.FirstOrDefault(a => a.Id == id);
            if (asset == null)
                return NotFound(new Response<FixedAssetDTO>(false, "Not found", null));

            asset.IsDisposed = true;
            asset.DisposalDate = dto.DisposalDate ?? DateTime.UtcNow;
            asset.DisposalValue = dto.DisposalValue ?? 0;
            asset.BookValue = 0;
            _db.SaveChanges();

            var saved = _db.FixedAssets.Include(a => a.Clinic).FirstOrDefault(a => a.Id == id);
            return Ok(new Response<FixedAssetDTO>(true, "Asset disposed", _mapper.Map<FixedAssetDTO>(saved)));
        }

        [HttpDelete("{id}")]
        public ActionResult<Response<object>> Delete(long id)
        {
            var asset = _db.FixedAssets.FirstOrDefault(a => a.Id == id);
            if (asset == null)
                return NotFound(new Response<object>(false, "Not found", null));
            _db.FixedAssets.Remove(asset);
            _db.SaveChanges();
            return Ok(new Response<object>(true, "Deleted", null));
        }

        [HttpPost("upload-warranty")]
        [DisableRequestSizeLimit]
        public IActionResult UploadWarranty()
        {
            try
            {
                if (Request.Form?.Files == null || Request.Form.Files.Count == 0)
                    return BadRequest("No file uploaded.");
                var file = Request.Form.Files[0];
                var folderName = Path.Combine("Resources", "Expenses", "warranties");
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

        private static decimal ComputeAccumDeprn(DateTime purchaseDate, decimal monthlyDeprn)
        {
            var now = DateTime.UtcNow;
            var months = ((now.Year - purchaseDate.Year) * 12) + (now.Month - purchaseDate.Month);
            if (months < 0) months = 0;
            return Math.Round(monthlyDeprn * months, 2);
        }
    }
}
