using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using VaccineAPI.Models;
using VaccineAPI.helper;

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

            // Load all stock rows for these brands at this clinic.
            // v2: clinic scope is Stock.ClinicId (opening-balance / transfer-in rows, BillId NULL)
            // OR Bill.ClinicId (purchase rows, ClinicId still NULL). Same resolution FEFO uses.
            var stockRows = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => brandIds.Contains(s.BrandId) && s.Quantity > 0
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
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
                    // Multiple bills can each own a Stock row for the same Brand+Batch+Expiry
                    // (never merged at write-time — see BillController). Collapse them here into
                    // one displayed line per batch so the UI never shows duplicate batch rows.
                    // Purchase price is intentionally not surfaced here (Stock Overview never
                    // displayed it) — rows in the same batch can legitimately have different
                    // costs across bills, and there is no single correct "the" price to show.
                    var batches = stockRows
                        .Where(s => s.BrandId == ba.BrandId)
                        .GroupBy(s => new { s.BatchLot, s.Expiry })
                        .Select(g => new
                        {
                            Id = g.Max(s => s.Id),
                            g.Key.BatchLot,
                            Expiry = g.Key.Expiry.HasValue ? g.Key.Expiry.Value.ToString("yyyy-MM-dd") : null,
                            Quantity = g.Sum(s => s.Quantity)
                        })
                        .OrderBy(b => b.Expiry == null ? 1 : 0)
                        .ThenBy(b => b.Expiry)
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
                .Where(s => brandIds.Contains(s.BrandId) && s.Quantity > 0
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
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
                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(255, 255, 255));
                var brandFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(26, 26, 46));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(26, 26, 46));
                var smallFont  = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(84, 110, 122));
                var redFont    = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(198, 40, 40));
                var orangeFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(180, 90, 0));

                string doctorName = doctor != null ? doctor.DisplayName : "";
                string clinicName = clinic != null ? clinic.Name : "";
                doc.Add(new Paragraph("Stock Overview", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(doctorName + (clinicName != "" ? " — " + clinicName : ""), subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph("Generated: " + System.DateTime.Now.ToString("dd MMM yyyy, HH:mm"), smallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 10 });

                var brands = brandAmounts
                    .OrderBy(ba => ba.Brand != null ? ba.Brand.Name : "")
                    .Select(ba =>
                    {
                        // Collapse multiple bills' Stock rows for the same Batch+Expiry into one
                        // displayed line — see same logic/reasoning in GET above.
                        var batches = stockRows
                            .Where(s => s.BrandId == ba.BrandId)
                            .GroupBy(s => new { s.BatchLot, s.Expiry })
                            .Select(g => new
                            {
                                BatchLot = g.Key.BatchLot,
                                Expiry = g.Key.Expiry,
                                Quantity = g.Sum(s => s.Quantity)
                            })
                            .OrderBy(b => b.Expiry == null ? 1 : 0)
                            .ThenBy(b => b.Expiry)
                            .ToList();
                        return new
                        {
                            BrandName  = ba.Brand != null ? ba.Brand.Name : "",
                            TotalCount = ba.Count,
                            Batches    = batches
                        };
                    })
                    .Where(x => x.TotalCount > 0 || x.Batches.Count > 0)
                    .ToList();

                // Single unified table: Brand | Batch/Lot | Expiry | Units
                var tbl = new PdfPTable(4) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(new float[] { 2.2f, 2.4f, 2.0f, 1.0f });

                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Brand", "Batch / Lot", "Expiry", "Units" };
                foreach (var h in headers)
                {
                    bool right = h == "Units";
                    tbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                    {
                        BackgroundColor      = headerBg,
                        Border               = Rectangle.NO_BORDER,
                        Padding              = 5,
                        HorizontalAlignment  = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                    });
                }

                BaseColor brandRowBg  = new BaseColor(232, 240, 254);
                BaseColor evenBg      = new BaseColor(255, 255, 255);
                BaseColor oddBg       = new BaseColor(248, 250, 253);
                BaseColor expiredBg   = new BaseColor(255, 245, 245);
                BaseColor soonBg      = new BaseColor(255, 253, 231);

                foreach (var brand in brands)
                {
                    if (brand.Batches.Count == 0)
                    {
                        // Brand name cell spanning Brand column, dash for rest
                        tbl.AddCell(new PdfPCell(new Phrase(brand.BrandName, brandFont))
                            { BackgroundColor = brandRowBg, Border = Rectangle.NO_BORDER, Padding = 4, PaddingLeft = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase("—", cellFont))
                            { BackgroundColor = brandRowBg, Border = Rectangle.NO_BORDER, Padding = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase("—", cellFont))
                            { BackgroundColor = brandRowBg, Border = Rectangle.NO_BORDER, Padding = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase("0", cellFont))
                            { BackgroundColor = brandRowBg, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        continue;
                    }

                    bool alt = false;
                    for (int i = 0; i < brand.Batches.Count; i++)
                    {
                        var s = brand.Batches[i];
                        bool expired = s.Expiry.HasValue && s.Expiry.Value < System.DateTime.Today;
                        bool soon    = !expired && s.Expiry.HasValue && (s.Expiry.Value - System.DateTime.Today).TotalDays <= 90;

                        BaseColor rowBg = expired ? expiredBg : soon ? soonBg : (alt ? oddBg : evenBg);

                        string batchLot   = string.IsNullOrEmpty(s.BatchLot) ? "—" : s.BatchLot;
                        string expiryStr  = s.Expiry.HasValue ? s.Expiry.Value.ToString("dd MMM yyyy") : "—";
                        string expiryLabel = expiryStr + (expired ? " [Exp]" : soon ? " [Soon]" : "");
                        Font expiryFont   = expired ? redFont : soon ? orangeFont : cellFont;

                        // Brand name only on first batch row of this brand
                        if (i == 0)
                            tbl.AddCell(new PdfPCell(new Phrase(brand.BrandName + " (" + brand.TotalCount + ")", brandFont))
                                { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4, PaddingLeft = 4, Rowspan = 1 });
                        else
                            tbl.AddCell(new PdfPCell(new Phrase("", cellFont))
                                { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4 });

                        tbl.AddCell(new PdfPCell(new Phrase(batchLot, cellFont))
                            { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase(expiryLabel, expiryFont))
                            { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase(s.Quantity.ToString(), cellFont))
                            { BackgroundColor = rowBg, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });

                        alt = !alt;
                    }

                    // Thin separator row between brands
                    for (int col = 0; col < 4; col++)
                        tbl.AddCell(new PdfPCell(new Phrase(""))
                            { BackgroundColor = new BaseColor(210, 220, 240), Border = Rectangle.NO_BORDER, FixedHeight = 1f, Padding = 0 });
                }

                doc.Add(tbl);
                doc.Close();
                var bytes = ms.ToArray();
                return File(bytes, "application/pdf", ReportFileName.Build("StockOverview", clinicName));
            }
        }
    }
}
