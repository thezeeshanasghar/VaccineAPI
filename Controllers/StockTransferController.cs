using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using Microsoft.EntityFrameworkCore;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockTransferController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public StockTransferController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpPost]
        public async Task<Response<List<StockTransferHistoryDTO>>> Post([FromBody] StockTransferRequestDTO request)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return new Response<List<StockTransferHistoryDTO>>(false, $"Validation error: {errors}", null);
            }

            if (request.FromClinicId == request.ToClinicId)
                return new Response<List<StockTransferHistoryDTO>>(false, "From and To clinics must be different.", null);

            if (!request.Items.Any())
                return new Response<List<StockTransferHistoryDTO>>(false, "No transfer items provided.", null);

            var fromClinic = await _db.Clinics.FindAsync(request.FromClinicId);
            if (fromClinic == null)
                return new Response<List<StockTransferHistoryDTO>>(false, $"From clinic (ID {request.FromClinicId}) not found.", null);

            var toClinic = await _db.Clinics.FindAsync(request.ToClinicId);
            if (toClinic == null)
                return new Response<List<StockTransferHistoryDTO>>(false, $"To clinic (ID {request.ToClinicId}) not found.", null);

            var doctor = await _db.Doctors.FindAsync(request.DoctorId);
            if (doctor == null)
                return new Response<List<StockTransferHistoryDTO>>(false, "Doctor not found.", null);

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Create a transfer bill in the destination clinic for audit linking
                var transferBill = new Bill
                {
                    BillNo = $"XFER-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Supplier = $"Transfer from {fromClinic.Name}",
                    BillDate = DateTime.UtcNow,
                    IsPaid = false,
                    PaidDate = null,
                    AmountPaid = null,
                    DoctorId = request.DoctorId,
                    ClinicId = request.ToClinicId,
                    IsPAApprove = true
                };
                _db.Bills.Add(transferBill);
                await _db.SaveChangesAsync();

                var transferRecords = new List<StockTransfer>();

                foreach (var item in request.Items)
                {
                    if (item.Quantity <= 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false, "Transfer quantity must be greater than zero.", null);
                    }

                    var brand = await _db.Brands.FindAsync(item.BrandId);
                    if (brand == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false, $"Brand ID {item.BrandId} not found.", null);
                    }

                    // Verify source has sufficient inventory
                    var sourceBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == item.BrandId && ba.ClinicId == request.FromClinicId);

                    if (sourceBrandAmount == null || sourceBrandAmount.Count < item.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false,
                            $"Insufficient inventory for brand '{brand.Name}'. Available: {sourceBrandAmount?.Count ?? 0}, Requested: {item.Quantity}.", null);
                    }

                    // Deduct from source Stock records in FEFO order (earliest expiry first).
                    // If a BatchNumber is specified it must match exactly — never fall back to all batches.
                    var hasBatch = !string.IsNullOrWhiteSpace(item.BatchNumber);
                    var sourceStocks = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == item.BrandId
                                 && s.Bill.ClinicId == request.FromClinicId
                                 && s.Quantity > 0
                                 && (!hasBatch || (s.BatchLot ?? "").Trim() == item.BatchNumber.Trim()))
                        .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                        .ThenBy(s => s.Expiry)
                        .ThenBy(s => s.Id)
                        .ToListAsync();

                    int remaining = item.Quantity;
                    foreach (var src in sourceStocks)
                    {
                        if (remaining <= 0) break;
                        int deduct = Math.Min(src.Quantity, remaining);
                        src.Quantity -= deduct;
                        remaining -= deduct;
                        if (src.Quantity == 0)
                            _db.Stocks.Remove(src);
                        else
                            _db.Entry(src).State = EntityState.Modified;
                    }

                    if (remaining > 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<StockTransferHistoryDTO>>(false,
                            $"Could not deduct full transfer quantity for brand '{brand.Name}' batch '{item.BatchNumber}'.", null);
                    }

                    // Update source BrandAmount
                    sourceBrandAmount.Count -= item.Quantity;
                    _db.Entry(sourceBrandAmount).State = EntityState.Modified;

                    // Add to destination Stock
                    var destStock = new Stock
                    {
                        BrandId          = item.BrandId,
                        BillId           = transferBill.Id,
                        Quantity         = item.Quantity,
                        OriginalQuantity = item.Quantity,
                        StockAmount      = item.CostPrice,
                        BatchLot         = item.BatchNumber?.Trim(),
                        Expiry           = item.ExpiryDate
                    };
                    _db.Stocks.Add(destStock);

                    // Update destination BrandAmount
                    var destBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                        ba.BrandId == item.BrandId && ba.ClinicId == request.ToClinicId);

                    if (destBrandAmount != null)
                    {
                        decimal newAvg = destBrandAmount.Count == 0
                            ? item.CostPrice
                            : ((destBrandAmount.PurchasedAmt * destBrandAmount.Count) + (item.CostPrice * item.Quantity))
                              / (destBrandAmount.Count + item.Quantity);
                        destBrandAmount.Count += item.Quantity;
                        destBrandAmount.PurchasedAmt = newAvg;
                        _db.Entry(destBrandAmount).State = EntityState.Modified;
                    }
                    else
                    {
                        _db.BrandAmounts.Add(new BrandAmount
                        {
                            BrandId = item.BrandId,
                            Count = item.Quantity,
                            DoctorId = request.DoctorId,
                            ClinicId = request.ToClinicId,
                            PurchasedAmt = item.CostPrice
                        });
                    }

                    // Record the transfer
                    var transfer = new StockTransfer
                    {
                        FromClinicId = request.FromClinicId,
                        ToClinicId = request.ToClinicId,
                        BrandId = item.BrandId,
                        BatchNumber = item.BatchNumber?.Trim(),
                        ExpiryDate = item.ExpiryDate,
                        Quantity = item.Quantity,
                        CostPrice = item.CostPrice,
                        TotalValue = item.Quantity * item.CostPrice,
                        CreatedBy = request.DoctorId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.StockTransfers.Add(transfer);
                    transferRecords.Add(transfer);

                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                // Build response DTOs with clinic and brand names
                var resultDtos = transferRecords.Select(t => new StockTransferHistoryDTO
                {
                    Id = t.Id,
                    CreatedAt = t.CreatedAt,
                    FromClinicId = t.FromClinicId,
                    FromClinicName = fromClinic.Name,
                    ToClinicId = t.ToClinicId,
                    ToClinicName = toClinic.Name,
                    BrandId = t.BrandId,
                    BrandName = request.Items.FirstOrDefault(i => i.BrandId == t.BrandId)?.BrandName ?? "",
                    BatchNumber = t.BatchNumber,
                    ExpiryDate = t.ExpiryDate,
                    Quantity = t.Quantity,
                    CostPrice = t.CostPrice,
                    TotalValue = t.TotalValue,
                    CreatedBy = t.CreatedBy,
                    TransferredByName = $"{doctor.FirstName}"
                }).ToList();

                return new Response<List<StockTransferHistoryDTO>>(true, "Stock transferred successfully.", resultDtos);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                string msg = $"Error: {ex.Message}";
                if (ex.InnerException != null) msg += $" | {ex.InnerException.Message}";
                return new Response<List<StockTransferHistoryDTO>>(false, msg, null);
            }
        }

        [HttpPut("{id}")]
        public async Task<Response<string>> Put(long id, [FromBody] StockTransferEditDTO dto)
        {
            var transfer = await _db.StockTransfers.FindAsync(id);
            if (transfer == null)
                return new Response<string>(false, "Transfer record not found.", null);

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // ── Reverse original from destination ──────────────────────────
                var destBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                    ba.BrandId == transfer.BrandId && ba.ClinicId == transfer.ToClinicId);

                if (destBrandAmount != null)
                {
                    destBrandAmount.Count = Math.Max(0, destBrandAmount.Count - transfer.Quantity);
                    _db.Entry(destBrandAmount).State = EntityState.Modified;
                }

                // Remove the stock row that was added in destination
                var destStock = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == transfer.BrandId
                             && s.Bill.ClinicId == transfer.ToClinicId
                             && (s.BatchLot ?? "") == (transfer.BatchNumber ?? "")
                             && s.Quantity == transfer.Quantity)
                    .OrderByDescending(s => s.Id)
                    .FirstOrDefaultAsync();
                if (destStock != null) _db.Stocks.Remove(destStock);

                // ── Restore original to source ─────────────────────────────────
                var sourceBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                    ba.BrandId == transfer.BrandId && ba.ClinicId == transfer.FromClinicId);

                if (sourceBrandAmount == null)
                {
                    await transaction.RollbackAsync();
                    return new Response<string>(false, "Source BrandAmount not found.", null);
                }

                sourceBrandAmount.Count += transfer.Quantity;
                _db.Entry(sourceBrandAmount).State = EntityState.Modified;

                // Restore source stock row — increment existing row, never add a new one
                var restoreStockPut = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == transfer.BrandId
                             && s.Bill.ClinicId == transfer.FromClinicId
                             && (s.BatchLot ?? "").Trim() == (transfer.BatchNumber ?? "").Trim()
                             && s.Expiry == transfer.ExpiryDate)
                    .OrderByDescending(s => s.Id)
                    .FirstOrDefaultAsync();

                if (restoreStockPut != null)
                {
                    restoreStockPut.Quantity += transfer.Quantity;
                    _db.Entry(restoreStockPut).State = EntityState.Modified;
                }
                else
                {
                    var fallbackBillIdPut = (await _db.Bills
                        .Where(b => b.ClinicId == transfer.FromClinicId)
                        .OrderByDescending(b => b.Id).FirstOrDefaultAsync())?.Id ?? 1;
                    _db.Stocks.Add(new Stock
                    {
                        BrandId     = transfer.BrandId,
                        BillId      = fallbackBillIdPut,
                        Quantity    = transfer.Quantity,
                        StockAmount = transfer.CostPrice,
                        BatchLot    = transfer.BatchNumber?.Trim(),
                        Expiry      = transfer.ExpiryDate
                    });
                }

                await _db.SaveChangesAsync();

                // ── Apply new values ───────────────────────────────────────────
                var hasBatchNew = !string.IsNullOrWhiteSpace(dto.BatchNumber);
                var newSourceStocks = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == transfer.BrandId
                             && s.Bill.ClinicId == transfer.FromClinicId
                             && s.Quantity > 0
                             && (!hasBatchNew || (s.BatchLot ?? "").Trim() == dto.BatchNumber.Trim()))
                    .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                    .ThenBy(s => s.Expiry)
                    .ThenBy(s => s.Id)
                    .ToListAsync();

                int remaining = dto.Quantity;
                foreach (var src in newSourceStocks)
                {
                    if (remaining <= 0) break;
                    int deduct = Math.Min(src.Quantity, remaining);
                    src.Quantity -= deduct;
                    remaining -= deduct;
                    if (src.Quantity == 0) _db.Stocks.Remove(src);
                    else _db.Entry(src).State = EntityState.Modified;
                }

                if (remaining > 0)
                {
                    await transaction.RollbackAsync();
                    return new Response<string>(false, $"Insufficient stock in source clinic for new quantity.", null);
                }

                sourceBrandAmount.Count -= dto.Quantity;
                _db.Entry(sourceBrandAmount).State = EntityState.Modified;

                // Add to destination with new values
                _db.Stocks.Add(new Stock
                {
                    BrandId     = transfer.BrandId,
                    BillId      = destStock?.BillId ?? (await _db.Bills.Where(b => b.ClinicId == transfer.ToClinicId)
                                                                       .OrderByDescending(b => b.Id).FirstOrDefaultAsync())?.Id ?? 1,
                    Quantity    = dto.Quantity,
                    StockAmount = dto.CostPrice,
                    BatchLot    = dto.BatchNumber?.Trim(),
                    Expiry      = dto.ExpiryDate
                });

                if (destBrandAmount != null)
                {
                    destBrandAmount.Count += dto.Quantity;
                    _db.Entry(destBrandAmount).State = EntityState.Modified;
                }
                else
                {
                    _db.BrandAmounts.Add(new BrandAmount
                    {
                        BrandId      = transfer.BrandId,
                        Count        = dto.Quantity,
                        DoctorId     = transfer.CreatedBy,
                        ClinicId     = transfer.ToClinicId,
                        PurchasedAmt = dto.CostPrice
                    });
                }

                // Update the transfer record
                transfer.BatchNumber = dto.BatchNumber?.Trim();
                transfer.ExpiryDate  = dto.ExpiryDate;
                transfer.Quantity    = dto.Quantity;
                transfer.CostPrice   = dto.CostPrice;
                transfer.TotalValue  = dto.Quantity * dto.CostPrice;
                _db.Entry(transfer).State = EntityState.Modified;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new Response<string>(true, "Transfer updated successfully.", null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                string msg = $"Error: {ex.Message}";
                if (ex.InnerException != null) msg += $" | {ex.InnerException.Message}";
                return new Response<string>(false, msg, null);
            }
        }

        [HttpDelete("{id}")]
        public async Task<Response<string>> Delete(long id)
        {
            var transfer = await _db.StockTransfers.FindAsync(id);
            if (transfer == null)
                return new Response<string>(false, "Transfer record not found.", null);

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Restore source
                var sourceBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                    ba.BrandId == transfer.BrandId && ba.ClinicId == transfer.FromClinicId);
                if (sourceBrandAmount != null)
                {
                    sourceBrandAmount.Count += transfer.Quantity;
                    _db.Entry(sourceBrandAmount).State = EntityState.Modified;
                }

                // Restore source stock row — increment existing row, never add a new one
                var restoreStockDel = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == transfer.BrandId
                             && s.Bill.ClinicId == transfer.FromClinicId
                             && (s.BatchLot ?? "").Trim() == (transfer.BatchNumber ?? "").Trim()
                             && s.Expiry == transfer.ExpiryDate)
                    .OrderByDescending(s => s.Id)
                    .FirstOrDefaultAsync();

                if (restoreStockDel != null)
                {
                    restoreStockDel.Quantity += transfer.Quantity;
                    _db.Entry(restoreStockDel).State = EntityState.Modified;
                }
                else
                {
                    var fallbackBillIdDel = (await _db.Bills
                        .Where(b => b.ClinicId == transfer.FromClinicId)
                        .OrderByDescending(b => b.Id).FirstOrDefaultAsync())?.Id ?? 1;
                    _db.Stocks.Add(new Stock
                    {
                        BrandId     = transfer.BrandId,
                        BillId      = fallbackBillIdDel,
                        Quantity    = transfer.Quantity,
                        StockAmount = transfer.CostPrice,
                        BatchLot    = transfer.BatchNumber?.Trim(),
                        Expiry      = transfer.ExpiryDate
                    });
                }

                // Deduct destination
                var destBrandAmount = await _db.BrandAmounts.FirstOrDefaultAsync(ba =>
                    ba.BrandId == transfer.BrandId && ba.ClinicId == transfer.ToClinicId);
                if (destBrandAmount != null)
                {
                    destBrandAmount.Count = Math.Max(0, destBrandAmount.Count - transfer.Quantity);
                    _db.Entry(destBrandAmount).State = EntityState.Modified;
                }

                var destStock = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == transfer.BrandId
                             && s.Bill.ClinicId == transfer.ToClinicId
                             && (s.BatchLot ?? "") == (transfer.BatchNumber ?? "")
                             && s.Quantity >= transfer.Quantity)
                    .OrderByDescending(s => s.Id)
                    .FirstOrDefaultAsync();

                if (destStock != null)
                {
                    destStock.Quantity -= transfer.Quantity;
                    if (destStock.Quantity == 0) _db.Stocks.Remove(destStock);
                    else _db.Entry(destStock).State = EntityState.Modified;
                }

                _db.StockTransfers.Remove(transfer);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new Response<string>(true, "Transfer reversed and deleted.", null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                string msg = $"Error: {ex.Message}";
                if (ex.InnerException != null) msg += $" | {ex.InnerException.Message}";
                return new Response<string>(false, msg, null);
            }
        }

        [HttpGet("history")]
        public Response<List<StockTransferHistoryDTO>> GetHistory(
            [FromQuery] long? fromClinicId,
            [FromQuery] long? toClinicId,
            [FromQuery] long? brandId,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] long? doctorId)
        {
            var query = _db.StockTransfers
                .Include(t => t.FromClinic)
                .Include(t => t.ToClinic)
                .Include(t => t.Brand)
                .AsQueryable();

            if (fromClinicId.HasValue)
                query = query.Where(t => t.FromClinicId == fromClinicId.Value || t.ToClinicId == fromClinicId.Value);

            if (toClinicId.HasValue)
                query = query.Where(t => t.ToClinicId == toClinicId.Value);

            if (brandId.HasValue)
                query = query.Where(t => t.BrandId == brandId.Value);

            if (fromDate.HasValue)
                query = query.Where(t => t.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(t => t.CreatedAt <= toDate.Value.AddDays(1));

            if (doctorId.HasValue)
                query = query.Where(t => t.CreatedBy == doctorId.Value);

            var records = query
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            var dtos = records.Select(t =>
            {
                var dto = _mapper.Map<StockTransferHistoryDTO>(t);
                var doc = _db.Doctors.Find(t.CreatedBy);
                dto.TransferredByName = doc != null ? $"{doc.FirstName}" : "";
                return dto;
            }).ToList();

            return new Response<List<StockTransferHistoryDTO>>(true, null, dtos);
        }

        [HttpGet("pdf")]
        public IActionResult GetTransferPdf([FromQuery] string ids)
        {
            try
            {
                var idList = (ids ?? "")
                    .Split(',')
                    .Select(s => long.TryParse(s.Trim(), out var n) ? n : 0)
                    .Where(n => n > 0)
                    .ToList();

                if (!idList.Any())
                    return BadRequest("No transfer IDs provided.");

                var transfers = _db.StockTransfers
                    .Include(t => t.FromClinic)
                    .Include(t => t.ToClinic)
                    .Include(t => t.Brand)
                    .Where(t => idList.Contains(t.Id))
                    .OrderBy(t => t.Id)
                    .ToList();

                if (!transfers.Any())
                    return NotFound("No transfers found.");

                var fromClinicName = transfers.First().FromClinic?.Name ?? "Unknown";
                var toClinicName   = transfers.First().ToClinic?.Name  ?? "Unknown";
                var transferDate   = transfers.First().CreatedAt;
                var doctor         = _db.Doctors.Find(transfers.First().CreatedBy);
                var doctorName     = doctor != null ? doctor.FirstName : "Unknown";

                using var ms = new MemoryStream();
                var doc2 = new Document(PageSize.A4, 30, 30, 40, 30);
                PdfWriter.GetInstance(doc2, ms);
                doc2.Open();

                // ── Title ─────────────────────────────────────────────────────
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                doc2.Add(new Paragraph("STOCK TRANSFER VOUCHER", titleFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 6f
                });

                var subFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13);
                doc2.Add(new Paragraph($"{fromClinicName}  →  {toClinicName}", subFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 16f
                });

                var metaFont = FontFactory.GetFont(FontFactory.HELVETICA, 11);
                doc2.Add(new Paragraph(
                    $"Date: {transferDate:dd/MM/yyyy HH:mm}     Transferred By: {doctorName}",
                    metaFont)
                {
                    SpacingAfter = 20f
                });

                // ── Table ─────────────────────────────────────────────────────
                var table = new PdfPTable(7) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 0.25f, 1.4f, 0.85f, 0.75f, 0.4f, 0.8f, 0.85f });

                var headerBg  = new BaseColor(21, 101, 192);
                var whiteFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.White);
                var rowFont   = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                var altBg     = new BaseColor(244, 246, 252);

                foreach (var h in new[] { "#", "Brand", "Batch / Lot", "Expiry", "Qty", "Cost Price", "Total Value" })
                {
                    table.AddCell(new PdfPCell(new Phrase(h, whiteFont))
                    {
                        BackgroundColor = headerBg,
                        Padding = 6f,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                int rowNum = 1;
                foreach (var t in transfers)
                {
                    var bg = rowNum % 2 == 0 ? altBg : BaseColor.White;
                    table.AddCell(new PdfPCell(new Phrase(rowNum.ToString(), rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase(t.Brand?.Name ?? "", rowFont))
                        { BackgroundColor = bg, Padding = 5f });
                    table.AddCell(new PdfPCell(new Phrase(t.BatchNumber ?? "—", rowFont))
                        { BackgroundColor = bg, Padding = 5f });
                    table.AddCell(new PdfPCell(new Phrase(t.ExpiryDate?.ToString("dd/MM/yyyy") ?? "—", rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase(t.Quantity.ToString(), rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase($"Rs {t.CostPrice:N2}", rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    table.AddCell(new PdfPCell(new Phrase($"Rs {t.TotalValue:N2}", rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    rowNum++;
                }

                doc2.Add(table);

                // ── Totals ────────────────────────────────────────────────────
                doc2.Add(new Paragraph("\n"));
                var totalFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                doc2.Add(new Paragraph(
                    $"Total Items: {transfers.Count}     Total Qty: {transfers.Sum(t => t.Quantity)}     Total Value: Rs {transfers.Sum(t => t.TotalValue):N2}",
                    totalFont)
                {
                    Alignment = Element.ALIGN_RIGHT,
                    SpacingBefore = 8f
                });

                // ── Footer ────────────────────────────────────────────────────
                doc2.Add(new Paragraph("\n"));
                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.Gray);
                doc2.Add(new Paragraph("This is a system-generated transfer voucher. Stock moved at cost price — no sale value.", footerFont)
                {
                    Alignment = Element.ALIGN_CENTER
                });

                doc2.Close();
                return File(ms.ToArray(), "application/pdf", $"StockTransfer_{transferDate:yyyyMMdd_HHmm}.pdf");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }
    }
}
