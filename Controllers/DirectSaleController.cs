using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;
using VaccineAPI.helper;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DirectSaleController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryTransactionService _inventory;

        public DirectSaleController(Context db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DirectSaleCreateDTO dto)
        {
            var guard = StockActionGuard.CheckStockAction(
                _db, dto.PaId, dto.ManagerId, dto.CallerUserId, dto.SecurityStamp,
                perm => perm.StockDirectSale, "record a direct sale");
            if (!guard.allowed)
                return Ok(new { IsSuccess = false, Message = guard.error });

            if (dto.Items == null || dto.Items.Count == 0)
                return Ok(new { IsSuccess = false, Message = "At least one item is required" });
            if (string.IsNullOrWhiteSpace(dto.ClientName))
                return Ok(new { IsSuccess = false, Message = "Client name is required" });
            if (string.IsNullOrWhiteSpace(dto.PaymentMode))
                return Ok(new { IsSuccess = false, Message = "Payment mode is required" });

            if (dto.PaymentCollectorPaId.HasValue)
            {
                var collectorPa = await _db.PersonalAssistant
                    .FirstOrDefaultAsync(p => p.Id == dto.PaymentCollectorPaId.Value && p.DoctorId == dto.DoctorId && p.IsActive);
                if (collectorPa == null)
                    return Ok(new { IsSuccess = false, Message = "Selected PA is not valid for this doctor." });
            }

            foreach (var item in dto.Items)
            {
                if (item.Quantity <= 0)
                    return Ok(new { IsSuccess = false, Message = "Quantity must be greater than 0 for all items" });
                if (string.IsNullOrWhiteSpace(item.BatchLot))
                    return Ok(new { IsSuccess = false, Message = "Batch is required for all items" });
                if (item.SalePricePerUnit < 0)
                    return Ok(new { IsSuccess = false, Message = "Sale price cannot be negative" });
            }

            var replay = Idempotency.TryReplay(_db, dto.DoctorId, dto.ClientRequestId, "directsale.create");
            if (replay != null) return replay;

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Validate every line (and every batch it draws from) before mutating anything.
                // A lot that spans several batches is drawn earliest-expiry first; one sale row is
                // written per batch drawn so each row reverses exactly onto its own batch.
                var planned = new Dictionary<int, int>();
                var lines = new List<(DirectSaleItemDTO item, Stock stock, int take, BrandAmount ba)>();

                foreach (var item in dto.Items)
                {
                    var sourceBa = await _db.BrandAmounts
                        .FirstOrDefaultAsync(b => b.BrandId == item.BrandId && b.DoctorId == dto.DoctorId && b.ClinicId == dto.ClinicId);
                    if (sourceBa == null || sourceBa.Quantity == 0)
                        return Ok(new { IsSuccess = false, Message = $"No stock available for brand ID {item.BrandId}" });

                    var draws = new List<(Stock stock, int take)>();
                    var plan = _inventory.PlanLotDraw(item.BrandId, dto.ClinicId, item.BatchLot, item.ExpiryDate,
                        item.Quantity, planned, draws, "sell");
                    if (!plan.IsSuccess)
                        return Ok(new { IsSuccess = false, Message = plan.Message });
                    foreach (var d in draws) lines.Add((item, d.stock, d.take, sourceBa));
                }

                // Auto-generate SALE bill number — doctor-wide uniqueness
                string prefix = $"SALE-{dto.SaleDate.Year}-";
                var usedNos = await _db.DirectSales
                    .Where(s => s.DoctorId == dto.DoctorId && s.SaleBillNo != null && s.SaleBillNo.StartsWith(prefix))
                    .Select(s => s.SaleBillNo)
                    .Distinct()
                    .ToListAsync();
                int seq = 1;
                while (usedNos.Contains($"{prefix}{seq:D4}")) seq++;
                string saleBillNo = $"{prefix}{seq:D4}";

                // Process each planned line
                foreach (var (item, sourceStock, take, sourceBa) in lines)
                {
                    decimal purchasePrice = sourceStock.StockAmount;
                    decimal totalSale = item.SalePricePerUnit * take;
                    decimal totalCost = purchasePrice * take;
                    decimal profit = totalSale - totalCost;

                    var saleRow = new DirectSale
                    {
                        BrandId = item.BrandId,
                        ClinicId = dto.ClinicId,
                        DoctorId = dto.DoctorId,
                        BatchLot = item.BatchLot,
                        ExpiryDate = sourceStock.Expiry ?? item.ExpiryDate,
                        Quantity = take,
                        SalePricePerUnit = item.SalePricePerUnit,
                        PurchasePricePerUnit = purchasePrice,
                        TotalSaleValue = totalSale,
                        TotalCostValue = totalCost,
                        Profit = profit,
                        ClientName = dto.ClientName,
                        PaymentMode = dto.PaymentCollectorPaId.HasValue ? "Pending" : dto.PaymentMode,
                        OnlineService = dto.OnlineService ?? "",
                        IsPaymentApproved = true,
                        Notes = dto.Notes ?? "",
                        SaleBillNo = saleBillNo,
                        SaleDate = dto.SaleDate,
                        PaymentCollectorPaId = dto.PaymentCollectorPaId,
                        IsPaymentCollected = !dto.PaymentCollectorPaId.HasValue
                    };
                    _db.DirectSales.Add(saleRow);
                    await _db.SaveChangesAsync(); // need saleRow.Id for the ledger SourceId

                    await _inventory.SellDirect(dto.DoctorId, dto.ClinicId, sourceStock, sourceBa, take, saleRow.Id, dto.SaleDate);
                }

                var saleResponse = new { IsSuccess = true, Message = "Sale recorded", ResponseData = new { SaleBillNo = saleBillNo } };
                Idempotency.Record(_db, dto.DoctorId, dto.ClientRequestId, "directsale.create", saleResponse);
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();

                // Notify the assigned PA by email (fire-and-forget)
                if (dto.PaymentCollectorPaId.HasValue)
                {
                    var collectorPa = await _db.PersonalAssistant.FindAsync(dto.PaymentCollectorPaId.Value);
                    if (collectorPa != null && !string.IsNullOrEmpty(collectorPa.Email))
                    {
                        var saleDoctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == dto.DoctorId);
                        var saleSender = EmailSenderResolver.Resolve(saleDoctor, _db);
                        _ = Task.Run(() => UserEmail.SendEmail(
                            collectorPa.Email,
                            "A direct sale has been assigned to you for cash collection. Please log in to your VacDoc app to view it under Assignments.",
                            "New Direct Sale Assignment",
                            sender: saleSender
                        ));
                    }
                }

                return Ok(saleResponse);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Failed to save sale: " + ex.Message });
            }
        }

        // PATCH /api/DirectSale/by-bill/{saleBillNo}/record-payment-mode
        // Called by the PA assigned as cash collector once they know how the
        // client actually paid. Updates all rows sharing this SaleBillNo.
        [HttpPatch("by-bill/{saleBillNo}/record-payment-mode")]
        public IActionResult RecordPaymentMode(string saleBillNo, [FromBody] DirectSalePaymentModeDTO dto)
        {
            var rows = _db.DirectSales.Where(d => d.SaleBillNo == saleBillNo).ToList();
            if (rows.Count == 0)
                return Ok(new { IsSuccess = false, Message = "Sale not found." });

            var allowed = new[] { "Cash", "Online" };
            if (!allowed.Contains(dto.PaymentMode))
                return Ok(new { IsSuccess = false, Message = "Invalid payment mode." });

            foreach (var row in rows)
            {
                row.PaymentMode = dto.PaymentMode;
                row.OnlineService = dto.OnlineService;
                row.IsPaymentCollected = true;
            }
            _db.SaveChanges();

            return Ok(new { IsSuccess = true, Message = "Payment recorded." });
        }

        // PATCH /api/DirectSale/by-bill/{saleBillNo}/mark-done
        // PA's second step after recording the payment mode — hands the sale
        // off to the doctor's Payment Reconciliation page (status becomes
        // "Pending Handover"). Mirrors PAAssignmentController.MarkDone.
        [HttpPatch("by-bill/{saleBillNo}/mark-done")]
        public IActionResult MarkDone(string saleBillNo)
        {
            var rows = _db.DirectSales.Where(d => d.SaleBillNo == saleBillNo).ToList();
            if (rows.Count == 0)
                return Ok(new { IsSuccess = false, Message = "Sale not found." });

            if (rows.Any(r => !r.IsPaymentCollected))
                return Ok(new { IsSuccess = false, Message = "Record the payment mode before marking this done." });

            foreach (var row in rows)
                row.IsMarkedDoneByPA = true;

            _db.SaveChanges();
            return Ok(new { IsSuccess = true, Message = "Marked as done." });
        }

        // PATCH /api/DirectSale/by-bill/{saleBillNo}/confirm
        // Doctor's confirmation on the Payment Reconciliation page that this
        // sale's payment has been received. Mirrors ScheduleController.ConfirmInvoice.
        [HttpPatch("by-bill/{saleBillNo}/confirm")]
        public IActionResult Confirm(
            string saleBillNo,
            [FromQuery] long doctorId,
            [FromQuery] long? callerUserId = null,
            [FromQuery] string? securityStamp = null)
        {
            var doctor = _db.Doctors.Find(doctorId);
            if (doctor == null || !CallerGuard.VerifyCaller(_db, callerUserId, securityStamp) || callerUserId!.Value != doctor.UserId)
                return Ok(new { IsSuccess = false, Message = "Not authorised to confirm this sale." });

            var rows = _db.DirectSales.Where(d => d.SaleBillNo == saleBillNo).ToList();
            if (rows.Count == 0)
                return Ok(new { IsSuccess = false, Message = "Sale not found." });
            if (rows.Any(d => d.DoctorId != doctorId))
                return Ok(new { IsSuccess = false, Message = "Not authorised to confirm this sale." });

            var now = DateTime.UtcNow;
            foreach (var row in rows)
            {
                row.IsConfirmedByDoctor = true;
                row.ConfirmedAt = now;
            }

            _db.SaveChanges();
            return Ok(new { IsSuccess = true, Message = "Confirmed." });
        }

        // GET /api/DirectSale/pending-for-pa/{paId}
        // Direct sales assigned to this PA that the PA still needs to act on —
        // either record the payment mode, or mark done after recording it.
        // Shown in the PA's Assignments page under "Direct Sales — Record Payment".
        [HttpGet("pending-for-pa/{paId}")]
        public IActionResult GetPendingForPa(long paId)
        {
            var rows = _db.DirectSales
                .Where(d => d.PaymentCollectorPaId == paId && !d.IsMarkedDoneByPA)
                .OrderByDescending(d => d.SaleDate)
                .ToList();

            var result = rows
                .GroupBy(d => d.SaleBillNo ?? $"id-{d.Id}")
                .Select(g =>
                {
                    var first = g.First();
                    return new
                    {
                        SaleBillNo = first.SaleBillNo,
                        ClientName = first.ClientName ?? "",
                        Date = first.SaleDate.ToString("yyyy-MM-dd"),
                        Amount = g.Sum(x => x.TotalSaleValue),
                        ClinicId = first.ClinicId,
                        IsPaymentCollected = first.IsPaymentCollected,
                        PaymentMode = first.IsPaymentCollected ? first.PaymentMode : null
                    };
                });

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        // GET /api/DirectSale/completed-for-pa/{paId}
        // Direct sales this PA has marked done but the doctor hasn't confirmed cash for yet —
        // shown in the PA's list alongside completed vaccine assignments. Was previously
        // unfiltered on IsConfirmedByDoctor, so a confirmed sale stayed visible forever — same
        // bug class as GetByPA's old !IsCompleted filter, now excluded once confirmed so both
        // lists disappear on the same doctor-confirmation cutoff.
        [HttpGet("completed-for-pa/{paId}")]
        public IActionResult GetCompletedForPa(long paId)
        {
            var rows = _db.DirectSales
                .Where(d => d.PaymentCollectorPaId == paId && d.IsMarkedDoneByPA && !d.IsConfirmedByDoctor)
                .OrderByDescending(d => d.SaleDate)
                .ToList();

            var result = rows
                .GroupBy(d => d.SaleBillNo ?? $"id-{d.Id}")
                .Select(g =>
                {
                    var first = g.First();
                    return new
                    {
                        SaleBillNo = first.SaleBillNo,
                        ClientName = first.ClientName ?? "",
                        Date = first.SaleDate.ToString("yyyy-MM-dd"),
                        Amount = g.Sum(x => x.TotalSaleValue),
                        ClinicId = first.ClinicId,
                        PaymentMode = first.PaymentMode,
                        IsConfirmedByDoctor = first.IsConfirmedByDoctor,
                        ConfirmedAt = first.ConfirmedAt.HasValue ? first.ConfirmedAt.Value.ToString("yyyy-MM-ddTHH:mm:ss") : (string)null
                    };
                });

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            var rows = await _db.DirectSales
                .Include(s => s.Brand)
                .Where(s => s.DoctorId == doctorId && s.ClinicId == clinicId)
                .OrderByDescending(s => s.SaleDate)
                .ThenByDescending(s => s.Id)
                .ToListAsync();

            var brandIds = rows.Select(s => s.BrandId).Distinct().ToList();
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            var paIds = rows.Where(s => s.PaymentCollectorPaId.HasValue)
                .Select(s => s.PaymentCollectorPaId.Value)
                .Distinct().ToList();
            var paNames = await _db.PersonalAssistant
                .Where(p => paIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var result = rows.Select(s =>
            {
                var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == s.BrandId);
                return new DirectSaleListDTO
                {
                    Id = s.Id,
                    SaleBillNo = s.SaleBillNo ?? "",
                    BrandName = s.Brand != null ? s.Brand.Name : "",
                    VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                    BatchLot = s.BatchLot ?? "",
                    ExpiryDate = s.ExpiryDate,
                    Quantity = s.Quantity,
                    SalePricePerUnit = s.SalePricePerUnit,
                    PurchasePricePerUnit = s.PurchasePricePerUnit,
                    TotalSaleValue = s.TotalSaleValue,
                    TotalCostValue = s.TotalCostValue,
                    Profit = s.Profit,
                    ClientName = s.ClientName ?? "",
                    PaymentMode = s.PaymentMode,
                    OnlineService = s.OnlineService ?? "",
                    Notes = s.Notes ?? "",
                    SaleDate = s.SaleDate,
                    PaymentCollectorPaId = s.PaymentCollectorPaId,
                    PaymentCollectorPaName = s.PaymentCollectorPaId.HasValue && paNames.ContainsKey(s.PaymentCollectorPaId.Value)
                        ? paNames[s.PaymentCollectorPaId.Value] : null
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(
            long id,
            [FromQuery] long? paId = null,
            [FromQuery] long? managerId = null,
            [FromQuery] long? callerUserId = null,
            [FromQuery] string? securityStamp = null)
        {
            var guard = StockActionGuard.CheckStockAction(
                _db, paId, managerId, callerUserId, securityStamp,
                perm => perm.StockDirectSale, "reverse a direct sale");
            if (!guard.allowed)
                return Ok(new { IsSuccess = false, Message = guard.error });

            var sale = await _db.DirectSales.FirstOrDefaultAsync(s => s.Id == id);
            if (sale == null)
                return Ok(new { IsSuccess = false, Message = "Sale not found" });

            string saleBillNo = sale.SaleBillNo ?? "";

            // Note: if sale.PaymentCollectorPaId is set and that cash was already swept into a
            // confirmed PaCashHandover, reversing this sale can make the PA's cash-in-hand go
            // negative. This is acceptable and correctable via POST /api/PaCashHandover/adjust
            // (PaPayableAdjustment), the existing escape valve for reconciliation discrepancies.
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // All rows sharing the same bill (reverse whole sale)
                var allRows = !string.IsNullOrEmpty(saleBillNo)
                    ? await _db.DirectSales.Where(s => s.SaleBillNo == saleBillNo && s.DoctorId == sale.DoctorId).ToListAsync()
                    : new List<DirectSale> { sale };

                foreach (var row in allRows)
                {
                    var rev = await _inventory.ReverseDirectSale(row.DoctorId, row.ClinicId, row.BrandId, row.Quantity,
                        row.BatchLot, row.PurchasePricePerUnit, row.ExpiryDate, row.Id);
                    if (!rev.IsSuccess)
                        return Ok(new { IsSuccess = false, Message = rev.Message });
                }

                _db.DirectSales.RemoveRange(allRows);
                await _inventory.SaveAndAssertAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Sale reversed" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Failed to reverse sale: " + ex.Message });
            }
        }

        [HttpGet("pdf")]
        public async Task<IActionResult> GetPdf([FromQuery] string saleBillNo)
        {
            var rows = await _db.DirectSales
                .Include(s => s.Brand)
                .Where(s => s.SaleBillNo == saleBillNo)
                .OrderBy(s => s.Id)
                .ToListAsync();

            if (rows.Count == 0)
                return Ok(new { IsSuccess = false, Message = "No sale found for this bill number" });

            var first = rows[0];
            var brandIds = rows.Select(s => s.BrandId).Distinct().ToList();
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == first.ClinicId);
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == first.DoctorId);

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
                var smallFont  = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(84, 110, 122));
                var redFont    = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(198, 40, 40));
                var orangeFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(245, 127, 23));
                var greenFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, new BaseColor(46, 125, 50));

                string clinicName  = clinic != null ? clinic.Name : "";
                string doctorName  = doctor != null ? doctor.DisplayName : "";

                doc.Add(new Paragraph("Sale Invoice", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(saleBillNo, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(doctorName + (clinicName != "" ? " — " + clinicName : ""), subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph("Date: " + first.SaleDate.ToString("dd MMM yyyy"), smallFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph("Client: " + (first.ClientName ?? ""), smallFont) { Alignment = Element.ALIGN_CENTER });
                string payStr = first.PaymentMode + (!string.IsNullOrEmpty(first.OnlineService) ? " / " + first.OnlineService : "");
                doc.Add(new Paragraph("Payment: " + payStr, smallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 10 });

                if (!string.IsNullOrWhiteSpace(first.Notes))
                    doc.Add(new Paragraph("Notes: " + first.Notes, smallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 8 });

                // Table: Vaccine | Brand | Batch | Expiry | Qty | Unit Price | Total
                var tbl = new PdfPTable(7) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(new float[] { 2.2f, 1.8f, 1.6f, 1.6f, 0.8f, 1.4f, 1.4f });

                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Vaccine", "Brand", "Batch", "Expiry", "Qty", "Unit Price", "Total" };
                foreach (var h in headers)
                {
                    bool rightAlign = h == "Qty" || h == "Unit Price" || h == "Total";
                    tbl.AddCell(new PdfPCell(new Phrase(h, headerFont))
                    {
                        BackgroundColor = headerBg,
                        Border = Rectangle.NO_BORDER,
                        Padding = 5,
                        HorizontalAlignment = rightAlign ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT
                    });
                }

                decimal grandTotal = 0;
                bool alt = false;
                foreach (var s in rows)
                {
                    BaseColor rowBg = alt ? new BaseColor(250, 250, 255) : new BaseColor(255, 255, 255);
                    alt = !alt;

                    bool expired = s.ExpiryDate.HasValue && s.ExpiryDate.Value < DateTime.Today;
                    bool soon = !expired && s.ExpiryDate.HasValue && (s.ExpiryDate.Value - DateTime.Today).TotalDays <= 90;

                    var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == s.BrandId);
                    string vaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "";
                    string brandName = s.Brand != null ? s.Brand.Name : "";
                    string batchStr = string.IsNullOrEmpty(s.BatchLot) ? "—" : s.BatchLot;
                    string expiryStr = s.ExpiryDate.HasValue ? s.ExpiryDate.Value.ToString("dd MMM yyyy") : "—";
                    string expiryLabel = expiryStr + (expired ? " [Exp]" : soon ? " [Soon]" : "");
                    Font expiryFont = expired ? redFont : soon ? orangeFont : cellFont;
                    grandTotal += s.TotalSaleValue;

                    void AddCell(string text, Font f, int align = Element.ALIGN_LEFT)
                    {
                        tbl.AddCell(new PdfPCell(new Phrase(text, f))
                        {
                            BackgroundColor = rowBg,
                            Border = Rectangle.BOX,
                            BorderColor = new BaseColor(220, 220, 220),
                            Padding = 4,
                            HorizontalAlignment = align
                        });
                    }

                    AddCell(vaccineName, cellFont);
                    AddCell(brandName, cellFont);
                    AddCell(batchStr, cellFont);
                    AddCell(expiryLabel, expiryFont);
                    AddCell(s.Quantity.ToString(), cellFont, Element.ALIGN_RIGHT);
                    AddCell(s.SalePricePerUnit.ToString("N2"), cellFont, Element.ALIGN_RIGHT);
                    AddCell(s.TotalSaleValue.ToString("N2"), cellFont, Element.ALIGN_RIGHT);
                }

                doc.Add(tbl);

                var totalsTable = new PdfPTable(2) { WidthPercentage = 40, HorizontalAlignment = Element.ALIGN_RIGHT, SpacingBefore = 6 };
                totalsTable.SetWidths(new float[] { 1.5f, 1f });

                void AddTotal(string label, string value, bool bold = false)
                {
                    var f = bold ? boldCell : cellFont;
                    totalsTable.AddCell(new PdfPCell(new Phrase(label, f)) { Border = Rectangle.NO_BORDER, Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                    totalsTable.AddCell(new PdfPCell(new Phrase(value, f)) { Border = Rectangle.NO_BORDER, Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                }

                AddTotal("Grand Total:", grandTotal.ToString("N2"), true);
                doc.Add(totalsTable);

                doc.Add(new Paragraph("PAID", greenFont) { Alignment = Element.ALIGN_RIGHT, SpacingBefore = 6 });

                doc.Close();
                writer.Close();

                return File(ms.ToArray(), "application/pdf", ReportFileName.Build($"Sale-{saleBillNo}", clinicName));
            }
        }
    }
}
