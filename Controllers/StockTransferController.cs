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

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockTransferController : ControllerBase
    {
        private readonly Context _db;
        private readonly InventoryTransactionService _inventory;

        public StockTransferController(Context db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StockTransferCreateDTO dto)
        {
            if (dto.Items == null || dto.Items.Count == 0)
                return Ok(new { IsSuccess = false, Message = "At least one item is required" });
            if (dto.FromClinicId == dto.ToClinicId)
                return Ok(new { IsSuccess = false, Message = "Source and destination clinics must be different" });

            foreach (var item in dto.Items)
            {
                if (item.Quantity <= 0)
                    return Ok(new { IsSuccess = false, Message = "Quantity must be greater than 0 for all items" });
                if (string.IsNullOrWhiteSpace(item.BatchLot))
                    return Ok(new { IsSuccess = false, Message = "Batch is required for all items" });
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var fromClinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == dto.FromClinicId);
                var toClinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == dto.ToClinicId);

                // Validate all source stocks before mutating anything
                var sourceStocks = new List<Stock>();
                var sourceBas = new List<BrandAmount>();

                foreach (var item in dto.Items)
                {
                    var sourceBa = await _db.BrandAmounts
                        .FirstOrDefaultAsync(b => b.BrandId == item.BrandId && b.DoctorId == dto.DoctorId && b.ClinicId == dto.FromClinicId);
                    if (sourceBa == null || sourceBa.Count == 0)
                        return Ok(new { IsSuccess = false, Message = $"No stock available for brand ID {item.BrandId} at the source clinic" });

                    var sourceStock = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == item.BrandId
                                 && s.BillId != null
                                 && s.BatchLot == item.BatchLot
                                 && s.Quantity > 0
                                 && s.Bill.ClinicId == dto.FromClinicId)
                        .FirstOrDefaultAsync();

                    if (sourceStock == null)
                        return Ok(new { IsSuccess = false, Message = $"Batch '{item.BatchLot}' not found or has no remaining stock at the source clinic" });
                    if (item.Quantity > sourceStock.Quantity)
                        return Ok(new { IsSuccess = false, Message = $"Cannot transfer more than available ({sourceStock.Quantity}) in batch '{item.BatchLot}'" });

                    sourceStocks.Add(sourceStock);
                    sourceBas.Add(sourceBa);
                }

                // Auto-generate XFER bill number — doctor-wide uniqueness
                string prefix = $"XFER-{dto.TransferDate.Year}-";
                var usedNos = await _db.Bills
                    .Where(b => b.DoctorId == dto.DoctorId && b.BillNo.StartsWith(prefix))
                    .Select(b => b.BillNo)
                    .ToListAsync();
                int seq = 1;
                while (usedNos.Contains($"{prefix}{seq:D4}")) seq++;
                string billNo = $"{prefix}{seq:D4}";

                // Calculate totals
                decimal subTotal = dto.Items.Sum(i => i.UnitPrice * i.Quantity);
                decimal awtAmount = Math.Round(subTotal * dto.AwtPercent / 100, 2);
                decimal totalPayable = subTotal + awtAmount;

                // Create purchase bill at destination clinic
                var bill = new Bill
                {
                    BillNo = billNo,
                    BillDate = dto.TransferDate,
                    Supplier = fromClinic != null ? fromClinic.Name : $"Clinic {dto.FromClinicId}",
                    SupplierId = null,
                    DoctorId = dto.DoctorId,
                    ClinicId = dto.ToClinicId,
                    AwtPercent = dto.AwtPercent,
                    AwtAmount = awtAmount,
                    AmountPaid = totalPayable,
                    PaymentMethod = "Transfer",
                    IsPaid = totalPayable >= 0,
                    PaidDate = DateTime.Now,
                    IsPAApprove = false
                };
                _db.Bills.Add(bill);
                await _db.SaveChangesAsync(); // get bill.Id

                // Process each item
                for (int i = 0; i < dto.Items.Count; i++)
                {
                    var item = dto.Items[i];
                    var sourceStock = sourceStocks[i];
                    var sourceBa = sourceBas[i];

                    // Audit record — created first so its Id is available as the ledger SourceId
                    decimal lineAwt = Math.Round(item.UnitPrice * item.Quantity * dto.AwtPercent / 100, 2);
                    var transferRow = new StockTransfer
                    {
                        DoctorId = dto.DoctorId,
                        FromClinicId = dto.FromClinicId,
                        ToClinicId = dto.ToClinicId,
                        BrandId = item.BrandId,
                        BatchLot = item.BatchLot,
                        ExpiryDate = item.ExpiryDate,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        AwtPercent = dto.AwtPercent,
                        AwtAmount = lineAwt,
                        Reason = dto.Reason ?? "",
                        TransferDate = dto.TransferDate,
                        BillId = bill.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.StockTransfers.Add(transferRow);
                    await _db.SaveChangesAsync();

                    await _inventory.TransferOut(dto.DoctorId, dto.FromClinicId, sourceStock, sourceBa, item.Quantity, transferRow.Id);
                    await _inventory.TransferIn(dto.DoctorId, dto.ToClinicId, item.BrandId, bill.Id, item.Quantity, item.UnitPrice, item.BatchLot, item.ExpiryDate, transferRow.Id, sourceBa.Amount);
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Transfer recorded", ResponseData = new { BillId = bill.Id, BillNo = billNo } });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                var inner1 = ex.InnerException != null ? ex.InnerException.Message : "";
                var inner2 = ex.InnerException?.InnerException != null ? ex.InnerException.InnerException.Message : "";
                var detail = ex.Message + (inner1 != "" ? " | L1: " + inner1 : "") + (inner2 != "" ? " | L2: " + inner2 : "");
                return Ok(new { IsSuccess = false, Message = "Failed to save transfer: " + detail });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] long doctorId, [FromQuery] long clinicId)
        {
            var rows = await _db.StockTransfers
                .Include(t => t.Brand)
                .Include(t => t.FromClinic)
                .Include(t => t.ToClinic)
                .Where(t => t.DoctorId == doctorId && (t.FromClinicId == clinicId || t.ToClinicId == clinicId))
                .OrderByDescending(t => t.TransferDate)
                .ThenByDescending(t => t.Id)
                .ToListAsync();

            // Fetch BillNo separately to avoid EF loading Bill.Stocks via navigation fixup
            var billIds = rows.Where(t => t.BillId.HasValue).Select(t => t.BillId.Value).Distinct().ToList();
            var billNos = await _db.Bills
                .Where(b => billIds.Contains(b.Id))
                .Select(b => new { b.Id, b.BillNo })
                .ToListAsync();

            var brandIds = rows.Select(t => t.BrandId).Distinct().ToList();
            var vaccineBrands = await _db.VaccineBrands
                .Include(vb => vb.Vaccine)
                .Where(vb => brandIds.Contains(vb.BrandId))
                .ToListAsync();

            var result = rows.Select(t =>
            {
                var vb = vaccineBrands.FirstOrDefault(x => x.BrandId == t.BrandId);
                var billEntry = billNos.FirstOrDefault(b => t.BillId.HasValue && b.Id == t.BillId.Value);
                return new StockTransferListDTO
                {
                    Id = t.Id,
                    BillId = t.BillId,
                    BillNo = billEntry != null ? billEntry.BillNo : "",
                    BrandName = t.Brand != null ? t.Brand.Name : "",
                    VaccineName = vb != null && vb.Vaccine != null ? vb.Vaccine.Name : "",
                    FromClinicName = t.FromClinic != null ? t.FromClinic.Name : "",
                    ToClinicName = t.ToClinic != null ? t.ToClinic.Name : "",
                    BatchLot = t.BatchLot ?? "",
                    ExpiryDate = t.ExpiryDate,
                    Quantity = t.Quantity,
                    UnitPrice = t.UnitPrice,
                    LineTotal = t.UnitPrice * t.Quantity,
                    AwtPercent = t.AwtPercent,
                    AwtAmount = t.AwtAmount,
                    Reason = t.Reason ?? "",
                    TransferDate = t.TransferDate
                };
            }).ToList();

            return Ok(new { IsSuccess = true, ResponseData = result });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (transfer == null)
                return Ok(new { IsSuccess = false, Message = "Transfer not found" });

            int? billId = transfer.BillId;

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Collect all transfer rows sharing the same bill (whole transfer reversal)
                var allRows = billId.HasValue
                    ? await _db.StockTransfers.Where(t => t.BillId == billId).ToListAsync()
                    : new List<StockTransfer> { transfer };

                foreach (var row in allRows)
                {
                    await _inventory.ReverseTransferOut(row.DoctorId, row.FromClinicId, row.BrandId, row.Quantity, row.BatchLot, row.UnitPrice, row.ExpiryDate, row.Id);
                    await _inventory.ReverseTransferIn(row.DoctorId, row.ToClinicId, row.BrandId, row.Quantity, row.Id);
                }

                // Remove destination stock rows created by this bill
                if (billId.HasValue)
                {
                    var destStocks = await _db.Stocks.Where(s => s.BillId == billId).ToListAsync();
                    _db.Stocks.RemoveRange(destStocks);

                    // Delete the XFER bill
                    var xferBill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == billId);
                    if (xferBill != null)
                        _db.Bills.Remove(xferBill);
                }

                _db.StockTransfers.RemoveRange(allRows);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { IsSuccess = true, Message = "Transfer reversed" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Ok(new { IsSuccess = false, Message = "Failed to reverse transfer: " + ex.Message });
            }
        }

        [HttpGet("pdf")]
        public async Task<IActionResult> GetPdf([FromQuery] int billId)
        {
            var transfers = await _db.StockTransfers
                .Include(t => t.Brand)
                .Include(t => t.FromClinic)
                .Include(t => t.ToClinic)
                .Where(t => t.BillId == billId)
                .OrderBy(t => t.Id)
                .ToListAsync();

            if (transfers.Count == 0)
                return Ok(new { IsSuccess = false, Message = "No transfer found for this bill" });

            // Fetch bill number without triggering Bill.Stocks navigation load
            var billEntry = await _db.Bills
                .Where(b => b.Id == billId)
                .Select(b => new { b.BillNo })
                .FirstOrDefaultAsync();
            string billNo = billEntry != null ? billEntry.BillNo : "";

            var first = transfers[0];
            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 50, 36);
                var writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, new BaseColor(21, 101, 192));
                var subFont     = FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(84, 110, 122));
                var headerFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(255, 255, 255));
                var cellFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9, new BaseColor(26, 26, 46));
                var boldCell    = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(26, 26, 46));
                var smallFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(84, 110, 122));
                var redFont     = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(198, 40, 40));
                var orangeFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(245, 127, 23));
                var greenFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, new BaseColor(46, 125, 50));

                doc.Add(new Paragraph("Stock Transfer", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(billNo, subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(
                    (first.FromClinic != null ? first.FromClinic.Name : $"Clinic {first.FromClinicId}")
                    + "  →  "
                    + (first.ToClinic != null ? first.ToClinic.Name : $"Clinic {first.ToClinicId}"),
                    subFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph("Date: " + first.TransferDate.ToString("dd MMM yyyy"), smallFont) { Alignment = Element.ALIGN_CENTER });
                if (!string.IsNullOrWhiteSpace(first.Reason))
                    doc.Add(new Paragraph("Reason: " + first.Reason, smallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 12 });
                else
                    doc.Add(new Paragraph(" ") { SpacingAfter = 8 });

                // Table: Brand | Batch | Expiry | Qty | Unit Price | Total
                var tbl = new PdfPTable(6) { WidthPercentage = 100, SpacingBefore = 4 };
                tbl.SetWidths(new float[] { 2.2f, 1.8f, 1.6f, 0.8f, 1.4f, 1.4f });

                BaseColor headerBg = new BaseColor(21, 101, 192);
                string[] headers = { "Brand", "Batch", "Expiry", "Qty", "Unit Price", "Total" };
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

                decimal subTotal = 0;
                bool alt = false;
                foreach (var t in transfers)
                {
                    BaseColor rowBg = alt ? new BaseColor(250, 250, 255) : new BaseColor(255, 255, 255);
                    alt = !alt;

                    bool expired = t.ExpiryDate.HasValue && t.ExpiryDate.Value < DateTime.Today;
                    bool soon = !expired && t.ExpiryDate.HasValue && (t.ExpiryDate.Value - DateTime.Today).TotalDays <= 90;

                    string brandName = t.Brand != null ? t.Brand.Name : "";
                    string batchStr = string.IsNullOrEmpty(t.BatchLot) ? "—" : t.BatchLot;
                    string expiryStr = t.ExpiryDate.HasValue ? t.ExpiryDate.Value.ToString("dd MMM yyyy") : "—";
                    string expiryLabel = expiryStr + (expired ? " [Exp]" : soon ? " [Soon]" : "");
                    Font expiryFont = expired ? redFont : soon ? orangeFont : cellFont;
                    decimal lineTotal = t.UnitPrice * t.Quantity;
                    subTotal += lineTotal;

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

                    AddCell(brandName, cellFont);
                    AddCell(batchStr, cellFont);
                    AddCell(expiryLabel, expiryFont);
                    AddCell(t.Quantity.ToString(), cellFont, Element.ALIGN_RIGHT);
                    AddCell(t.UnitPrice.ToString("N2"), cellFont, Element.ALIGN_RIGHT);
                    AddCell(lineTotal.ToString("N2"), cellFont, Element.ALIGN_RIGHT);
                }

                doc.Add(tbl);

                // Totals
                decimal awtTotal = transfers.Sum(t => t.AwtAmount);
                decimal grandTotal = subTotal + awtTotal;

                var totalsTable = new PdfPTable(2) { WidthPercentage = 40, HorizontalAlignment = Element.ALIGN_RIGHT, SpacingBefore = 6 };
                totalsTable.SetWidths(new float[] { 1.5f, 1f });

                void AddTotal(string label, string value, bool bold = false)
                {
                    var f = bold ? boldCell : cellFont;
                    totalsTable.AddCell(new PdfPCell(new Phrase(label, f)) { Border = Rectangle.NO_BORDER, Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                    totalsTable.AddCell(new PdfPCell(new Phrase(value, f)) { Border = Rectangle.NO_BORDER, Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                }

                AddTotal("Sub-total:", subTotal.ToString("N2"));
                if (first.AwtPercent > 0)
                    AddTotal($"AWT ({first.AwtPercent:N1}%):", awtTotal.ToString("N2"));
                AddTotal("Grand Total:", grandTotal.ToString("N2"), true);

                doc.Add(totalsTable);

                doc.Add(new Paragraph("PAID", greenFont) { Alignment = Element.ALIGN_RIGHT, SpacingBefore = 6 });

                doc.Close();
                writer.Close();

                string fileName = $"Transfer-{(string.IsNullOrEmpty(billNo) ? billId.ToString() : billNo)}.pdf";
                return File(ms.ToArray(), "application/pdf", fileName);
            }
        }
    }
}
