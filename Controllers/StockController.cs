using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
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
    public class StockController : ControllerBase
    {
        private readonly Context _db;
        public StockController(Context db) { _db = db; }

        // GET /api/stock/batch-lots?brandId=X&clinicId=Y
        [HttpGet("batch-lots")]
        public async Task<IActionResult> GetBatchLots([FromQuery] long brandId, [FromQuery] long clinicId)
        {
            var stocks = await _db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BrandId == brandId && s.Quantity > 0 && s.BillId != null && s.Bill.ClinicId == clinicId)
                .OrderBy(s => s.Expiry)
                .Select(s => new
                {
                    s.BatchLot,
                    s.Expiry,
                    s.Quantity,
                    s.BrandId,
                    s.StockAmount
                })
                .ToListAsync();

            return Ok(new { IsSuccess = true, ResponseData = stocks });
        }

        // GET /api/stock/sales-report?clinicId=X&from=DATE&to=DATE
        [HttpGet("sales-report")]
        public async Task<IActionResult> GetSalesReport(
            [FromQuery] long clinicId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            string clinicName = clinic != null ? clinic.Name : $"Clinic {clinicId}";

            // Vaccinations given to patients
            var schedules = await _db.Schedules
                .Include(s => s.Brand)
                .Include(s => s.Child)
                .Where(s => s.Child.ClinicId == clinicId
                         && s.IsDone == true
                         && s.BrandId != null
                         && s.GivenDate.HasValue
                         && s.GivenDate.Value.Date >= from.Date
                         && s.GivenDate.Value.Date <= to.Date)
                .ToListAsync();

            // Direct sales
            var directSales = await _db.DirectSales
                .Include(d => d.Brand)
                .Where(d => d.ClinicId == clinicId
                         && d.SaleDate.Date >= from.Date
                         && d.SaleDate.Date <= to.Date)
                .ToListAsync();

            if (schedules.Count == 0 && directSales.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No sales data for the selected period" });

            // Get sale prices from brandamounts for vaccinations
            var vaxBrandIds = schedules.Where(s => s.BrandId.HasValue).Select(s => s.BrandId.Value).Distinct().ToList();
            var brandAmounts = await _db.BrandAmounts
                .Where(b => b.ClinicId == clinicId && vaxBrandIds.Contains(b.BrandId))
                .ToListAsync();

            // Group vaccinations by brand
            var vaxGroups = schedules
                .Where(s => s.BrandId.HasValue)
                .GroupBy(s => s.BrandId.Value)
                .Select(g =>
                {
                    var ba = brandAmounts.FirstOrDefault(b => b.BrandId == g.Key);
                    decimal price = ba != null ? ba.Amount : 0;
                    return new
                    {
                        BrandName = g.First().Brand != null ? g.First().Brand.Name : "",
                        QtyGiven = g.Count(),
                        SalePrice = price,
                        Total = g.Count() * price
                    };
                })
                .OrderBy(x => x.BrandName)
                .ToList();

            // Group direct sales by brand
            var saleGroups = directSales
                .GroupBy(d => d.BrandId)
                .Select(g => new
                {
                    BrandName = g.First().Brand != null ? g.First().Brand.Name : "",
                    QtyGiven = g.Sum(d => d.Quantity),
                    SalePrice = g.First().SalePricePerUnit,
                    Total = g.Sum(d => d.TotalSaleValue)
                })
                .OrderBy(x => x.BrandName)
                .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 50, 36);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(26, 26, 46));
                var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, new BaseColor(21, 101, 192));

                doc.Add(new Paragraph("Sales Report", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(clinicName, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"{from:dd MMM yyyy}  –  {to:dd MMM yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });

                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Brand", "Qty", "Sale Price/Unit", "Total Revenue" };
                float[] widths = { 3f, 1f, 1.8f, 1.8f };

                decimal grandTotal = 0;

                // Vaccinations section
                if (vaxGroups.Count > 0)
                {
                    doc.Add(new Paragraph("Vaccinations Given to Patients", sectionFont) { SpacingBefore = 6, SpacingAfter = 4 });
                    var tbl = new PdfPTable(4) { WidthPercentage = 100 };
                    tbl.SetWidths(widths);
                    foreach (var h in headers)
                    {
                        bool right = h != "Brand";
                        tbl.AddCell(new PdfPCell(new Phrase(h, headerFont)) { BackgroundColor = headerBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT });
                    }
                    bool alt = false;
                    decimal secTotal = 0;
                    foreach (var row in vaxGroups)
                    {
                        var bg = alt ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                        alt = !alt;
                        tbl.AddCell(new PdfPCell(new Phrase(row.BrandName, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                        tbl.AddCell(new PdfPCell(new Phrase(row.QtyGiven.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        tbl.AddCell(new PdfPCell(new Phrase(row.SalePrice.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        tbl.AddCell(new PdfPCell(new Phrase(row.Total.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        secTotal += row.Total;
                    }
                    // Section subtotal
                    tbl.AddCell(new PdfPCell(new Phrase("Sub-total", boldCell)) { Colspan = 3, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(secTotal.ToString("N2"), boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    doc.Add(tbl);
                    grandTotal += secTotal;
                }

                // Direct sales section
                if (saleGroups.Count > 0)
                {
                    doc.Add(new Paragraph("Direct (Walk-in) Sales", sectionFont) { SpacingBefore = 10, SpacingAfter = 4 });
                    var tbl2 = new PdfPTable(4) { WidthPercentage = 100 };
                    tbl2.SetWidths(widths);
                    foreach (var h in headers)
                    {
                        bool right = h != "Brand";
                        tbl2.AddCell(new PdfPCell(new Phrase(h, headerFont)) { BackgroundColor = headerBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT });
                    }
                    bool alt2 = false;
                    decimal secTotal2 = 0;
                    foreach (var row in saleGroups)
                    {
                        var bg = alt2 ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                        alt2 = !alt2;
                        tbl2.AddCell(new PdfPCell(new Phrase(row.BrandName, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                        tbl2.AddCell(new PdfPCell(new Phrase(row.QtyGiven.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        tbl2.AddCell(new PdfPCell(new Phrase(row.SalePrice.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        tbl2.AddCell(new PdfPCell(new Phrase(row.Total.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        secTotal2 += row.Total;
                    }
                    tbl2.AddCell(new PdfPCell(new Phrase("Sub-total", boldCell)) { Colspan = 3, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl2.AddCell(new PdfPCell(new Phrase(secTotal2.ToString("N2"), boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    doc.Add(tbl2);
                    grandTotal += secTotal2;
                }

                // Grand total
                var totTbl = new PdfPTable(2) { WidthPercentage = 40, HorizontalAlignment = Element.ALIGN_RIGHT, SpacingBefore = 8 };
                totTbl.SetWidths(new float[] { 1.5f, 1f });
                totTbl.AddCell(new PdfPCell(new Phrase("Grand Total", boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                totTbl.AddCell(new PdfPCell(new Phrase(grandTotal.ToString("N2"), boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                doc.Add(totTbl);

                doc.Close();
                writer.Close();
                return File(ms.ToArray(), "application/pdf", $"SalesReport-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf");
            }
        }

        // GET /api/stock/sales-collection-report?clinicId=X&from=DATE&to=DATE
        [HttpGet("sales-collection-report")]
        public async Task<IActionResult> GetSalesCollectionReport(
            [FromQuery] long clinicId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });

            var schedules = await _db.Schedules
                .Include(s => s.Brand)
                .Include(s => s.Child)
                .Where(s => s.Child.ClinicId == clinicId
                         && s.IsDone == true
                         && s.BrandId != null
                         && s.GivenDate.HasValue
                         && s.GivenDate.Value.Date >= from.Date
                         && s.GivenDate.Value.Date <= to.Date)
                .OrderBy(s => s.GivenDate)
                .ThenBy(s => s.Child.Name)
                .ToListAsync();

            if (schedules.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No sales data for the selected period" });

            // Fallback prices from BrandAmounts
            var brandIds = schedules.Where(s => s.BrandId.HasValue).Select(s => s.BrandId.Value).Distinct().ToList();
            var brandAmounts = await _db.BrandAmounts
                .Where(b => b.ClinicId == clinicId && brandIds.Contains(b.BrandId))
                .ToListAsync();

            // Vaccination fees — primary: InvoiceSubmissions; fallback: Fee table via Invoice
            var childIds = schedules.Select(s => s.ChildId).Distinct().ToList();
            long doctorId = clinic.DoctorId;

            var invoiceSubs = await _db.InvoiceSubmissions
                .Where(x => x.DoctorId == doctorId
                          && childIds.Contains(x.ChildId)
                          && x.InvoiceDate.Date >= from.Date
                          && x.InvoiceDate.Date <= to.Date)
                .ToListAsync();

            // Fee table fallback: join Invoice → Fee to get vaccination charge per patient per day
            // This covers doctor-generated invoices where InvoiceSubmission may not exist yet
            var invoiceRecords = await _db.Invoices
                .Where(i => i.ClinicId == clinicId
                          && childIds.Contains(i.ChildId)
                          && !i.IsVoided)
                .ToListAsync();
            var invoiceIds = invoiceRecords.Select(i => i.InvoiceId).Distinct().ToList();
            var feeRecords = await _db.Fee
                .Where(f => invoiceIds.Contains(f.InvoiceId))
                .ToListAsync();

            // Group by patient+day
            var patientVisits = schedules
                .GroupBy(s => new { s.ChildId, Date = s.GivenDate!.Value.Date })
                .OrderBy(g => g.Key.Date)
                .ThenBy(g => g.First().Child?.Name)
                .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 50, 36);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9,  new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8,  new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8,  new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8,  new BaseColor(26, 26, 46));

                doc.Add(new Paragraph("Sales Report", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(clinic.Name, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"FROM {from:dd-MM-yyyy} TO {to:dd-MM-yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 6 });

                // Pre-calculate totals for summary header
                decimal totalVaxFee = 0;
                decimal totalItemsPrice = 0;
                int totalPatients = patientVisits.Count;

                foreach (var visit in patientVisits)
                {
                    decimal consFee = ResolveVaccinationFee(visit.Key.ChildId, visit.Key.Date, invoiceSubs, invoiceRecords, feeRecords);
                    totalVaxFee += consFee;
                    foreach (var s in visit)
                    {
                        bool hasAmount = s.Amount.HasValue && s.Amount.Value != 0;
                        var ba = brandAmounts.FirstOrDefault(b => s.BrandId.HasValue && b.BrandId == s.BrandId.Value);
                        decimal price = hasAmount ? s.Amount.Value : (ba != null ? ba.Amount : 0);
                        totalItemsPrice += price;
                    }
                }
                decimal grandTotal = totalVaxFee + totalItemsPrice;

                // Summary block
                var sumTbl = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10 };
                sumTbl.SetWidths(new float[] { 1.5f, 1.5f, 1.5f, 1.5f });
                string[] sumHeaders = { "Total Patients", "Total Vaccination Fee", "Total Items Price", "Grand Total Cash" };
                string[] sumValues  = { totalPatients.ToString(), totalVaxFee.ToString("N2"), totalItemsPrice.ToString("N2"), grandTotal.ToString("N2") };
                var sumHdrBg = new BaseColor(21, 101, 192);
                foreach (var h in sumHeaders)
                    sumTbl.AddCell(new PdfPCell(new Phrase(h, headerFont)) { BackgroundColor = sumHdrBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
                var sumValBg = new BaseColor(232, 240, 254);
                foreach (var v in sumValues)
                    sumTbl.AddCell(new PdfPCell(new Phrase(v, boldCell)) { BackgroundColor = sumValBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
                doc.Add(sumTbl);

                // Per-patient detail table
                BaseColor headerBg = new BaseColor(21, 101, 192);
                float[] colWidths = { 1.4f, 2.2f, 1.4f, 2.2f, 0.6f, 1.2f };
                string[] colHeaders = { "Date", "Patient", "Vaccination Fee", "Item", "Qty", "Price" };

                var mainTbl = new PdfPTable(6) { WidthPercentage = 100, SpacingBefore = 4 };
                mainTbl.SetWidths(colWidths);
                foreach (var h in colHeaders)
                {
                    bool right = h == "Consult. Fee" || h == "Qty" || h == "Price";
                    mainTbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                    {
                        BackgroundColor = headerBg, Border = Rectangle.NO_BORDER,
                        Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                    });
                }

                bool alt = false;
                foreach (var visit in patientVisits)
                {
                    var scheduleRows = visit.OrderBy(s => s.Brand != null ? s.Brand.Name : "").ToList();
                    decimal consFee = ResolveVaccinationFee(visit.Key.ChildId, visit.Key.Date, invoiceSubs, invoiceRecords, feeRecords);
                    string patientName = scheduleRows.Count > 0 && scheduleRows[0].Child != null ? scheduleRows[0].Child.Name : "";
                    string visitDate = visit.Key.Date.ToString("dd-MM-yyyy");

                    var bg = alt ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                    alt = !alt;

                    decimal patientTotal = consFee;
                    bool firstRow = true;

                    foreach (var s in scheduleRows)
                    {
                        bool hasAmount = s.Amount.HasValue && s.Amount.Value != 0;
                        var ba = brandAmounts.FirstOrDefault(b => s.BrandId.HasValue && b.BrandId == s.BrandId.Value);
                        decimal price = hasAmount ? s.Amount.Value : (ba != null ? ba.Amount : 0);
                        patientTotal += price;
                        string brandName = s.Brand != null ? s.Brand.Name : "";

                        if (firstRow)
                        {
                            mainTbl.AddCell(new PdfPCell(new Phrase(visitDate, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                            mainTbl.AddCell(new PdfPCell(new Phrase(patientName, boldCell)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                            mainTbl.AddCell(new PdfPCell(new Phrase(consFee == 0 ? "-" : consFee.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                            firstRow = false;
                        }
                        else
                        {
                            mainTbl.AddCell(new PdfPCell(new Phrase("", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                            mainTbl.AddCell(new PdfPCell(new Phrase("", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                            mainTbl.AddCell(new PdfPCell(new Phrase("", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                        }
                        mainTbl.AddCell(new PdfPCell(new Phrase(brandName, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                        mainTbl.AddCell(new PdfPCell(new Phrase("1", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                        mainTbl.AddCell(new PdfPCell(new Phrase(price == 0 ? "-" : price.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                    }

                    // Per-patient subtotal row
                    var subtotalBg = new BaseColor(224, 235, 252);
                    mainTbl.AddCell(new PdfPCell(new Phrase($"Total for {patientName}: {patientTotal:N2}", boldCell))
                    {
                        Colspan = 6, BackgroundColor = subtotalBg,
                        Border = Rectangle.NO_BORDER, Padding = 4,
                        HorizontalAlignment = Element.ALIGN_RIGHT
                    });
                }

                doc.Add(mainTbl);

                doc.Add(new Paragraph($"\nPrinted on: {DateTime.Now:yyyy-MM-dd hh:mm tt}", subFont));

                doc.Close();
                writer.Close();
                return File(ms.ToArray(), "application/pdf", $"SalesReport_{clinicId}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.pdf");
            }
        }

        // GET /api/stock/items-report?clinicId=X&brandId=X&from=DATE&to=DATE
        [HttpGet("items-report")]
        public async Task<IActionResult> GetItemsReport(
            [FromQuery] long clinicId,
            [FromQuery] long brandId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            string clinicName = clinic != null ? clinic.Name : $"Clinic {clinicId}";

            // All brands in scope
            List<long> brandIds;
            if (brandId > 0)
            {
                brandIds = new List<long> { brandId };
            }
            else
            {
                var soldBrands = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Child.ClinicId == clinicId && s.IsDone == true && s.BrandId != null)
                    .Select(s => s.BrandId.Value).Distinct().ToListAsync();
                var purchBrands = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BillId != null && s.Bill.ClinicId == clinicId && !s.Bill.BillNo.StartsWith("XFER-"))
                    .Select(s => s.BrandId).Distinct().ToListAsync();
                var xferBrands = await _db.StockTransfers
                    .Where(t => t.ToClinicId == clinicId || t.FromClinicId == clinicId)
                    .Select(t => t.BrandId).Distinct().ToListAsync();
                var adjBrands = await _db.AdjustStocks
                    .Where(a => a.ClinicId == clinicId)
                    .Select(a => a.BrandId).Distinct().ToListAsync();
                brandIds = soldBrands.Concat(purchBrands).Concat(xferBrands).Concat(adjBrands).Distinct().ToList();
            }

            if (brandIds.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No stock movement data for the selected period" });

            var brandsLookup = await _db.Brands.Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 30, 30, 40, 30);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(50, 50, 50));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(26, 26, 46));
                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(120, 120, 120));
                BaseColor headerBg  = new BaseColor(21, 101, 192);
                BaseColor totalsBg  = new BaseColor(230, 240, 255);
                BaseColor altBg     = new BaseColor(245, 248, 255);
                BaseColor whiteBg   = new BaseColor(255, 255, 255);
                BaseColor borderClr = new BaseColor(200, 200, 200);

                foreach (var bid in brandIds)
                {
                    string itemName = brandsLookup.TryGetValue(bid, out var n) ? n : $"Brand {bid}";

                    // Opening stock = all movement before `from` date
                    int openingStock = await ComputeStockUpTo(_db, clinicId, bid, from.Date.AddDays(-1));

                    // Fetch all events in range for this brand
                    var soldRows = await _db.Schedules
                        .Include(s => s.Child)
                        .Where(s => s.Child.ClinicId == clinicId && s.IsDone == true
                                 && s.BrandId == bid && s.GivenDate.HasValue
                                 && s.GivenDate.Value.Date >= from.Date && s.GivenDate.Value.Date <= to.Date)
                        .ToListAsync();

                    var purchRows = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BillId != null && s.Bill.ClinicId == clinicId
                                 && !s.Bill.BillNo.StartsWith("XFER-")
                                 && s.BrandId == bid
                                 && s.Bill.BillDate.Date >= from.Date && s.Bill.BillDate.Date <= to.Date)
                        .ToListAsync();

                    var xferInRows = await _db.StockTransfers
                        .Where(t => t.ToClinicId == clinicId && t.BrandId == bid
                                 && t.TransferDate.Date >= from.Date && t.TransferDate.Date <= to.Date)
                        .ToListAsync();

                    var xferOutRows = await _db.StockTransfers
                        .Where(t => t.FromClinicId == clinicId && t.BrandId == bid
                                 && t.TransferDate.Date >= from.Date && t.TransferDate.Date <= to.Date)
                        .ToListAsync();

                    var adjRows = await _db.AdjustStocks
                        .Where(a => a.ClinicId == clinicId && a.BrandId == bid
                                 && a.Date.Date >= from.Date && a.Date.Date <= to.Date)
                        .ToListAsync();

                    // Collect all active dates
                    var activeDates = soldRows.Select(s => s.GivenDate.Value.Date)
                        .Concat(purchRows.Select(p => p.Bill.BillDate.Date))
                        .Concat(xferInRows.Select(t => t.TransferDate.Date))
                        .Concat(xferOutRows.Select(t => t.TransferDate.Date))
                        .Concat(adjRows.Select(a => a.Date.Date))
                        .Distinct().OrderBy(d => d).ToList();

                    if (activeDates.Count == 0) continue;

                    // Title block
                    doc.Add(new Paragraph("ITEM REPORT", titleFont) { Alignment = Element.ALIGN_CENTER, SpacingBefore = 6 });
                    doc.Add(new Paragraph($"ITEM: {itemName}", subFont) { Alignment = Element.ALIGN_CENTER });
                    doc.Add(new Paragraph(clinicName, subFont) { Alignment = Element.ALIGN_CENTER });
                    doc.Add(new Paragraph($"FROM {from:dd-MM-yyyy}  TO  {to:dd-MM-yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 10 });

                    // Table: Date | Opening Stock | Sold | Direct Sale | Transfer | Purchased | Adjusted | Stock In Hand
                    var tbl = new PdfPTable(8) { WidthPercentage = 100, SpacingBefore = 4 };
                    tbl.SetWidths(new float[] { 1.8f, 1.4f, 1f, 1.2f, 1.2f, 1.3f, 1.2f, 1.5f });

                    string[] colHeaders = { "Date", "Opening Stock", "Sold", "Direct Sale", "Transfer", "Purchased", "Adjusted", "Stock In Hand" };
                    foreach (var h in colHeaders)
                    {
                        bool right = h != "Date";
                        tbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                        {
                            BackgroundColor = headerBg, Border = Rectangle.NO_BORDER,
                            Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                        });
                    }

                    int running = openingStock;
                    int totSold = 0, totDirectSale = 0, totTransfer = 0, totPurchased = 0, totAdjusted = 0;
                    bool alt = false;

                    foreach (var d in activeDates)
                    {
                        int dayOpening   = running;
                        int sold         = soldRows.Count(s => s.GivenDate.Value.Date == d);
                        int directSale   = 0; // placeholder — direct sale not yet in model
                        int purchased    = purchRows.Where(p => p.Bill.BillDate.Date == d).Sum(p => p.Quantity);
                        int xferNet      = xferInRows.Where(t => t.TransferDate.Date == d).Sum(t => t.Quantity)
                                         - xferOutRows.Where(t => t.TransferDate.Date == d).Sum(t => t.Quantity);
                        int adjusted     = adjRows.Where(a => a.Date.Date == d).Sum(a => a.Adjustment);
                        int closing      = dayOpening - sold - directSale + xferNet + purchased + adjusted;

                        totSold       += sold;
                        totDirectSale += directSale;
                        totTransfer   += xferNet;
                        totPurchased  += purchased;
                        totAdjusted   += adjusted;
                        running        = closing;

                        var bg = alt ? altBg : whiteBg;
                        alt = !alt;

                        tbl.AddCell(Cell(d.ToString("dd-MM-yyyy"), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(dayOpening.ToString(), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(sold.ToString(), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(directSale.ToString(), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(xferNet.ToString(), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(purchased.ToString(), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(adjusted.ToString(), cellFont, bg, borderClr));
                        tbl.AddCell(CellR(closing.ToString(), boldCell, bg, borderClr));
                    }

                    // Totals row
                    tbl.AddCell(new PdfPCell(new Phrase("Totals", boldCell)) { BackgroundColor = totalsBg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4 });
                    tbl.AddCell(CellR(openingStock.ToString(), boldCell, totalsBg, borderClr));
                    tbl.AddCell(CellR(totSold.ToString(), boldCell, totalsBg, borderClr));
                    tbl.AddCell(CellR(totDirectSale.ToString(), boldCell, totalsBg, borderClr));
                    tbl.AddCell(CellR(totTransfer.ToString(), boldCell, totalsBg, borderClr));
                    tbl.AddCell(CellR(totPurchased.ToString(), boldCell, totalsBg, borderClr));
                    tbl.AddCell(CellR(totAdjusted.ToString(), boldCell, totalsBg, borderClr));
                    tbl.AddCell(CellR(running.ToString(), boldCell, totalsBg, borderClr));

                    doc.Add(tbl);
                    doc.Add(new Paragraph($"\nPrinted on: {DateTime.Now:yyyy-MM-dd hh:mm tt}", footerFont) { Alignment = Element.ALIGN_CENTER, SpacingBefore = 8 });

                    if (bid != brandIds.Last()) doc.NewPage();
                }

                doc.Close();
                writer.Close();
                string fname = brandId > 0
                    ? $"ItemsReport_{clinicId}_{brandId}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.pdf"
                    : $"ItemsReport_{clinicId}_All_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.pdf";
                return File(ms.ToArray(), "application/pdf", fname);
            }
        }

        private static PdfPCell Cell(string text, Font font, BaseColor bg, BaseColor border) =>
            new PdfPCell(new Phrase(text, font)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = border, Padding = 4, HorizontalAlignment = Element.ALIGN_LEFT };

        private static PdfPCell CellR(string text, Font font, BaseColor bg, BaseColor border) =>
            new PdfPCell(new Phrase(text, font)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = border, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT };

        private static async Task<int> ComputeStockUpTo(Context db, long clinicId, long brandId, DateTime upTo)
        {
            int purchased = await db.Stocks
                .Include(s => s.Bill)
                .Where(s => s.BillId != null && s.Bill.ClinicId == clinicId
                         && !s.Bill.BillNo.StartsWith("XFER-")
                         && s.BrandId == brandId && s.Bill.BillDate.Date <= upTo.Date)
                .SumAsync(s => (int?)s.Quantity) ?? 0;

            int sold = await db.Schedules
                .Include(s => s.Child)
                .Where(s => s.Child.ClinicId == clinicId && s.IsDone == true
                         && s.BrandId == brandId && s.GivenDate.HasValue
                         && s.GivenDate.Value.Date <= upTo.Date)
                .CountAsync();

            int xferIn = await db.StockTransfers
                .Where(t => t.ToClinicId == clinicId && t.BrandId == brandId && t.TransferDate.Date <= upTo.Date)
                .SumAsync(t => (int?)t.Quantity) ?? 0;

            int xferOut = await db.StockTransfers
                .Where(t => t.FromClinicId == clinicId && t.BrandId == brandId && t.TransferDate.Date <= upTo.Date)
                .SumAsync(t => (int?)t.Quantity) ?? 0;

            int adjusted = await db.AdjustStocks
                .Where(a => a.ClinicId == clinicId && a.BrandId == brandId && a.Date.Date <= upTo.Date)
                .SumAsync(a => (int?)a.Adjustment) ?? 0;

            return purchased + xferIn - sold - xferOut + adjusted;
        }

        // GET /api/stock/items-purchase-report?clinicId=X&brandId=X&from=DATE&to=DATE
        [HttpGet("items-purchase-report")]
        public async Task<IActionResult> GetItemsPurchaseReport(
            [FromQuery] long clinicId,
            [FromQuery] long brandId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            string clinicName = clinic != null ? clinic.Name : $"Clinic {clinicId}";

            var query = _db.Stocks
                .Include(s => s.Bill)
                .Include(s => s.Brand)
                .Where(s => s.BillId != null
                         && s.Bill.ClinicId == clinicId
                         && !s.Bill.BillNo.StartsWith("XFER-")
                         && s.Bill.BillDate.Date >= from.Date
                         && s.Bill.BillDate.Date <= to.Date);
            if (brandId > 0) query = query.Where(s => s.BrandId == brandId);
            var lines = await query.OrderBy(s => s.Bill.BillDate).ThenBy(s => s.Bill.BillNo).ToListAsync();

            if (lines.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No purchase data for the selected period" });

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 30, 30, 40, 30);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(26, 26, 46));

                doc.Add(new Paragraph("Item Purchase Report", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(clinicName, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"{from:dd MMM yyyy}  –  {to:dd MMM yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });

                var tbl = new PdfPTable(7) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(new float[] { 1.8f, 1.4f, 2f, 2f, 1f, 1.4f, 1.4f });
                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Bill No", "Date", "Supplier", "Brand", "Qty", "Unit Price", "Total" };
                foreach (var h in headers)
                {
                    bool right = h == "Qty" || h == "Unit Price" || h == "Total";
                    tbl.AddCell(new PdfPCell(new Phrase(h, headerFont)) { BackgroundColor = headerBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT });
                }

                bool alt = false;
                decimal grandTotal = 0;
                foreach (var s in lines)
                {
                    var bg = alt ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                    alt = !alt;
                    decimal lineTotal = s.StockAmount * s.OriginalQuantity;
                    grandTotal += lineTotal;

                    tbl.AddCell(new PdfPCell(new Phrase(s.Bill.BillNo, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.Bill.BillDate.ToString("dd MMM yyyy"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.Bill.Supplier ?? "", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.Brand != null ? s.Brand.Name : "", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.OriginalQuantity.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(s.StockAmount.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(lineTotal.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                }

                tbl.AddCell(new PdfPCell(new Phrase("Grand Total", boldCell)) { Colspan = 6, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                tbl.AddCell(new PdfPCell(new Phrase(grandTotal.ToString("N2"), boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                doc.Add(tbl);
                doc.Close();
                writer.Close();
                return File(ms.ToArray(), "application/pdf", $"PurchaseReport-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf");
            }
        }

        // GET /api/stock/items-supplier-report?clinicId=X&supplier=X&from=DATE&to=DATE
        [HttpGet("items-supplier-report")]
        public async Task<IActionResult> GetItemsSupplierReport(
            [FromQuery] long clinicId,
            [FromQuery] string supplier,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            string clinicName = clinic != null ? clinic.Name : $"Clinic {clinicId}";

            var bills = await _db.Bills
                .Include(b => b.Stocks).ThenInclude(s => s.Brand)
                .Where(b => b.ClinicId == clinicId
                         && !b.BillNo.StartsWith("XFER-")
                         && b.BillDate.Date >= from.Date
                         && b.BillDate.Date <= to.Date
                         && b.Supplier.Contains(supplier))
                .OrderBy(b => b.Supplier)
                .ThenBy(b => b.BillDate)
                .ToListAsync();

            if (bills.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No purchase data for the selected supplier and period" });

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 30, 30, 40, 30);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, new BaseColor(21, 101, 192));
                var subFont     = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(84, 110, 122));
                var headerFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var cellFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(26, 26, 46));
                var boldCell    = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(26, 26, 46));
                var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, new BaseColor(21, 101, 192));

                doc.Add(new Paragraph("Supplier Purchase Report", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(clinicName, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"{from:dd MMM yyyy}  –  {to:dd MMM yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });

                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Bill No", "Date", "Brand", "Batch", "Qty", "Unit Price", "Total" };
                float[] widths = { 1.8f, 1.4f, 2f, 1.6f, 0.8f, 1.4f, 1.4f };

                decimal grandTotal = 0;
                var grouped = bills.GroupBy(b => b.Supplier);

                foreach (var grp in grouped)
                {
                    doc.Add(new Paragraph($"Supplier: {grp.Key}", sectionFont) { SpacingBefore = 8, SpacingAfter = 4 });

                    var tbl = new PdfPTable(7) { WidthPercentage = 100 };
                    tbl.SetWidths(widths);
                    foreach (var h in headers)
                    {
                        bool right = h == "Qty" || h == "Unit Price" || h == "Total";
                        tbl.AddCell(new PdfPCell(new Phrase(h, headerFont)) { BackgroundColor = headerBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT });
                    }

                    bool alt = false;
                    decimal supplierTotal = 0;
                    foreach (var bill in grp)
                    {
                        foreach (var s in bill.Stocks)
                        {
                            var bg = alt ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                            alt = !alt;
                            decimal lineTotal = s.StockAmount * s.OriginalQuantity;
                            supplierTotal += lineTotal;

                            tbl.AddCell(new PdfPCell(new Phrase(bill.BillNo, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                            tbl.AddCell(new PdfPCell(new Phrase(bill.BillDate.ToString("dd MMM yyyy"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                            tbl.AddCell(new PdfPCell(new Phrase(s.Brand != null ? s.Brand.Name : "", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                            tbl.AddCell(new PdfPCell(new Phrase(s.BatchLot ?? "—", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                            tbl.AddCell(new PdfPCell(new Phrase(s.OriginalQuantity.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                            tbl.AddCell(new PdfPCell(new Phrase(s.StockAmount.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                            tbl.AddCell(new PdfPCell(new Phrase(lineTotal.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                        }
                    }
                    tbl.AddCell(new PdfPCell(new Phrase($"Supplier Total", boldCell)) { Colspan = 6, Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(supplierTotal.ToString("N2"), boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    doc.Add(tbl);
                    grandTotal += supplierTotal;
                }

                var totTbl = new PdfPTable(2) { WidthPercentage = 40, HorizontalAlignment = Element.ALIGN_RIGHT, SpacingBefore = 8 };
                totTbl.SetWidths(new float[] { 1.5f, 1f });
                totTbl.AddCell(new PdfPCell(new Phrase("Grand Total", boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                totTbl.AddCell(new PdfPCell(new Phrase(grandTotal.ToString("N2"), boldCell)) { Border = Rectangle.NO_BORDER, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                doc.Add(totTbl);

                doc.Close();
                writer.Close();
                return File(ms.ToArray(), "application/pdf", $"SupplierReport-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf");
            }
        }
    // Returns the vaccination fee for a patient visit.
    // Primary: InvoiceSubmission (written after the 2026-05-30 fix)
    // Fallback: Fee table (written when any invoice PDF is generated — works for all historical invoices)
    private static decimal ResolveVaccinationFee(
        long childId, DateTime visitDate,
        List<InvoiceSubmission> invoiceSubs,
        List<Invoice> invoiceRecords,
        List<Fee> feeRecords)
    {
        var sub = invoiceSubs.FirstOrDefault(x =>
            x.ChildId == childId && x.InvoiceDate.Date == visitDate.Date);
        if (sub != null && sub.ConsultationFee != 0)
            return sub.ConsultationFee;

        // Fallback: find Invoice rows for this child, get their Fee records
        var childInvoiceIds = invoiceRecords
            .Where(i => i.ChildId == childId)
            .Select(i => i.InvoiceId)
            .ToList();
        var fee = feeRecords.FirstOrDefault(f => childInvoiceIds.Contains(f.InvoiceId));
        return fee != null ? fee.Amount : 0;
    }
    }
}
