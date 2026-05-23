using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockOverviewController : ControllerBase
    {
        private readonly Context _db;
        public StockOverviewController(Context db) { _db = db; }

        // GET /api/stockoverview?doctorId=X&clinicId=Y
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            // Load all BrandAmounts for this doctor+clinic
            var brandAmounts = await _db.BrandAmounts
                .Include(ba => ba.Brand)
                .Where(ba => ba.DoctorId == doctorId && ba.ClinicId == clinicId)
                .ToListAsync();

            var brandIds = brandAmounts.Select(ba => ba.BrandId).ToList();

            // Load VaccineBrand join for vaccine names
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            // Load all stock rows for these brands at this clinic
            // Stock is clinic-scoped via Bill.ClinicId
            var stockRows = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => brandIds.Contains(s.BrandId) && s.Quantity > 0 && s.BillId != null && s.Bill.ClinicId == clinicId)
                .OrderBy(s => s.Expiry == null ? 1 : 0)
                .ThenBy(s => s.Expiry)
                .ThenBy(s => s.Id)
                .ToListAsync();

            var result = brandAmounts
                .OrderBy(ba =>
                {
                    var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == ba.BrandId);
                    return vb != null && vb.Vaccine != null ? vb.Vaccine.Name : ba.Brand.Name;
                })
                .Select(ba =>
                {
                    var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == ba.BrandId);
                    var batches = stockRows
                        .Where(s => s.BrandId == ba.BrandId)
                        .Select(s => new
                        {
                            s.Id,
                            s.BatchLot,
                            Expiry = s.Expiry.HasValue ? s.Expiry.Value.ToString("yyyy-MM-dd") : null,
                            s.Quantity,
                            UnitPrice = s.StockAmount,
                            LineTotal = s.Quantity * s.StockAmount
                        })
                        .ToList();

                    return new
                    {
                        BrandId = ba.BrandId,
                        BrandName = ba.Brand != null ? ba.Brand.Name : "",
                        VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                        TotalCount = ba.Count,
                        SalePrice = ba.Amount,
                        Batches = batches
                    };
                })
                .Where(x => x.TotalCount > 0 || x.Batches.Count > 0)
                .ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // GET /api/stockoverview/pdf?doctorId=X&clinicId=Y
        [HttpGet("pdf")]
        public async Task<IActionResult> GetPdf([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            var brandAmounts = await _db.BrandAmounts
                .Include(ba => ba.Brand)
                .Where(ba => ba.DoctorId == doctorId && ba.ClinicId == clinicId)
                .ToListAsync();

            var brandIds = brandAmounts.Select(ba => ba.BrandId).ToList();

            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            var stockRows = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => brandIds.Contains(s.BrandId) && s.Quantity > 0 && s.BillId != null && s.Bill.ClinicId == clinicId)
                .OrderBy(s => s.Expiry == null ? 1 : 0)
                .ThenBy(s => s.Expiry)
                .ThenBy(s => s.Id)
                .ToListAsync();

            var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId);
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 50, 36);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                // Fonts
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, new BaseColor(21, 101, 192));
                var subFont = FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var brandFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, new BaseColor(26, 26, 46));
                var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(26, 26, 46));
                var smallFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(84, 110, 122));
                var redFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(198, 40, 40));
                var orangeFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(245, 127, 23));

                // Title
                string doctorName = doctor != null ? doctor.DisplayName : "";
                string clinicName = clinic != null ? clinic.Name : "";
                doc.Add(new Paragraph("Stock Overview", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(doctorName + (clinicName != "" ? " — " + clinicName : ""), subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph("Generated: " + System.DateTime.Now.ToString("dd MMM yyyy, HH:mm"), smallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });

                var brands = brandAmounts
                    .OrderBy(ba =>
                    {
                        var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == ba.BrandId);
                        return vb != null && vb.Vaccine != null ? vb.Vaccine.Name : ba.Brand.Name;
                    })
                    .Select(ba =>
                    {
                        var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == ba.BrandId);
                        var batches = stockRows.Where(s => s.BrandId == ba.BrandId).ToList();
                        return new
                        {
                            BrandName = ba.Brand != null ? ba.Brand.Name : "",
                            VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                            TotalCount = ba.Count,
                            Batches = batches
                        };
                    })
                    .Where(x => x.TotalCount > 0 || x.Batches.Count > 0)
                    .ToList();

                foreach (var brand in brands)
                {
                    // Brand name row spanning full width
                    var brandTable = new PdfPTable(1) { WidthPercentage = 100, SpacingBefore = 8 };
                    var brandCell = new PdfPCell(new Phrase(brand.VaccineName + " — " + brand.BrandName + "   (" + brand.TotalCount + " units)", brandFont))
                    {
                        BackgroundColor = new BaseColor(238, 242, 247),
                        Border = Rectangle.NO_BORDER,
                        Padding = 6
                    };
                    brandTable.AddCell(brandCell);
                    doc.Add(brandTable);

                    if (brand.Batches.Count == 0)
                    {
                        var emptyTable = new PdfPTable(1) { WidthPercentage = 100 };
                        emptyTable.AddCell(new PdfPCell(new Phrase("No batch records", smallFont))
                        {
                            Border = Rectangle.NO_BORDER,
                            Padding = 4,
                            PaddingLeft = 12
                        });
                        doc.Add(emptyTable);
                        continue;
                    }

                    // Batch detail table: Batch | Expiry | Qty | Unit Price
                    var tbl = new PdfPTable(4) { WidthPercentage = 100 };
                    tbl.SetWidths(new float[] { 2.2f, 2f, 1f, 1.5f });

                    BaseColor headerBg = new BaseColor(21, 101, 192);
                    string[] headers = { "Batch / Lot", "Expiry", "Qty", "Unit Price" };
                    foreach (var h in headers)
                    {
                        tbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                        {
                            BackgroundColor = headerBg,
                            Border = Rectangle.NO_BORDER,
                            Padding = 5,
                            HorizontalAlignment = h == "Qty" || h == "Unit Price" ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                        });
                    }

                    bool alt = false;
                    foreach (var s in brand.Batches)
                    {
                        BaseColor rowBg = alt ? new BaseColor(250, 250, 255) : new BaseColor(255, 255, 255);
                        bool expired = s.Expiry.HasValue && s.Expiry.Value < System.DateTime.Today;
                        bool soon = !expired && s.Expiry.HasValue && (s.Expiry.Value - System.DateTime.Today).TotalDays <= 90;
                        if (expired) rowBg = new BaseColor(255, 248, 248);
                        else if (soon) rowBg = new BaseColor(255, 253, 231);

                        string batchLot = string.IsNullOrEmpty(s.BatchLot) ? "—" : s.BatchLot;
                        string expiryStr = s.Expiry.HasValue ? s.Expiry.Value.ToString("dd MMM yyyy") : "—";
                        string expiryLabel = expiryStr + (expired ? " [Expired]" : soon ? " [Soon]" : "");
                        Font expiryFont = expired ? redFont : soon ? orangeFont : cellFont;

                        tbl.AddCell(new PdfPCell(new Phrase(batchLot, cellFont)) { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4, PaddingLeft = 8 });
                        tbl.AddCell(new PdfPCell(new Phrase(expiryLabel, expiryFont)) { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase(s.Quantity.ToString(), cellFont)) { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        tbl.AddCell(new PdfPCell(new Phrase("Rs " + s.StockAmount.ToString("N2"), cellFont)) { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        alt = !alt;
                    }

                    doc.Add(tbl);
                }

                doc.Close();
                var bytes = ms.ToArray();
                return File(bytes, "application/pdf", "StockOverview.pdf");
            }
        }
    }
}
