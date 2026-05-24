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

            // Purchased (from purchase bills, not XFER)
            var purchaseQuery = _db.Stocks
                .Include(s => s.Bill)
                .Include(s => s.Brand)
                .Where(s => s.BillId != null
                         && s.Bill.ClinicId == clinicId
                         && !s.Bill.BillNo.StartsWith("XFER-")
                         && s.Bill.BillDate.Date >= from.Date
                         && s.Bill.BillDate.Date <= to.Date);
            if (brandId > 0) purchaseQuery = purchaseQuery.Where(s => s.BrandId == brandId);
            var purchases = await purchaseQuery.ToListAsync();

            // Given to patients
            var scheduleQuery = _db.Schedules
                .Include(s => s.Brand)
                .Include(s => s.Child)
                .Where(s => s.Child.ClinicId == clinicId
                         && s.IsDone == true
                         && s.BrandId != null
                         && s.GivenDate.HasValue
                         && s.GivenDate.Value.Date >= from.Date
                         && s.GivenDate.Value.Date <= to.Date);
            if (brandId > 0) scheduleQuery = scheduleQuery.Where(s => s.BrandId == brandId);
            var given = await scheduleQuery.ToListAsync();

            // Transferred in
            var xferInQuery = _db.StockTransfers
                .Include(t => t.Brand)
                .Where(t => t.ToClinicId == clinicId
                         && t.TransferDate.Date >= from.Date
                         && t.TransferDate.Date <= to.Date);
            if (brandId > 0) xferInQuery = xferInQuery.Where(t => t.BrandId == brandId);
            var xferIn = await xferInQuery.ToListAsync();

            // Transferred out
            var xferOutQuery = _db.StockTransfers
                .Include(t => t.Brand)
                .Where(t => t.FromClinicId == clinicId
                         && t.TransferDate.Date >= from.Date
                         && t.TransferDate.Date <= to.Date);
            if (brandId > 0) xferOutQuery = xferOutQuery.Where(t => t.BrandId == brandId);
            var xferOut = await xferOutQuery.ToListAsync();

            // Losses (negative adjustments)
            var lossQuery = _db.AdjustStocks
                .Include(a => a.Brand)
                .Where(a => a.ClinicId == clinicId
                         && a.Adjustment < 0
                         && a.Date.Date >= from.Date
                         && a.Date.Date <= to.Date);
            if (brandId > 0) lossQuery = lossQuery.Where(a => a.BrandId == brandId);
            var losses = await lossQuery.ToListAsync();

            // Collect all brand IDs involved
            var allBrandIds = purchases.Select(p => p.BrandId)
                .Concat(given.Where(s => s.BrandId.HasValue).Select(s => s.BrandId.Value))
                .Concat(xferIn.Select(t => t.BrandId))
                .Concat(xferOut.Select(t => t.BrandId))
                .Concat(losses.Select(a => a.BrandId))
                .Distinct().ToList();

            if (allBrandIds.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No stock movement data for the selected period" });

            var brands = await _db.Brands.Where(b => allBrandIds.Contains(b.Id)).ToListAsync();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 30, 30, 40, 30);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(26, 26, 46));

                doc.Add(new Paragraph("Item Movement Report", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(clinicName, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"{from:dd MMM yyyy}  –  {to:dd MMM yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });

                var tbl = new PdfPTable(7) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(new float[] { 2.5f, 1.2f, 1.2f, 1.4f, 1.4f, 1.2f, 1.2f });
                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Brand", "Purchased", "Given", "Transferred In", "Transferred Out", "Lost", "Net Movement" };
                foreach (var h in headers)
                {
                    bool right = h != "Brand";
                    tbl.AddCell(new PdfPCell(new Phrase(h, headerFont)) { BackgroundColor = headerBg, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT });
                }

                bool alt = false;
                foreach (var bid in allBrandIds)
                {
                    var br = brands.FirstOrDefault(b => b.Id == bid);
                    string bName = br != null ? br.Name : $"Brand {bid}";
                    int purchased = purchases.Where(p => p.BrandId == bid).Sum(p => p.Quantity);
                    int givenQty  = given.Where(s => s.BrandId == bid).Count();
                    int tIn       = xferIn.Where(t => t.BrandId == bid).Sum(t => t.Quantity);
                    int tOut      = xferOut.Where(t => t.BrandId == bid).Sum(t => t.Quantity);
                    int lost      = Math.Abs(losses.Where(a => a.BrandId == bid).Sum(a => a.Adjustment));
                    int net       = purchased + tIn - givenQty - tOut - lost;

                    var bg = alt ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                    alt = !alt;

                    Font netFont = net < 0 ? FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(198, 40, 40)) : boldCell;

                    tbl.AddCell(new PdfPCell(new Phrase(bName, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(purchased.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(givenQty.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(tIn.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(tOut.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(lost.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase((net >= 0 ? "+" : "") + net.ToString(), netFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                }
                doc.Add(tbl);
                doc.Close();
                writer.Close();
                string fname = brandId > 0 ? $"ItemReport-Brand{brandId}-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf" : $"ItemReport-All-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf";
                return File(ms.ToArray(), "application/pdf", fname);
            }
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
    }
}
