using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DirectSaleController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public DirectSaleController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpPost]
        public async Task<Response<DirectSaleDTO>> Create([FromBody] DirectSaleDTO dto)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var brand = await _db.Brands.FindAsync(dto.BrandId);
                if (brand == null)
                    return new Response<DirectSaleDTO>(false, $"Brand ID {dto.BrandId} not found.", null);

                var brandAmount = await _db.BrandAmounts
                    .FirstOrDefaultAsync(ba => ba.BrandId == dto.BrandId && ba.ClinicId == dto.ClinicId);

                if (brandAmount == null || brandAmount.Count < dto.Quantity)
                    return new Response<DirectSaleDTO>(false,
                        $"Insufficient stock for '{brand.Name}'. Available: {brandAmount?.Count ?? 0}.", null);

                // Deduct from Stock records in FEFO order
                var sourceStocks = await _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => s.BrandId == dto.BrandId
                             && s.Bill.ClinicId == dto.ClinicId
                             && s.Quantity > 0
                             && (dto.BatchLot == null || (s.BatchLot ?? "").Trim() == dto.BatchLot.Trim()))
                    .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                    .ThenBy(s => s.Expiry)
                    .ThenBy(s => s.Id)
                    .ToListAsync();

                int remaining = dto.Quantity;
                foreach (var src in sourceStocks)
                {
                    if (remaining <= 0) break;
                    int deduct = System.Math.Min(src.Quantity, remaining);
                    src.Quantity -= deduct;
                    remaining -= deduct;
                    if (src.Quantity == 0) _db.Stocks.Remove(src);
                    else _db.Entry(src).State = EntityState.Modified;
                }

                if (remaining > 0)
                {
                    await transaction.RollbackAsync();
                    return new Response<DirectSaleDTO>(false,
                        $"Could not deduct full quantity for '{brand.Name}'.", null);
                }

                // Update BrandAmount
                brandAmount.Count -= dto.Quantity;
                _db.Entry(brandAmount).State = EntityState.Modified;

                // Compute financials
                decimal purchasePrice = brandAmount.PurchasedAmt;
                decimal totalSale  = dto.Quantity * dto.SalePricePerUnit;
                decimal totalCost  = dto.Quantity * purchasePrice;
                decimal profit     = totalSale - totalCost;

                var sale = new DirectSale
                {
                    BrandId            = dto.BrandId,
                    ClinicId           = dto.ClinicId,
                    DoctorId           = dto.DoctorId,
                    BatchLot           = dto.BatchLot?.Trim(),
                    ExpiryDate         = dto.ExpiryDate,
                    Quantity           = dto.Quantity,
                    SalePricePerUnit   = dto.SalePricePerUnit,
                    PurchasePricePerUnit = purchasePrice,
                    TotalSaleValue     = totalSale,
                    TotalCostValue     = totalCost,
                    Profit             = profit,
                    ClientName         = dto.ClientName?.Trim(),
                    PaymentMode        = dto.PaymentMode ?? "Cash",
                    Notes              = dto.Notes?.Trim(),
                    SaleDate           = dto.SaleDate != default ? dto.SaleDate : System.DateTime.UtcNow
                };
                _db.DirectSales.Add(sale);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = _mapper.Map<DirectSaleDTO>(sale);
                result.BrandName  = brand.Name;
                result.ClinicName = _db.Clinics.Find(dto.ClinicId)?.Name ?? "";
                return new Response<DirectSaleDTO>(true, "Direct sale recorded successfully.", result);
            }
            catch (System.Exception ex)
            {
                await transaction.RollbackAsync();
                return new Response<DirectSaleDTO>(false, $"Error: {ex.Message}", null);
            }
        }

        [HttpPost("bulk")]
        public async Task<Response<List<DirectSaleDTO>>> CreateBulk([FromBody] BulkDirectSaleRequestDTO request)
        {
            if (request.Items == null || !request.Items.Any())
                return new Response<List<DirectSaleDTO>>(false, "No sale items provided.", null);

            var clinic = await _db.Clinics.FindAsync(request.ClinicId);
            if (clinic == null)
                return new Response<List<DirectSaleDTO>>(false, "Clinic not found.", null);

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var results = new List<DirectSale>();

                foreach (var item in request.Items)
                {
                    var brand = await _db.Brands.FindAsync(item.BrandId);
                    if (brand == null)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<DirectSaleDTO>>(false, $"Brand ID {item.BrandId} not found.", null);
                    }

                    var brandAmount = await _db.BrandAmounts
                        .FirstOrDefaultAsync(ba => ba.BrandId == item.BrandId && ba.ClinicId == request.ClinicId);

                    if (brandAmount == null || brandAmount.Count < item.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<DirectSaleDTO>>(false,
                            $"Insufficient stock for '{brand.Name}'. Available: {brandAmount?.Count ?? 0}.", null);
                    }

                    var sourceStocks = await _db.Stocks
                        .Include(s => s.Bill)
                        .Where(s => s.BrandId == item.BrandId
                                 && s.Bill.ClinicId == request.ClinicId
                                 && s.Quantity > 0
                                 && (item.BatchLot == null || (s.BatchLot ?? "").Trim() == item.BatchLot.Trim()))
                        .OrderBy(s => s.Expiry.HasValue ? 0 : 1)
                        .ThenBy(s => s.Expiry)
                        .ThenBy(s => s.Id)
                        .ToListAsync();

                    int remaining = item.Quantity;
                    foreach (var src in sourceStocks)
                    {
                        if (remaining <= 0) break;
                        int deduct = System.Math.Min(src.Quantity, remaining);
                        src.Quantity -= deduct;
                        remaining -= deduct;
                        if (src.Quantity == 0) _db.Stocks.Remove(src);
                        else _db.Entry(src).State = EntityState.Modified;
                    }

                    if (remaining > 0)
                    {
                        await transaction.RollbackAsync();
                        return new Response<List<DirectSaleDTO>>(false,
                            $"Could not deduct full quantity for '{brand.Name}'.", null);
                    }

                    brandAmount.Count -= item.Quantity;
                    _db.Entry(brandAmount).State = EntityState.Modified;

                    decimal purchasePrice = brandAmount.PurchasedAmt;
                    decimal totalSale  = item.Quantity * item.SalePricePerUnit;
                    decimal totalCost  = item.Quantity * purchasePrice;
                    decimal profit     = totalSale - totalCost;

                    var sale = new DirectSale
                    {
                        BrandId              = item.BrandId,
                        ClinicId             = request.ClinicId,
                        DoctorId             = request.DoctorId,
                        BatchLot             = item.BatchLot?.Trim(),
                        ExpiryDate           = item.ExpiryDate,
                        Quantity             = item.Quantity,
                        SalePricePerUnit     = item.SalePricePerUnit,
                        PurchasePricePerUnit = purchasePrice,
                        TotalSaleValue       = totalSale,
                        TotalCostValue       = totalCost,
                        Profit               = profit,
                        ClientName           = request.ClientName?.Trim(),
                        PaymentMode          = request.PaymentMode ?? "Cash",
                        Notes                = request.Notes?.Trim(),
                        SaleDate             = request.SaleDate != default ? request.SaleDate : System.DateTime.UtcNow
                    };
                    _db.DirectSales.Add(sale);
                    await _db.SaveChangesAsync();

                    sale.Brand  = brand;
                    sale.Clinic = clinic;
                    results.Add(sale);
                }

                await transaction.CommitAsync();

                var dtos = _mapper.Map<List<DirectSaleDTO>>(results);
                return new Response<List<DirectSaleDTO>>(true, "Sale recorded successfully.", dtos);
            }
            catch (System.Exception ex)
            {
                await transaction.RollbackAsync();
                return new Response<List<DirectSaleDTO>>(false, $"Error: {ex.Message}", null);
            }
        }

        [HttpGet("pdf")]
        public IActionResult GetSalePdf([FromQuery] string ids)
        {
            try
            {
                var idList = (ids ?? "")
                    .Split(',')
                    .Select(s => long.TryParse(s.Trim(), out var n) ? n : 0)
                    .Where(n => n > 0)
                    .ToList();

                if (!idList.Any())
                    return BadRequest("No sale IDs provided.");

                var sales = _db.DirectSales
                    .Include(d => d.Brand)
                    .Include(d => d.Clinic)
                    .Where(d => idList.Contains(d.Id))
                    .OrderBy(d => d.Id)
                    .ToList();

                if (!sales.Any())
                    return NotFound("No sales found.");

                var clinicName  = sales.First().Clinic?.Name ?? "Unknown";
                var saleDate    = sales.First().SaleDate;
                var clientName  = sales.First().ClientName ?? "Walk-in";
                var paymentMode = sales.First().PaymentMode ?? "Cash";

                using var ms   = new MemoryStream();
                var doc2       = new Document(PageSize.A4, 30, 30, 40, 30);
                PdfWriter.GetInstance(doc2, ms);
                doc2.Open();

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                doc2.Add(new Paragraph("DIRECT SALE INVOICE", titleFont)
                {
                    Alignment    = Element.ALIGN_CENTER,
                    SpacingAfter = 6f
                });

                var subFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13);
                doc2.Add(new Paragraph(clinicName, subFont)
                {
                    Alignment    = Element.ALIGN_CENTER,
                    SpacingAfter = 16f
                });

                var metaFont = FontFactory.GetFont(FontFactory.HELVETICA, 11);
                doc2.Add(new Paragraph(
                    $"Date: {saleDate:dd/MM/yyyy}     Client: {clientName}     Payment: {paymentMode}",
                    metaFont)
                {
                    SpacingAfter = 20f
                });

                var table = new PdfPTable(7) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 0.25f, 1.4f, 0.85f, 0.75f, 0.4f, 0.8f, 0.85f });

                var headerBg  = new BaseColor(21, 101, 192);
                var whiteFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.White);
                var rowFont   = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                var altBg     = new BaseColor(244, 246, 252);

                foreach (var h in new[] { "#", "Brand", "Batch / Lot", "Expiry", "Qty", "Sale Price", "Total" })
                {
                    table.AddCell(new PdfPCell(new Phrase(h, whiteFont))
                    {
                        BackgroundColor     = headerBg,
                        Padding             = 6f,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                int rowNum = 1;
                foreach (var s in sales)
                {
                    var bg = rowNum % 2 == 0 ? altBg : BaseColor.White;
                    table.AddCell(new PdfPCell(new Phrase(rowNum.ToString(), rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase(s.Brand?.Name ?? "", rowFont))
                        { BackgroundColor = bg, Padding = 5f });
                    table.AddCell(new PdfPCell(new Phrase(s.BatchLot ?? "—", rowFont))
                        { BackgroundColor = bg, Padding = 5f });
                    table.AddCell(new PdfPCell(new Phrase(s.ExpiryDate?.ToString("dd/MM/yyyy") ?? "—", rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase(s.Quantity.ToString(), rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase($"Rs {s.SalePricePerUnit:N2}", rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    table.AddCell(new PdfPCell(new Phrase($"Rs {s.TotalSaleValue:N2}", rowFont))
                        { BackgroundColor = bg, Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    rowNum++;
                }

                doc2.Add(table);

                doc2.Add(new Paragraph("\n"));
                var totalFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                doc2.Add(new Paragraph(
                    $"Total Items: {sales.Count}     Total Qty: {sales.Sum(s => s.Quantity)}     Grand Total: Rs {sales.Sum(s => s.TotalSaleValue):N2}",
                    totalFont)
                {
                    Alignment    = Element.ALIGN_RIGHT,
                    SpacingBefore = 8f
                });

                doc2.Add(new Paragraph("\n"));
                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.Gray);
                doc2.Add(new Paragraph("This is a system-generated sale invoice.", footerFont)
                {
                    Alignment = Element.ALIGN_CENTER
                });

                doc2.Close();
                return File(ms.ToArray(), "application/pdf", $"DirectSale_{saleDate:yyyyMMdd}.pdf");
            }
            catch (System.Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }

        [HttpGet("history")]
        public Response<List<DirectSaleDTO>> GetHistory(
            [FromQuery] long? clinicId,
            [FromQuery] long? brandId,
            [FromQuery] long? doctorId,
            [FromQuery] System.DateTime? fromDate,
            [FromQuery] System.DateTime? toDate)
        {
            var query = _db.DirectSales
                .Include(d => d.Brand)
                .Include(d => d.Clinic)
                .AsQueryable();

            if (clinicId.HasValue)  query = query.Where(d => d.ClinicId  == clinicId.Value);
            if (brandId.HasValue)   query = query.Where(d => d.BrandId   == brandId.Value);
            if (doctorId.HasValue)  query = query.Where(d => d.DoctorId  == doctorId.Value);
            if (fromDate.HasValue)  query = query.Where(d => d.SaleDate  >= fromDate.Value);
            if (toDate.HasValue)    query = query.Where(d => d.SaleDate  <= toDate.Value.AddDays(1));

            var records = query.OrderByDescending(d => d.SaleDate).ToList();
            var dtos = _mapper.Map<List<DirectSaleDTO>>(records);
            return new Response<List<DirectSaleDTO>>(true, null, dtos);
        }
    }
}
