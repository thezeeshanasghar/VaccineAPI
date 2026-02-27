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
                baDTO.VaccineName = "";
            return new Response<List<BrandAmountDTO>>(true, null, brandAmountDTOs);
        }

        [HttpGet("clinic/{Id}")]
        public Response<List<BrandAmountDTO>> Getonclinic(int Id)
        {
            var brandAmountDBs = _db.BrandAmounts.Include(x => x.Brand).Include(x => x.Clinic).Where(x => x.ClinicId == Id).ToList();

            if (brandAmountDBs == null || !brandAmountDBs.Any())
            {
                return new Response<List<BrandAmountDTO>>(false,"No brands found for the given clinic ID.",null);
            }

            var brandAmountDTOs = _mapper.Map<List<BrandAmountDTO>>(brandAmountDBs);

            foreach (var baDTO in brandAmountDTOs)
            {
                baDTO.VaccineName = "";
            }

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

        [HttpGet("brandamountclinicwisepdf/{clinicId}")]
        public IActionResult GetBrandAmountClinicWisePdf(int clinicId)
        {
            try
            {
                var brandAmounts = _db.BrandAmounts
                    .Include(x => x.Brand)
                    .Include(x => x.Clinic)
                    .Where(x => x.ClinicId == clinicId)
                    .OrderBy(x => x.Brand.Name)
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

                    Font title1Font = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                    Paragraph title1 = new Paragraph(brandAmounts.FirstOrDefault()?.Clinic?.Name ?? "Unknown clinic", title1Font);
                    title1.Alignment = Element.ALIGN_CENTER;
                    title1.SpacingAfter = 20f;
                    document.Add(title1);
                    // Add date
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);
                    Paragraph date = new Paragraph($"Date: {DateTime.Now:dd/MM/yyyy}", normalFont);
                    date.SpacingAfter = 20f;
                    document.Add(date);

                    // Create table
                    PdfPTable table = new PdfPTable(7);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 0.15f, 1.22f, 0.3f, 0.5f, 0.5f, 0.5f, 0.5f });

                    // Add headers
                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    table.AddCell(new PdfPCell(new Phrase("#", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Brand Name", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Quantity", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Purchase Price", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Purchase Value", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Sale Price", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Sale Value", headerFont)));

                    // Group by brand name and aggregate data
                    var groupedBrands = brandAmounts
                        .GroupBy(x => x.Brand?.Name)
                        .Select(g => new
                        {
                            BrandName = g.Key,
                            TotalQuantity = g.Sum(x => x.Count),
                            // Weighted average purchase price
                            AvgPurchasePrice = g.Sum(x => x.Count) != 0 
                                ? g.Sum(x => x.PurchasedAmt * x.Count) / g.Sum(x => x.Count) 
                                : 0,
                            TotalPurchaseValue = g.Sum(x => x.PurchasedAmt * x.Count),
                            // Weighted average sale price
                            AvgSalePrice = g.Sum(x => x.Count) != 0 
                                ? g.Sum(x => x.Amount * x.Count) / g.Sum(x => x.Count) 
                                : 0,
                            TotalSaleValue = g.Sum(x => x.Amount * x.Count)
                        })
                        .OrderBy(x => x.BrandName)
                        .ToList();

                    // Add data
                    int i = 1;
                    foreach (var item in groupedBrands)
                    {
                        table.AddCell(new Phrase(i.ToString(), normalFont));
                        
                        // Display only brand name without vaccine names
                        table.AddCell(new Phrase(item.BrandName ?? "Unknown", normalFont));
                        
                        table.AddCell(new Phrase(item.TotalQuantity.ToString(), normalFont));
                        table.AddCell(new Phrase($"₹{item.AvgPurchasePrice:N2}", normalFont));
                        table.AddCell(new Phrase($"₹{item.TotalPurchaseValue:N2}", normalFont));
                        table.AddCell(new Phrase($"₹{item.AvgSalePrice:N2}", normalFont));
                        table.AddCell(new Phrase($"₹{item.TotalSaleValue:N2}", normalFont));
                        i++;
                    }

                    document.Add(table);

                    decimal totalPurchaseValue = brandAmounts.Sum(x => x.PurchasedAmt * x.Count);
                    decimal totalSaleValue = brandAmounts.Sum(x => x.Amount * x.Count);
                    document.Add(new Paragraph("\n"));

                    Font totalFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    Paragraph totals = new Paragraph();
                    totals.Add(new Chunk($"Total Purchase Value: ₹{totalPurchaseValue:N2}\n", totalFont));
                    totals.Add(new Chunk($"Total Sale Value: ₹{totalSaleValue:N2}\n", totalFont));
                    totals.Alignment = Element.ALIGN_RIGHT;
                    totals.SpacingAfter = 30f;
                    document.Add(totals);
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
