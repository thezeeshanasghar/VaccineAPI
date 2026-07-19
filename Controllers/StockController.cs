using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using VaccineAPI.helper;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockController : ControllerBase
    {
        private readonly Context _db;
        private readonly VaccineAPI.Services.InventoryTransactionService _inventory;
        public StockController(Context db, VaccineAPI.Services.InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        // Report endpoints expose a clinic's patients + revenue by clinicId. The API has no auth
        // scheme, so the caller asserts its own doctorId and we enforce that it owns the clinic
        // (or, for a PA, is assigned to it via PaAccess). This stops trivial clinicId enumeration.
        // Returns true when access is allowed. doctorId <= 0 is treated as "not supplied" and
        // rejected so the check can't be bypassed by omitting the param.
        private async Task<bool> CallerOwnsClinicAsync(Clinic clinic, long doctorId)
        {
            if (clinic == null) return false;
            if (doctorId > 0 && clinic.DoctorId == doctorId) return true;
            // PA path: allow if the passed id belongs to a PA assigned to this clinic.
            return await _db.PaAccess
                .AnyAsync(a => a.ClinicId == clinic.Id && a.PersonalAssistantId == doctorId);
        }

        // POST /api/stock/opening-balance
        // v2 — record physical on-hand at the reset. Body: doctorId, clinicId, and a list of
        // { brandId, quantity, unitCost?, batchLot?, expiry? }. Each line becomes a real batch +
        // an OpeningBalance ledger row dated at the clinic's StockPeriodStart, so the ledger equals
        // reality from day one. Idempotency is the caller's job — running it twice doubles stock.
        [HttpPost("opening-balance")]
        public async Task<IActionResult> PostOpeningBalance([FromBody] OpeningBalanceDTO dto)
        {
            if (dto == null || dto.Lines == null || dto.Lines.Count == 0)
                return Ok(new { IsSuccess = false, Message = "No opening-balance lines provided." });

            var clinic = await _db.Clinics.FindAsync(dto.ClinicId);
            if (clinic == null)
                return Ok(new { IsSuccess = false, Message = "Clinic not found." });
            if (!clinic.StockPeriodStart.HasValue)
                return Ok(new { IsSuccess = false, Message = "This clinic has no stock reset date (StockPeriodStart) set; opening balance can only be recorded against a reset." });

            DateTime eventDate = clinic.StockPeriodStart.Value.Date;

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                int posted = 0;
                var errors = new System.Collections.Generic.List<string>();
                foreach (var line in dto.Lines)
                {
                    if (line.Quantity <= 0) continue;
                    var res = await _inventory.PostOpeningBalance(dto.DoctorId, dto.ClinicId,
                        line.BrandId, line.Quantity, line.UnitCost ?? 0m, line.BatchLot, line.Expiry, eventDate);
                    if (res.IsSuccess) posted++;
                    else errors.Add($"Brand {line.BrandId}: {res.Message}");
                }
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    IsSuccess = posted > 0,
                    Message = posted > 0
                        ? $"Recorded opening balance for {posted} item(s)" + (errors.Count > 0 ? $"; {errors.Count} skipped." : ".")
                        : "No opening balance recorded. " + string.Join(" ", errors),
                    PostedCount = posted,
                    Errors = errors
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Failed to record opening balance: " + ex.Message });
            }
        }

        // GET /api/stock/integrity?clinicId=X
        // v2 §7 — drift audit. The counter (BrandAmount.Count) is a transactional cache; the
        // ledger (Σ InventoryTransaction.QuantityDelta on/after StockPeriodStart) is truth. This
        // reports every brand where the two disagree, so drift is caught the moment it appears.
        // Read-only; run it anytime, especially right after the reset to confirm a clean slate.
        [HttpGet("integrity")]
        public async Task<IActionResult> CheckIntegrity([FromQuery] long clinicId)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null)
                return Ok(new { IsSuccess = false, Message = "Clinic not found." });

            DateTime floor = clinic.StockPeriodStart?.Date ?? DateTime.MinValue;

            var counters = await _db.BrandAmounts
                .Include(b => b.Brand)
                .Where(b => b.ClinicId == clinicId)
                .Select(b => new { b.BrandId, BrandName = b.Brand.Name, b.Count, b.NeedsReconcile })
                .ToListAsync();

            // Ledger balance per brand for this clinic, floored at the reset.
            var ledger = await _db.InventoryTransactions
                .Where(t => t.ClinicId == clinicId && t.EventDate.Date >= floor)
                .GroupBy(t => t.BrandId)
                .Select(g => new { BrandId = g.Key, Balance = g.Sum(x => (int?)x.QuantityDelta) ?? 0 })
                .ToDictionaryAsync(x => x.BrandId, x => x.Balance);

            var mismatches = new System.Collections.Generic.List<object>();
            foreach (var c in counters)
            {
                int ledgerBal = ledger.TryGetValue(c.BrandId, out var v) ? v : 0;
                if (ledgerBal != c.Count || c.NeedsReconcile)
                {
                    mismatches.Add(new
                    {
                        c.BrandId,
                        c.BrandName,
                        CounterCount = c.Count,
                        LedgerBalance = ledgerBal,
                        Drift = c.Count - ledgerBal,
                        c.NeedsReconcile
                    });
                }
            }

            return Ok(new
            {
                IsSuccess = true,
                ClinicId = clinicId,
                StockPeriodStart = clinic.StockPeriodStart,
                IsClean = mismatches.Count == 0,
                MismatchCount = mismatches.Count,
                Mismatches = mismatches
            });
        }

        // POST /api/stock/reconcile?clinicId=X[&brandId=Y]
        // v2 §7 — repair. Rewrites BrandAmount.Count from the ledger (Σ QuantityDelta floored at
        // StockPeriodStart) and clears NeedsReconcile. This is the ONLY sanctioned way to overwrite
        // a counter: it makes the cache match truth, it never invents stock. Scope to one brand via
        // brandId, or omit to reconcile the whole clinic. Doctor-triggered maintenance.
        [HttpPost("reconcile")]
        public async Task<IActionResult> Reconcile([FromQuery] long clinicId, [FromQuery] long brandId = 0)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null)
                return Ok(new { IsSuccess = false, Message = "Clinic not found." });

            DateTime floor = clinic.StockPeriodStart?.Date ?? DateTime.MinValue;

            var counters = await _db.BrandAmounts
                .Where(b => b.ClinicId == clinicId && (brandId == 0 || b.BrandId == brandId))
                .ToListAsync();
            if (counters.Count == 0)
                return Ok(new { IsSuccess = false, Message = "No stock counters found for this clinic/brand." });

            var ledger = await _db.InventoryTransactions
                .Where(t => t.ClinicId == clinicId && t.EventDate.Date >= floor
                         && (brandId == 0 || t.BrandId == brandId))
                .GroupBy(t => t.BrandId)
                .Select(g => new { BrandId = g.Key, Balance = g.Sum(x => (int?)x.QuantityDelta) ?? 0 })
                .ToDictionaryAsync(x => x.BrandId, x => x.Balance);

            int fixedCount = 0;
            foreach (var c in counters)
            {
                int ledgerBal = ledger.TryGetValue(c.BrandId, out var v) ? v : 0;
                if (c.Count != ledgerBal || c.NeedsReconcile)
                {
                    c.Count = ledgerBal;
                    c.NeedsReconcile = false;
                    fixedCount++;
                }
            }
            await _db.SaveChangesAsync();

            return Ok(new
            {
                IsSuccess = true,
                Message = fixedCount == 0 ? "Already reconciled — no drift." : $"Reconciled {fixedCount} item(s) to the ledger.",
                ReconciledCount = fixedCount
            });
        }

        // GET /api/stock/batch-lots?brandId=X&clinicId=Y
        [HttpGet("batch-lots")]
        public async Task<IActionResult> GetBatchLots([FromQuery] long brandId, [FromQuery] long clinicId)
        {
            var stocks = await _db.Stocks
                .Include(s => s.Bill)
                // v2: include opening-balance/transfer batches (Stock.ClinicId set, BillId NULL)
                // as well as purchase batches (Bill.ClinicId). Same resolution FEFO uses.
                .Where(s => s.BrandId == brandId && s.Quantity > 0
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
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
            [FromQuery] long doctorId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (!await CallerOwnsClinicAsync(clinic, doctorId))
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });
            string clinicName = clinic.Name;

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
                return File(ms.ToArray(), "application/pdf", ReportFileName.Build("SalesReport", clinicName));
            }
        }

        // GET /api/stock/sales-collection-report?clinicId=X&from=DATE&to=DATE
        [HttpGet("sales-collection-report")]
        public async Task<IActionResult> GetSalesCollectionReport(
            [FromQuery] long clinicId,
            [FromQuery] long doctorId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (!await CallerOwnsClinicAsync(clinic, doctorId))
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });

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

            // Fallback prices from BrandAmounts
            var brandIds = schedules.Where(s => s.BrandId.HasValue).Select(s => s.BrandId.Value).Distinct().ToList();
            var brandAmounts = await _db.BrandAmounts
                .Where(b => b.ClinicId == clinicId && brandIds.Contains(b.BrandId))
                .ToListAsync();

            // Vaccination fees — primary: InvoiceSubmissions; fallback: Fee table via Invoice
            var childIds = schedules.Select(s => s.ChildId).Distinct().ToList();
            long ownerDoctorId = clinic.DoctorId;

            var invoiceSubs = await _db.InvoiceSubmissions
                .Where(x => x.DoctorId == ownerDoctorId
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

            // Direct (walk-in) sales
            var directSales = await _db.DirectSales
                .Include(d => d.Brand)
                .Where(d => d.ClinicId == clinicId
                         && d.SaleDate.Date >= from.Date
                         && d.SaleDate.Date <= to.Date)
                .OrderBy(d => d.SaleDate)
                .ThenBy(d => d.ClientName)
                .ToListAsync();

            if (schedules.Count == 0 && directSales.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No sales data for the selected period" });

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

                var titleFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, new BaseColor(21, 101, 192));
                var subFont     = FontFactory.GetFont(FontFactory.HELVETICA, 9,  new BaseColor(84, 110, 122));
                var headerFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8,  new BaseColor(255, 255, 255));
                var cellFont    = FontFactory.GetFont(FontFactory.HELVETICA, 8,  new BaseColor(26, 26, 46));
                var boldCell    = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8,  new BaseColor(26, 26, 46));
                var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9,  new BaseColor(21, 101, 192));

                doc.Add(new Paragraph("Sales Report", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(clinic.Name, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"FROM {from:dd-MM-yyyy} TO {to:dd-MM-yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 6 });

                // Pre-calculate totals
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
                foreach (var ds in directSales)
                    totalItemsPrice += ds.TotalSaleValue;

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

                BaseColor headerBg = new BaseColor(21, 101, 192);
                float[] colWidths = { 1.4f, 2.2f, 1.4f, 2.2f, 0.6f, 1.2f };
                string[] colHeaders = { "Date", "Patient / Client", "Vaccination Fee", "Item", "Qty", "Price" };

                var mainTbl = new PdfPTable(6) { WidthPercentage = 100, SpacingBefore = 4 };
                mainTbl.SetWidths(colWidths);
                foreach (var h in colHeaders)
                {
                    bool right = h == "Vaccination Fee" || h == "Qty" || h == "Price";
                    mainTbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                    {
                        BackgroundColor = headerBg, Border = Rectangle.NO_BORDER,
                        Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                    });
                }

                // --- Patient vaccination rows ---
                if (patientVisits.Count > 0)
                {
                    mainTbl.AddCell(new PdfPCell(new Phrase("Patient Vaccinations", sectionFont))
                    {
                        Colspan = 6, BackgroundColor = new BaseColor(232, 240, 254),
                        Border = Rectangle.NO_BORDER, Padding = 4
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

                    var subtotalBg = new BaseColor(224, 235, 252);
                    mainTbl.AddCell(new PdfPCell(new Phrase($"Total for {patientName}: {patientTotal:N2}", boldCell))
                    {
                        Colspan = 6, BackgroundColor = subtotalBg,
                        Border = Rectangle.NO_BORDER, Padding = 4,
                        HorizontalAlignment = Element.ALIGN_RIGHT
                    });
                }

                // --- Direct (walk-in) sales rows ---
                if (directSales.Count > 0)
                {
                    mainTbl.AddCell(new PdfPCell(new Phrase("Direct / Walk-in Sales", sectionFont))
                    {
                        Colspan = 6, BackgroundColor = new BaseColor(232, 240, 254),
                        Border = Rectangle.NO_BORDER, Padding = 4, PaddingTop = 10
                    });

                    bool altDs = false;
                    foreach (var ds in directSales)
                    {
                        var bg = altDs ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                        altDs = !altDs;
                        string dsDate  = ds.SaleDate.ToString("dd-MM-yyyy");
                        string dsName  = !string.IsNullOrWhiteSpace(ds.ClientName) ? ds.ClientName : "Walk-in";
                        string dsBrand = ds.Brand != null ? ds.Brand.Name : "";

                        mainTbl.AddCell(new PdfPCell(new Phrase(dsDate, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                        mainTbl.AddCell(new PdfPCell(new Phrase(dsName, boldCell)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                        mainTbl.AddCell(new PdfPCell(new Phrase("-", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                        mainTbl.AddCell(new PdfPCell(new Phrase(dsBrand, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3 });
                        mainTbl.AddCell(new PdfPCell(new Phrase(ds.Quantity.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                        mainTbl.AddCell(new PdfPCell(new Phrase(ds.TotalSaleValue.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                    }

                    var dsTotalBg = new BaseColor(224, 235, 252);
                    decimal dsTotalAmt = directSales.Sum(d => d.TotalSaleValue);
                    mainTbl.AddCell(new PdfPCell(new Phrase($"Total Direct Sales: {dsTotalAmt:N2}", boldCell))
                    {
                        Colspan = 6, BackgroundColor = dsTotalBg,
                        Border = Rectangle.NO_BORDER, Padding = 4,
                        HorizontalAlignment = Element.ALIGN_RIGHT
                    });
                }

                doc.Add(mainTbl);

                doc.Add(new Paragraph($"\nPrinted on: {DateTime.Now:yyyy-MM-dd hh:mm tt}", subFont));

                doc.Close();
                writer.Close();
                return File(ms.ToArray(), "application/pdf", ReportFileName.Build("SalesCollectionReport", clinic.Name));
            }
        }

        // GET /api/stock/items-report?clinicId=X&brandId=X&from=DATE&to=DATE
        [HttpGet("items-report")]
        public async Task<IActionResult> GetItemsReport(
            [FromQuery] long clinicId,
            [FromQuery] long doctorId,
            [FromQuery] long brandId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (!await CallerOwnsClinicAsync(clinic, doctorId))
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });
            string clinicName = clinic.Name;

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
                var directSaleBrands = await _db.DirectSales
                    .Where(d => d.ClinicId == clinicId)
                    .Select(d => d.BrandId).Distinct().ToListAsync();
                brandIds = soldBrands.Concat(purchBrands).Concat(xferBrands).Concat(adjBrands).Concat(directSaleBrands).Distinct().ToList();
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

                // v2 §7: reset floor. Only ledger rows on/after StockPeriodStart count, so a stock
                // reset yields a clean opening with no pre-reset history double-counted.
                DateTime? stockPeriodStart = clinic?.StockPeriodStart;
                DateTime floorDate = stockPeriodStart?.Date ?? DateTime.MinValue;
                // Effective window is clamped to the floor — nothing before the reset is reportable.
                DateTime effFrom = from.Date < floorDate ? floorDate : from.Date;

                foreach (var bid in brandIds)
                {
                    string itemName = brandsLookup.TryGetValue(bid, out var n) ? n : $"Brand {bid}";

                    // Opening = ledger balance the day before the window opens, floored at the reset.
                    int openingStock = await ComputeStockUpTo(_db, clinicId, bid, effFrom.AddDays(-1), stockPeriodStart);

                    // All ledger event dates in range for this brand (single source — no 6-table union).
                    var activeDates = await _db.InventoryTransactions
                        .Where(t => t.ClinicId == clinicId && t.BrandId == bid
                                 && t.EventDate.Date >= effFrom && t.EventDate.Date <= to.Date
                                 && t.QuantityDelta != 0)
                        .Select(t => t.EventDate.Date)
                        .Distinct().OrderBy(d => d).ToListAsync();

                    if (activeDates.Count == 0) continue;

                    // Title block
                    doc.Add(new Paragraph("ITEM REPORT", titleFont) { Alignment = Element.ALIGN_CENTER, SpacingBefore = 6 });
                    doc.Add(new Paragraph($"ITEM: {itemName}", subFont) { Alignment = Element.ALIGN_CENTER });
                    doc.Add(new Paragraph(clinicName, subFont) { Alignment = Element.ALIGN_CENTER });
                    doc.Add(new Paragraph($"FROM {effFrom:dd-MM-yyyy}  TO  {to:dd-MM-yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 10 });

                    if (stockPeriodStart.HasValue && from.Date < floorDate)
                    {
                        doc.Add(new Paragraph(
                            $"Note: Stock tracking for this clinic starts {stockPeriodStart.Value:dd-MM-yyyy}. " +
                            $"Figures before this date are not shown.",
                            footerFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 8 });
                    }

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
                        var mv = await ComputeDayMovement(_db, clinicId, bid, d);
                        int sold       = mv.Sold;
                        int directSale = mv.DirectSale;
                        int xferNet    = mv.Transfer;
                        int purchased  = mv.Purchased;
                        int adjusted   = mv.Adjusted;

                        var bg = alt ? altBg : whiteBg;
                        alt = !alt;

                        tbl.AddCell(Cell(d.ToString("dd-MM-yyyy"), cellFont, bg, borderClr));

                        totSold       += sold;
                        totDirectSale += directSale;
                        totTransfer   += xferNet;
                        totPurchased  += purchased;
                        totAdjusted   += adjusted;

                        int dayOpening = running;
                        // Closing == running ledger balance through this day (all signs already
                        // baked into QuantityDelta): opening - sold - directSale + xferNet + purchased + adjusted.
                        int closing    = dayOpening - sold - directSale + xferNet + purchased + adjusted;
                        running        = closing;

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
                string itemsReportType = brandId > 0 && brandsLookup.ContainsKey(brandId)
                    ? $"ItemStockReport-{brandsLookup[brandId]}"
                    : "ItemStockReport-AllBrands";
                string fname = ReportFileName.Build(itemsReportType, clinicName);
                return File(ms.ToArray(), "application/pdf", fname);
            }
        }

        private static PdfPCell Cell(string text, Font font, BaseColor bg, BaseColor border) =>
            new PdfPCell(new Phrase(text, font)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = border, Padding = 4, HorizontalAlignment = Element.ALIGN_LEFT };

        private static PdfPCell CellR(string text, Font font, BaseColor bg, BaseColor border) =>
            new PdfPCell(new Phrase(text, font)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = border, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT };

        // v2 §7 — LEDGER-ONLY balance. Sums signed InventoryTransaction.QuantityDelta for the
        // brand+clinic up to (and including) `upTo`. This is the SINGLE source of truth: purchases
        // (+), gives (-1), ungives (+1), transfers (±), direct sales (-), adjustments (±) all land
        // here as one signed number, so there is no 6-table drift to reconcile. Give/ungive rows
        // that recorded a clinical fact without moving stock (OHF / historical / pre-period) carry
        // QuantityDelta = 0, so they are automatically excluded from the balance.
        //
        // `floor` is the clinic's StockPeriodStart: only movements on/after it count, so a stock
        // reset gives every item a clean opening of 0 with no pre-reset history bleeding through
        // (this is what fixes the reset double-count). Passing DateTime.MinValue means "all time".
        // Filters on EventDate (the business date), never CreatedAt (UTC audit stamp).
        private static async Task<int> ComputeStockUpTo(Context db, long clinicId, long brandId, DateTime upTo, DateTime? floor = null)
        {
            DateTime floorDate = floor?.Date ?? DateTime.MinValue;

            return await db.InventoryTransactions
                .Where(t => t.ClinicId == clinicId && t.BrandId == brandId
                         && t.EventDate.Date <= upTo.Date
                         && t.EventDate.Date >= floorDate)
                .SumAsync(t => (int?)t.QuantityDelta) ?? 0;
        }

        // Movement totals for a single day, straight from the ledger, split by direction so the
        // report's per-column figures (Sold / Direct Sale / Transfer / Purchased / Adjusted) stay
        // reconcilable against the opening/closing balance. Signs match the ledger convention.
        private static async Task<DayMovement> ComputeDayMovement(Context db, long clinicId, long brandId, DateTime day)
        {
            var rows = await db.InventoryTransactions
                .Where(t => t.ClinicId == clinicId && t.BrandId == brandId && t.EventDate.Date == day.Date)
                .Select(t => new { t.SourceType, t.QuantityDelta })
                .ToListAsync();

            var m = new DayMovement();
            foreach (var r in rows)
            {
                switch (r.SourceType)
                {
                    case InventoryTransactionType.Administer:      // -1 per dose (0 if no-deduct)
                    case InventoryTransactionType.Unadminister:    // +1 restore
                        m.Sold -= r.QuantityDelta;                 // report shows "Sold" as a positive count
                        break;
                    case InventoryTransactionType.DirectSale:
                    case InventoryTransactionType.DirectSaleReverse:
                        m.DirectSale -= r.QuantityDelta;
                        break;
                    case InventoryTransactionType.TransferIn:
                    case InventoryTransactionType.TransferOut:
                    case InventoryTransactionType.TransferReverse:
                        m.Transfer += r.QuantityDelta;             // net in(+)/out(-)
                        break;
                    case InventoryTransactionType.Purchase:
                    case InventoryTransactionType.BillEdit:
                    case InventoryTransactionType.BillReverse:
                        m.Purchased += r.QuantityDelta;
                        break;
                    case InventoryTransactionType.AdjustIncrease:
                    case InventoryTransactionType.AdjustLoss:
                    case InventoryTransactionType.AdjustReverse:
                    case InventoryTransactionType.OpeningBalance:
                    case InventoryTransactionType.TwinCorrection:
                        m.Adjusted += r.QuantityDelta;
                        break;
                    // SplitConsumed nets to zero (out of one batch, into another) → no report impact.
                    // BatchCorrection / Migration* carry QuantityDelta = 0 → no impact.
                    default:
                        break;
                }
            }
            return m;
        }

        private class DayMovement
        {
            public int Sold;
            public int DirectSale;
            public int Transfer;
            public int Purchased;
            public int Adjusted;
        }

        // Shared by the JSON and PDF stock-position endpoints: one row per brand covering the
        // whole [from, to] window (no day-by-day breakdown), listing only brands where opening
        // or closing stock is non-zero — dead/never-stocked items are omitted by the caller.
        private async Task<(Clinic clinic, List<VaccineAPI.ModelDTO.StockPositionRowDTO> rows)> BuildStockPositionRows(
            long clinicId, long doctorId, DateTime from, DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null || !await CallerOwnsClinicAsync(clinic, doctorId))
                return (clinic, null);

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
            var directSaleBrands = await _db.DirectSales
                .Where(d => d.ClinicId == clinicId)
                .Select(d => d.BrandId).Distinct().ToListAsync();
            var brandIds = soldBrands.Concat(purchBrands).Concat(xferBrands).Concat(adjBrands).Concat(directSaleBrands).Distinct().ToList();

            var rows = new List<VaccineAPI.ModelDTO.StockPositionRowDTO>();
            if (brandIds.Count == 0)
                return (clinic, rows);

            var brandsLookup = await _db.Brands.Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);

            DateTime? stockPeriodStart = clinic.StockPeriodStart;
            DateTime floorDate = stockPeriodStart?.Date ?? DateTime.MinValue;
            DateTime effFrom = from.Date < floorDate ? floorDate : from.Date;

            foreach (var bid in brandIds)
            {
                int opening = await ComputeStockUpTo(_db, clinicId, bid, effFrom.AddDays(-1), stockPeriodStart);

                var activeDates = await _db.InventoryTransactions
                    .Where(t => t.ClinicId == clinicId && t.BrandId == bid
                             && t.EventDate.Date >= effFrom && t.EventDate.Date <= to.Date
                             && t.QuantityDelta != 0)
                    .Select(t => t.EventDate.Date)
                    .Distinct().ToListAsync();

                int totSold = 0, totDirectSale = 0, totTransfer = 0, totPurchased = 0, totAdjusted = 0;
                foreach (var d in activeDates)
                {
                    var mv = await ComputeDayMovement(_db, clinicId, bid, d);
                    totSold       += mv.Sold;
                    totDirectSale += mv.DirectSale;
                    totTransfer   += mv.Transfer;
                    totPurchased  += mv.Purchased;
                    totAdjusted   += mv.Adjusted;
                }

                int closing = opening - totSold - totDirectSale + totTransfer + totPurchased + totAdjusted;
                if (opening == 0 && closing == 0) continue;

                rows.Add(new VaccineAPI.ModelDTO.StockPositionRowDTO
                {
                    BrandId = bid,
                    BrandName = brandsLookup.TryGetValue(bid, out var n) ? n : $"Brand {bid}",
                    Opening = opening,
                    Purchased = totPurchased,
                    DirectSale = totDirectSale,
                    Given = totSold,
                    Adjusted = totAdjusted,
                    Transfer = totTransfer,
                    Closing = closing
                });
            }

            return (clinic, rows.OrderBy(r => r.BrandName).ToList());
        }

        // GET /api/stock/stock-position-report?clinicId=X&from=DATE&to=DATE
        // One row per brand with opening/closing stock > 0 across the whole window — for the
        // on-screen "Stock Position Report" table (multi-brand, not the single-brand items-report).
        [HttpGet("stock-position-report")]
        public async Task<IActionResult> GetStockPositionReport(
            [FromQuery] long clinicId,
            [FromQuery] long doctorId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var (clinic, rows) = await BuildStockPositionRows(clinicId, doctorId, from, to);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (rows == null)
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });

            return Ok(new VaccineAPI.ModelDTO.StockPositionReportDTO
            {
                ClinicName = clinic.Name,
                FromDate = from.ToString("dd-MM-yyyy"),
                ToDate = to.ToString("dd-MM-yyyy"),
                Rows = rows
            });
        }

        // GET /api/stock/stock-position-report/pdf?clinicId=X&from=DATE&to=DATE
        [HttpGet("stock-position-report/pdf")]
        public async Task<IActionResult> GetStockPositionReportPdf(
            [FromQuery] long clinicId,
            [FromQuery] long doctorId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var (clinic, rows) = await BuildStockPositionRows(clinicId, doctorId, from, to);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (rows == null)
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });
            if (rows.Count == 0)
                return NotFound(new { IsSuccess = false, Message = "No stock movement data for the selected period" });

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 24, 24, 40, 30);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, new BaseColor(21, 101, 192));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(50, 50, 50));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7.5f, new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(26, 26, 46));
                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(120, 120, 120));
                BaseColor headerBg  = new BaseColor(21, 101, 192);
                BaseColor totalsBg  = new BaseColor(230, 240, 255);
                BaseColor altBg     = new BaseColor(245, 248, 255);
                BaseColor whiteBg   = new BaseColor(255, 255, 255);
                BaseColor borderClr = new BaseColor(200, 200, 200);

                doc.Add(new Paragraph("STOCK POSITION REPORT", titleFont) { Alignment = Element.ALIGN_CENTER, SpacingBefore = 6 });
                doc.Add(new Paragraph(clinic.Name, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"FROM {from:dd-MM-yyyy}  TO  {to:dd-MM-yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 10 });

                // Widths tuned to fit portrait A4's ~527pt usable width (page 595pt - 24pt*2 margins)
                // at font size 8 without wrapping: Item gets the most room, numeric columns are even.
                var tbl = new PdfPTable(8) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(new float[] { 2.4f, 1f, 1.05f, 1.05f, 0.9f, 1f, 1f, 1.05f });

                string[] colHeaders = { "Item", "Opening", "Purchase", "Direct Sale", "Given", "Adjusted", "Transfer", "Closing" };
                foreach (var h in colHeaders)
                {
                    bool right = h != "Item";
                    tbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                    {
                        BackgroundColor = headerBg, Border = Rectangle.NO_BORDER,
                        Padding = 4, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                    });
                }

                bool alt = false;
                int sOpen = 0, sPurch = 0, sDirect = 0, sGiven = 0, sAdj = 0, sXfer = 0, sClose = 0;
                foreach (var r in rows)
                {
                    var bg = alt ? altBg : whiteBg;
                    alt = !alt;

                    tbl.AddCell(Cell(r.BrandName, cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.Opening.ToString(), cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.Purchased.ToString(), cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.DirectSale.ToString(), cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.Given.ToString(), cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.Adjusted.ToString(), cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.Transfer.ToString(), cellFont, bg, borderClr));
                    tbl.AddCell(CellR(r.Closing.ToString(), boldCell, bg, borderClr));

                    sOpen += r.Opening; sPurch += r.Purchased; sDirect += r.DirectSale;
                    sGiven += r.Given; sAdj += r.Adjusted; sXfer += r.Transfer; sClose += r.Closing;
                }

                tbl.AddCell(new PdfPCell(new Phrase("Total", boldCell)) { BackgroundColor = totalsBg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4 });
                tbl.AddCell(CellR(sOpen.ToString(), boldCell, totalsBg, borderClr));
                tbl.AddCell(CellR(sPurch.ToString(), boldCell, totalsBg, borderClr));
                tbl.AddCell(CellR(sDirect.ToString(), boldCell, totalsBg, borderClr));
                tbl.AddCell(CellR(sGiven.ToString(), boldCell, totalsBg, borderClr));
                tbl.AddCell(CellR(sAdj.ToString(), boldCell, totalsBg, borderClr));
                tbl.AddCell(CellR(sXfer.ToString(), boldCell, totalsBg, borderClr));
                tbl.AddCell(CellR(sClose.ToString(), boldCell, totalsBg, borderClr));

                doc.Add(tbl);
                doc.Add(new Paragraph($"\nPrinted on: {DateTime.Now:yyyy-MM-dd hh:mm tt}", footerFont) { Alignment = Element.ALIGN_CENTER, SpacingBefore = 8 });

                doc.Close();
                writer.Close();
                return File(ms.ToArray(), "application/pdf", ReportFileName.Build("StockPositionReport", clinic.Name));
            }
        }

        // GET /api/stock/items-purchase-report?clinicId=X&brandId=X&from=DATE&to=DATE
        [HttpGet("items-purchase-report")]
        public async Task<IActionResult> GetItemsPurchaseReport(
            [FromQuery] long clinicId,
            [FromQuery] long doctorId,
            [FromQuery] long brandId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (!await CallerOwnsClinicAsync(clinic, doctorId))
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });
            string clinicName = clinic.Name;

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

            // When a single brand is selected, use its name as subtitle
            string itemName = brandId > 0 && lines.Count > 0 && lines[0].Brand != null
                ? lines[0].Brand.Name.ToUpper()
                : "ALL ITEMS";

            bool singleBrand = brandId > 0;

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 30, 30, 40, 30);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13, new BaseColor(21, 101, 192));
                var itemFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, new BaseColor(26, 26, 46));
                var subFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9,  new BaseColor(84, 110, 122));
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var cellFont   = FontFactory.GetFont(FontFactory.HELVETICA, 9,  new BaseColor(26, 26, 46));
                var boldCell   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(26, 26, 46));
                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8,  new BaseColor(120, 120, 120));

                // Clinic header block
                doc.Add(new Paragraph(clinicName, titleFont) { Alignment = Element.ALIGN_CENTER });
                if (!string.IsNullOrWhiteSpace(clinic?.Address))
                    doc.Add(new Paragraph(clinic.Address, subFont) { Alignment = Element.ALIGN_CENTER });
                if (!string.IsNullOrWhiteSpace(clinic?.PhoneNumber))
                    doc.Add(new Paragraph(clinic.PhoneNumber, subFont) { Alignment = Element.ALIGN_CENTER });

                doc.Add(new Paragraph(" ") { SpacingAfter = 4 });
                doc.Add(new Paragraph("PURCHASE REPORT (ITEM WISE)", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(itemName, itemFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"FROM {from:dd-MM-yyyy} TO {to:dd-MM-yyyy}", subFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });

                // Columns: Date | Invoice | Supplier | [Brand if all-items] | Quantity | Rate | Total Amount
                int colCount = singleBrand ? 6 : 7;
                float[] widths = singleBrand
                    ? new float[] { 1.6f, 1.8f, 2.4f, 1f, 1.4f, 1.6f }
                    : new float[] { 1.4f, 1.6f, 2f, 1.8f, 0.9f, 1.3f, 1.4f };

                var tbl = new PdfPTable(colCount) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(widths);
                BaseColor headerBg = new BaseColor(21, 101, 192);
                BaseColor borderClr = new BaseColor(220, 220, 220);

                var headers = singleBrand
                    ? new[] { "Date", "Invoice", "Supplier", "Quantity", "Rate", "Total Amount" }
                    : new[] { "Date", "Invoice", "Supplier", "Brand", "Quantity", "Rate", "Total Amount" };

                foreach (var h in headers)
                {
                    bool right = h == "Quantity" || h == "Rate" || h == "Total Amount";
                    tbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                    {
                        BackgroundColor = headerBg, Border = Rectangle.NO_BORDER,
                        Padding = 5, HorizontalAlignment = right ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                    });
                }

                bool alt = false;
                decimal grandTotal = 0;
                int totalQty = 0;

                foreach (var s in lines)
                {
                    var bg = alt ? new BaseColor(240, 245, 255) : new BaseColor(255, 255, 255);
                    alt = !alt;
                    decimal lineTotal = s.StockAmount * s.OriginalQuantity;
                    grandTotal += lineTotal;
                    totalQty   += s.OriginalQuantity;

                    tbl.AddCell(new PdfPCell(new Phrase(s.Bill.BillDate.ToString("dd-MM-yyyy"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.Bill.BillNo, cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.Bill.Supplier ?? "", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4 });
                    if (!singleBrand)
                        tbl.AddCell(new PdfPCell(new Phrase(s.Brand != null ? s.Brand.Name : "", cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4 });
                    tbl.AddCell(new PdfPCell(new Phrase(s.OriginalQuantity.ToString(), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(s.StockAmount.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                    tbl.AddCell(new PdfPCell(new Phrase(lineTotal.ToString("N2"), cellFont)) { BackgroundColor = bg, Border = Rectangle.BOX, BorderColor = borderClr, Padding = 4, HorizontalAlignment = Element.ALIGN_RIGHT });
                }

                doc.Add(tbl);

                // Footer: Total Amount | Average Rate
                decimal avgRate = totalQty > 0 ? Math.Round(grandTotal / totalQty, 2) : 0;
                var footerTbl = new PdfPTable(1) { WidthPercentage = 100, SpacingBefore = 6 };
                footerTbl.AddCell(new PdfPCell(new Phrase(
                    $"Total Amount: {grandTotal:N2} | Average Rate: {avgRate:N2}", boldCell))
                {
                    Border = Rectangle.NO_BORDER, Padding = 4,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                });
                doc.Add(footerTbl);

                doc.Add(new Paragraph($"Printed on: {DateTime.Now:yyyy-MM-dd hh:mm tt}", footerFont) { Alignment = Element.ALIGN_RIGHT, SpacingBefore = 4 });

                doc.Close();
                writer.Close();
                string purchaseReportType = singleBrand ? $"PurchaseReport-{itemName}" : "PurchaseReport-AllItems";
                string fname = ReportFileName.Build(purchaseReportType, clinicName);
                return File(ms.ToArray(), "application/pdf", fname);
            }
        }

        // GET /api/stock/items-supplier-report?clinicId=X&supplier=X&from=DATE&to=DATE
        [HttpGet("items-supplier-report")]
        public async Task<IActionResult> GetItemsSupplierReport(
            [FromQuery] long clinicId,
            [FromQuery] long doctorId,
            [FromQuery] string supplier,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            var clinic = await _db.Clinics.FindAsync(clinicId);
            if (clinic == null)
                return NotFound(new { IsSuccess = false, Message = "Clinic not found" });
            if (!await CallerOwnsClinicAsync(clinic, doctorId))
                return StatusCode(403, new { IsSuccess = false, Message = "You do not have access to this clinic." });
            string clinicName = clinic.Name;

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
                return File(ms.ToArray(), "application/pdf", ReportFileName.Build($"SupplierReport-{supplier}", clinicName));
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
