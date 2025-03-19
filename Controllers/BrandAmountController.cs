using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrandAmountController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BrandAmountController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("{Id}")]
        public Response<List<BrandAmountDTO>> Get(int Id)
        {
            List<BrandAmount> brandAmountDBs = _db.BrandAmounts.Include(x => x.Brand).Include(x => x.Doctor).Where(x => x.DoctorId == Id).ToList();
            if (brandAmountDBs == null || brandAmountDBs.Count() == 0)
                return new Response<List<BrandAmountDTO>>(false, "Brand not found", null);
            List<BrandAmountDTO> brandAmountDTOs = _mapper.Map<List<BrandAmountDTO>>(brandAmountDBs);
            foreach (BrandAmountDTO baDTO in brandAmountDTOs)
                baDTO.VaccineName = _db.Brands.Include(x => x.Vaccine).Where(x => x.Id == baDTO.BrandId).First().Vaccine.Name;
            return new Response<List<BrandAmountDTO>>(true, null, brandAmountDTOs);
        }

        // [HttpPost]
        // public async Task<ActionResult<BrandAmount>> Post(BrandAmount BrandAmount)
        // {
        //     _db.BrandAmounts.Update(BrandAmount);
        //     await _db.SaveChangesAsync();

        //     return CreatedAtAction(nameof(GetSingle), new { id = BrandAmount.Id }, BrandAmount);
        // }

        [HttpPut("inventory")]
        public Response<List<BrandAmountDTO>> Putinventory([FromBody] List<BrandAmountDTO> brandAmountDTOs)

        {
            foreach (var brandAmountDTO in brandAmountDTOs)
            {
                var brandAmoundDB = _db.BrandAmounts.Where(b => b.Id == brandAmountDTO.Id).FirstOrDefault();
                brandAmoundDB.Count = brandAmountDTO.Count;
                _db.SaveChanges();
            }
            return new Response<List<BrandAmountDTO>>(true, null, brandAmountDTOs);
        }


        [HttpPut]
        public Response<List<BrandAmountDTO>> Put([FromBody] List<BrandAmountDTO> brandAmountDTOs)

        {
            foreach (var brandAmountDTO in brandAmountDTOs)
            {
                var brandAmoundDB = _db.BrandAmounts.Where(b => b.Id == brandAmountDTO.Id).FirstOrDefault();
                brandAmoundDB.Amount = brandAmountDTO.Amount;
                // brandAmoundDB.Count = brandAmountDTO.Count;
                // brandAmoundDB.SupName = brandAmountDTO.SupName;
                // brandAmoundDB.PurchasedAmt = brandAmountDTO.PurchasedAmt;
                // brandAmoundDB.IsPaid = brandAmountDTO.IsPaid;
                _db.SaveChanges();
            }
            return new Response<List<BrandAmountDTO>>(true, null, brandAmountDTOs);
        }

        [HttpGet("pdf/{doctorId}")]
        public IActionResult GetPdf(int doctorId)
        {
            try
            {
                var brandAmounts = _db.BrandAmounts
                    .Include(x => x.Brand)
                        .ThenInclude(b => b.Vaccine)
                    .Include(x => x.Doctor)
                    .Where(x => x.DoctorId == doctorId)
                    .ToList();

                if (!brandAmounts.Any())
                    return NotFound("No brand amounts found");

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);

                    document.Open();

                    // Add title
                    Font titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                    Paragraph title = new Paragraph("STOCK SUMMARY REPORT", titleFont);
                    title.Alignment = Element.ALIGN_CENTER;
                    title.SpacingAfter = 20f;
                    document.Add(title);

                    // Add date
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);
                    Paragraph date = new Paragraph($"Date: {DateTime.Now:dd/MM/yyyy}", normalFont);
                    date.SpacingAfter = 20f;
                    document.Add(date);

                    // Create table
                    PdfPTable table = new PdfPTable(7); // 5 columns
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 0.25f, 1f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f });

                    // Add headers
                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    table.AddCell(new PdfPCell(new Phrase("#", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Item", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Quantity", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Purchase Price", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Purchase Value", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Sale Price", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Sale Value", headerFont)));

                    // Add data
                    int i = 1;
                    foreach (var item in brandAmounts)
                    {
                        table.AddCell(new Phrase(i.ToString(), normalFont));
                        table.AddCell(new Phrase(item.Brand?.Name ?? "", normalFont));
                        // table.AddCell(new Phrase(item.Brand?.Vaccine?.Name ?? "", normalFont));
                        table.AddCell(new Phrase(item.Count.ToString(), normalFont));
                        table.AddCell(new Phrase($"₹{item.Amount:N2}", normalFont));
                        table.AddCell(new Phrase($"₹{item.Amount*item.Count}", normalFont));
                        table.AddCell(new Phrase($"₹{item.PurchasedAmt:N2}", normalFont));
                        table.AddCell(new Phrase($"₹{item.PurchasedAmt*item.Count}", normalFont));
                        i++;
                    }

                    // Add totals
                    PdfPCell totalCell = new PdfPCell(new Phrase("Totals:", headerFont));
                    totalCell.Colspan = 2;
                    table.AddCell(totalCell);
                    table.AddCell(new Phrase(brandAmounts.Sum(x => x.Count).ToString(), headerFont));
                    table.AddCell(new Phrase($"₹{brandAmounts.Sum(x => x.Amount):N2}", headerFont));
                    table.AddCell(new Phrase($"₹{brandAmounts.Sum(x => x.PurchasedAmt):N2}", headerFont));

                    document.Add(table);
                    document.Close();

                    byte[] pdfBytes = ms.ToArray();
                    return File(pdfBytes, "application/pdf", $"BrandAmountReport_{DateTime.Now:yyyyMMdd}.pdf");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.BrandAmounts.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.BrandAmounts.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
