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

        [HttpGet("clinic/{clinicId}/batches")]
        public Response<List<BatchBreakdownDTO>> GetBatchBreakdown(int clinicId)
        {
            var brandAmounts = _db.BrandAmounts
                .Include(x => x.Brand)
                .Where(b => b.ClinicId == clinicId)
                .ToList();

            if (!brandAmounts.Any())
                return new Response<List<BatchBreakdownDTO>>(false, "No brand amounts found", null);

            var actualCountByBrandId = brandAmounts
                .GroupBy(b => b.BrandId)
                .ToDictionary(g => g.Key, g => g.Sum(b => b.Count));

            var stocks = _db.Stocks
                .Include(x => x.Brand)
                .Include(x => x.Bill)
                .Where(x => x.Bill != null && x.Bill.ClinicId == clinicId && x.Quantity > 0)
                .ToList();

            if (!stocks.Any())
                return new Response<List<BatchBreakdownDTO>>(true, null, new List<BatchBreakdownDTO>());

            // Load batch-specific adjustments so we can correct the proportional formula
            var batchedAdjs = _db.AdjustStocks
                .Where(a => a.ClinicId == clinicId && a.BatchLot != null && a.BatchLot != "")
                .ToList();

            var batchGroups = stocks
                .GroupBy(x => new {
                    x.BrandId,
                    BrandName = x.Brand != null ? x.Brand.Name : "Unknown",
                    BatchLot  = string.IsNullOrWhiteSpace(x.BatchLot) ? "" : x.BatchLot.Trim(),
                    Expiry    = x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null
                })
                .Select(g => new {
                    g.Key.BrandId,
                    g.Key.BrandName,
                    g.Key.BatchLot,
                    g.Key.Expiry,
                    PurchasedQty = g.Sum(x => x.Quantity)
                })
                .ToList();

            var totalPurchasedByBrand = batchGroups
                .GroupBy(x => x.BrandId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.PurchasedQty));

            // Total batch-specific adjustments per brand — stripped from actualTotal
            // so the proportional base only distributes unbatched inventory
            var totalBatchedAdjByBrand = batchedAdjs
                .GroupBy(a => a.BrandId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

            var result = batchGroups
                .Select(b => {
                    int actualTotal    = actualCountByBrandId.ContainsKey(b.BrandId) ? actualCountByBrandId[b.BrandId] : 0;
                    int purchasedTotal = totalPurchasedByBrand.ContainsKey(b.BrandId) ? totalPurchasedByBrand[b.BrandId] : 0;
                    int totalBatchAdj  = totalBatchedAdjByBrand.ContainsKey(b.BrandId) ? totalBatchedAdjByBrand[b.BrandId] : 0;

                    int pureCount = Math.Max(0, actualTotal - totalBatchAdj);
                    int proportionalBase = purchasedTotal > 0
                        ? (int)Math.Round((double)b.PurchasedQty / purchasedTotal * pureCount)
                        : 0;

                    // Add back this batch's specific adjustments
                    int batchAdj = batchedAdjs
                        .Where(a => a.BrandId == b.BrandId
                                 && (a.BatchLot?.Trim() ?? "") == b.BatchLot
                                 && AdjustExpiryMatches(a.ExpiryDate, b.Expiry))
                        .Sum(a => a.Adjustment);

                    int batchQty = Math.Max(0, proportionalBase + batchAdj);

                    return new BatchBreakdownDTO {
                        BrandId   = b.BrandId,
                        BrandName = b.BrandName,
                        BatchLot  = b.BatchLot,
                        Expiry    = b.Expiry,
                        Quantity  = batchQty
                    };
                })
                .Where(x => x.Quantity > 0)
                .OrderBy(x => x.BrandName)
                .ThenBy(x => x.Expiry ?? DateTime.MaxValue)
                .ThenBy(x => x.BatchLot)
                .ToList();

            return new Response<List<BatchBreakdownDTO>>(true, null, result);
        }

        // [HttpPost]
        // public async Task<ActionResult<BrandAmount>> Post(BrandAmount BrandAmount)
        // {
        //     _db.BrandAmounts.Update(BrandAmount);
        //     await _db.SaveChangesAsync();

        //     return CreatedAtAction(nameof(GetSingle), new { id = BrandAmount.Id }, BrandAmount);
        // }



        [HttpPut]
        public Response<List<BrandAmountDTO>> Put([FromBody] List<BrandAmountDTO> brandAmountDTOs)

        {
            foreach (var brandAmountDTO in brandAmountDTOs)
            {
                var brandAmoundDB = _db.BrandAmounts.Where(b => b.Id == brandAmountDTO.Id).FirstOrDefault();
                if (brandAmoundDB == null)
                    continue;
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

        [HttpGet("brandamountclinicwiseexpirypdf/{clinicId}")]
        public IActionResult GetBrandAmountClinicWiseExpiryPdf(int clinicId)
        {
            try
            {
                var stocks = _db.Stocks
                    .Include(x => x.Brand)
                    .Include(x => x.Bill)
                    .Include(x => x.Bill.Clinic)
                    .Where(x => x.Bill != null && x.Bill.ClinicId == clinicId && x.Quantity > 0)
                    .ToList();

                if (!stocks.Any())
                    return NotFound("No stock records found");

                var clinicName = stocks.FirstOrDefault()?.Bill?.Clinic?.Name ?? "Unknown clinic";

                // BrandAmount.Count is the authoritative current inventory.
                // Stock.Quantity reflects original purchase amounts and is not decremented
                // when vaccines are administered — only BrandAmount.Count is.
                // So we use BrandAmount.Count as the true total per brand and distribute
                // it proportionally across batches using Stock.Quantity as the weight.
                var brandAmounts = _db.BrandAmounts
                    .Where(b => b.ClinicId == clinicId)
                    .ToList();

                // Build a lookup: BrandId -> actual current count
                var actualCountByBrand = brandAmounts
                    .GroupBy(b => b.BrandId)
                    .ToDictionary(g => g.Key, g => g.Sum(b => b.Count));

                // Load batch-specific adjustments so we can correct the proportional formula
                var batchedAdjs = _db.AdjustStocks
                    .Where(a => a.ClinicId == clinicId && a.BatchLot != null && a.BatchLot != "")
                    .ToList();

                // Group stock records by brand + batch + expiry to get purchase proportions
                var batchGroups = stocks
                    .GroupBy(x => new
                    {
                        BrandId   = x.BrandId,
                        BrandName = x.Brand != null ? x.Brand.Name : "Unknown",
                        BatchLot  = string.IsNullOrWhiteSpace(x.BatchLot) ? "" : x.BatchLot.Trim(),
                        Expiry    = x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null
                    })
                    .Select(g => new
                    {
                        g.Key.BrandId,
                        g.Key.BrandName,
                        g.Key.BatchLot,
                        g.Key.Expiry,
                        PurchasedQty = g.Sum(x => x.Quantity)
                    })
                    .ToList();

                // For each brand, total purchased qty across all batches (denominator for ratio)
                var totalPurchasedByBrand = batchGroups
                    .GroupBy(x => x.BrandId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.PurchasedQty));

                // Total batch-specific adjustments per brand — stripped from actualTotal
                // so the proportional base only distributes unbatched inventory
                var totalBatchedAdjByBrand = batchedAdjs
                    .GroupBy(a => a.BrandId)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

                var reportRows = batchGroups
                    .Select(b =>
                    {
                        int actualTotal    = actualCountByBrand.ContainsKey(b.BrandId)
                            ? actualCountByBrand[b.BrandId] : 0;
                        int purchasedTotal = totalPurchasedByBrand.ContainsKey(b.BrandId)
                            ? totalPurchasedByBrand[b.BrandId] : 0;
                        int totalBatchAdj  = totalBatchedAdjByBrand.ContainsKey(b.BrandId)
                            ? totalBatchedAdjByBrand[b.BrandId] : 0;

                        int pureCount = Math.Max(0, actualTotal - totalBatchAdj);
                        int proportionalBase = purchasedTotal > 0
                            ? (int)Math.Round((double)b.PurchasedQty / purchasedTotal * pureCount)
                            : 0;

                        // Add back this batch's specific adjustments
                        int batchAdj = batchedAdjs
                            .Where(a => a.BrandId == b.BrandId
                                     && (a.BatchLot?.Trim() ?? "") == b.BatchLot
                                     && AdjustExpiryMatches(a.ExpiryDate, b.Expiry))
                            .Sum(a => a.Adjustment);

                        int batchQty = Math.Max(0, proportionalBase + batchAdj);

                        return new
                        {
                            b.BrandName,
                            b.BatchLot,
                            b.Expiry,
                            Quantity = batchQty
                        };
                    })
                    .Where(x => x.Quantity > 0)
                    .OrderBy(x => x.BrandName)
                    .ThenBy(x => x.Expiry ?? DateTime.MaxValue)
                    .ThenBy(x => x.BatchLot)
                    .ToList();

                if (!reportRows.Any())
                    return NotFound("No stock data found for report");

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(document, ms);

                    document.Open();

                    Font titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                    Paragraph title = new Paragraph("STOCK EXPIRY REPORT", titleFont)
                    {
                        Alignment = Element.ALIGN_CENTER,
                        SpacingAfter = 20f
                    };
                    document.Add(title);

                    Font clinicFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                    Paragraph clinicTitle = new Paragraph(clinicName, clinicFont)
                    {
                        Alignment = Element.ALIGN_CENTER,
                        SpacingAfter = 20f
                    };
                    document.Add(clinicTitle);

                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);
                    Paragraph date = new Paragraph($"Date: {DateTime.Now:dd/MM/yyyy}", normalFont)
                    {
                        SpacingAfter = 20f
                    };
                    document.Add(date);

                    PdfPTable table = new PdfPTable(4);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 0.15f, 1.45f, 0.4f, 0.55f });

                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    table.AddCell(new PdfPCell(new Phrase("#", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Brand Name", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Quantity", headerFont)));
                    table.AddCell(new PdfPCell(new Phrase("Expiry", headerFont)));

                    int i = 1;
                    foreach (var item in reportRows)
                    {
                        table.AddCell(new Phrase(i.ToString(), normalFont));

                        var brandDisplay = item.BrandName;
                        if (!string.IsNullOrWhiteSpace(item.BatchLot))
                        {
                            brandDisplay = $"{brandDisplay} (Lot: {item.BatchLot})";
                        }

                        table.AddCell(new Phrase(brandDisplay, normalFont));
                        table.AddCell(new Phrase(item.Quantity.ToString(), normalFont));
                        table.AddCell(new Phrase(item.Expiry?.ToString("dd/MM/yyyy") ?? "-", normalFont));
                        i++;
                    }

                    document.Add(table);

                    int totalQuantity = reportRows.Sum(x => x.Quantity);
                    document.Add(new Paragraph("\n"));

                    Font totalFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    Paragraph totals = new Paragraph
                    {
                        Alignment = Element.ALIGN_RIGHT,
                        SpacingAfter = 30f
                    };
                    totals.Add(new Chunk($"Total Quantity: {totalQuantity}\n", totalFont));
                    document.Add(totals);

                    document.Close();

                    byte[] pdfBytes = ms.ToArray();
                    return File(pdfBytes, "application/pdf", $"BrandAmountExpiryReport_{DateTime.Now:yyyyMMdd}.pdf");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }
        
        private static bool AdjustExpiryMatches(DateTime? adjExpiry, DateTime? stockExpiry)
        {
            if (!adjExpiry.HasValue && !stockExpiry.HasValue) return true;
            if (!adjExpiry.HasValue || !stockExpiry.HasValue) return false;
            return Math.Abs((adjExpiry.Value - stockExpiry.Value).TotalHours) < 24;
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
