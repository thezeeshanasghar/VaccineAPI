using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using System;
using System.Data;
using System.Threading.Tasks;
using AutoMapper;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
        private readonly IWebHostEnvironment _host;

        public BillController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _db = context;
            _mapper = mapper;
            _host = host;
        }

        [HttpGet]
        public Response<List<BillDTO>> Get()
        {
            var bills = _db.Bills.ToList();
            if (!bills.Any())
                return new Response<List<BillDTO>>(false, "No bills found", null);

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        [HttpGet("doctor/{doctorId}")] // Changed route to avoid conflict
        public Response<List<BillDTO>> GetByDoctor(long doctorId)
        {
            var bills = _db
                .Bills.Include(b => b.Doctor)
                .ThenInclude(d => d.User)
                .Include(b => b.Stocks)
                .ThenInclude(s => s.Brand)
                .Where(b => b.DoctorId == doctorId)
                .ToList();

            if (!bills.Any())
                return new Response<List<BillDTO>>(
                    false,
                    $"No bills found for doctor ID {doctorId}",
                    null
                );

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        // [HttpGet("{id:int}")]  // Added constraint to differentiate from doctorId
        // public Response<BillDTO> Getbyid(int id)
        // {
        //     var bill = _db.Bills
        //         .Include(b => b.Doctor)
        //             .ThenInclude(d => d.User)
        //         .FirstOrDefault(b => b.Id == id);

        //     if (bill == null)
        //         return new Response<BillDTO>(false, "Bill not found", null);

        //     var billDTO = _mapper.Map<BillDTO>(bill);
        //     return new Response<BillDTO>(true, null, billDTO);
        // }

        [HttpGet("{id}")]
        public Response<BillDTO> Getbyid(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            var billDTO = _mapper.Map<BillDTO>(bill);
            return new Response<BillDTO>(true, null, billDTO);
        }

        // [HttpGet("suppliers")]
        // public Response<List<SupplierDTO>> GetSuppliers()
        // {
        //     try
        //     {
        //         var suppliers = _db.Bills
        //             .Where(b => !string.IsNullOrEmpty(b.Supplier))
        //             .Select(b => new SupplierDTO { Name = b.Supplier })
        //             .Distinct()
        //             .OrderBy(s => s.Name)
        //             .ToList();

        //         if (!suppliers.Any())
        //         {
        //             return new Response<List<SupplierDTO>>(false, "No suppliers found", null);
        //         }

        //         return new Response<List<SupplierDTO>>(true, null, suppliers);
        //     }
        //     catch (Exception ex)
        //     {
        //         return new Response<List<SupplierDTO>>(
        //             false,
        //             $"Error retrieving suppliers: {ex.Message}",
        //             null
        //         );
        //     }
        // }

        [HttpGet("clinic/{clinicId}")] // Changed route to avoid conflict
        public Response<List<BillDTO>> GetByClinic(long clinicId)
        {
            var bills = _db
                .Bills.Include(b => b.Doctor)
                .ThenInclude(d => d.User)
                .Include(b => b.Stocks)
                .ThenInclude(s => s.Brand)
                .Where(b => b.ClinicId == clinicId)
                .OrderByDescending(x => x.Id)
                .ToList();

            if (!bills.Any())
                return new Response<List<BillDTO>>(
                    false,
                    $"No bills found for clinic ID {clinicId}",
                    null
                );

            var billDTOs = _mapper.Map<List<BillDTO>>(bills);
            return new Response<List<BillDTO>>(true, null, billDTOs);
        }

        [HttpPost]
        public Response<BillDTO> Post(BillDTO billDTO)
        {
            var bill = _mapper.Map<Bill>(billDTO);
            _db.Bills.Add(bill);
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill created successfully", billDTO);
        }

        [HttpPut("{id}")]
        public Response<BillDTO> Put(int id, BillDTO billDTO)
        {
            if (id != billDTO.Id)
                return new Response<BillDTO>(false, "ID mismatch", null);

            var bill = _mapper.Map<Bill>(billDTO);
            _db.Entry(bill).State = EntityState.Modified;
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill updated successfully", billDTO);
        }

        [HttpDelete("{id}")]
        public Response<BillDTO> Delete(int id)
        {
            var bill = _db.Bills.Find(id);
            if (bill == null)
                return new Response<BillDTO>(false, "Bill not found", null);

            _db.Bills.Remove(bill);
            _db.SaveChanges();
            return new Response<BillDTO>(true, "Bill deleted successfully", null);
        }

        [HttpGet("Suppliers")]
        public Response<IEnumerable<string>> GetSupplierNames()
        {
            try
            {
                // Fetch distinct agent names where Agent is not null/empty and matches the given DoctorId
                var supplierNames = _db
                    .Bills.Where(c => !string.IsNullOrEmpty(c.Supplier))
                    .Select(c => c.Supplier)
                    .Distinct()
                    .ToList();

                if (!supplierNames.Any())
                {
                    return new Response<IEnumerable<string>>(
                        false,
                        "No suppliers found for the specified doctor",
                        null
                    );
                }

                return new Response<IEnumerable<string>>(
                    true,
                    "suppliers retrieved successfully",
                    supplierNames
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving suppliers: {ex.Message}");
                return new Response<IEnumerable<string>>(
                    false,
                    "An error occurred while retrieving suppliers",
                    null
                );
            }
        }

        [HttpPatch("{id}/ispaapprove")]
        public async Task<IActionResult> PatchIsPAApprove(long id)
        {
            try
            {
                var Bill = await _db.Bills.FirstOrDefaultAsync(s => s.Id == id);
                if (Bill == null)
                {
                    return NotFound(new { message = "Bill not found." });
                }
                Bill.IsPAApprove = true;
                await _db.SaveChangesAsync();
                return Ok(new { message = "IsPAApprove updated successfully.", Bill });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> EditBill(long id, [FromBody] BillDTO billDTO)
        {
            if (id != billDTO.Id)
            {
                return BadRequest(new { message = "ID mismatch between route and body." });
            }

            try
            {
                // Validate the clinic exists
                if (billDTO.ClinicId != default)
                {
                    var clinic = await _db.Clinics.FindAsync(billDTO.ClinicId);
                    if (clinic == null)
                    {
                        return NotFound(
                            new { message = $"Clinic with ID {billDTO.ClinicId} not found." }
                        );
                    }
                }

                // Find the bill by ID
                var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == id);
                if (bill == null)
                {
                    return NotFound(new { message = "Bill not found." });
                }

                // Update the fields
                bill.BillNo = billDTO.BillNo ?? bill.BillNo;
                bill.Supplier = billDTO.Supplier?.Trim() ?? bill.Supplier;
                bill.BillDate = billDTO.BillDate != default ? billDTO.BillDate : bill.BillDate;
                bill.IsPaid = billDTO.IsPaid;
                bill.PaidDate = billDTO.PaidDate != default ? billDTO.PaidDate : bill.PaidDate;
                bill.ClinicId = billDTO.ClinicId != default ? billDTO.ClinicId : bill.ClinicId;

                // Save changes
                await _db.SaveChangesAsync();

                return Ok(new { message = "Bill updated successfully.", Bill = bill });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
            }
        }

        // [HttpGet("custom-report-pdf/{entityId}")]
        // public IActionResult GenerateCustomReportPdf(
        //     long entityId,
        //     [FromQuery] string fromDate,
        //     [FromQuery] string toDate
        // )
        // {
        //     try
        //     {
        //         var parsedFromDate = DateTime.Parse(fromDate);
        //         var parsedToDate = DateTime.Parse(toDate);

        //         // Fetch data for the custom report
        //         var entity = _db.Entities.FirstOrDefault(e => e.Id == entityId);
        //         if (entity == null)
        //         {
        //             return NotFound("Entity not found.");
        //         }

        //         var reportData = _db
        //             .CustomTable.Where(r =>
        //                 r.EntityId == entityId && r.Date >= parsedFromDate && r.Date <= parsedToDate
        //             )
        //             .Select(r => new
        //             {
        //                 r.Column1,
        //                 r.Column2,
        //                 r.Column3,
        //                 r.Column4,
        //                 r.Column5,
        //                 r.Column6,
        //                 r.Date,
        //             })
        //             .OrderBy(r => r.Date)
        //             .ToList();

        //         if (!reportData.Any())
        //         {
        //             return NotFound("No data found for the specified entity and date range.");
        //         }

        //         using (MemoryStream ms = new MemoryStream())
        //         {
        //             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
        //             PdfWriter writer = PdfWriter.GetInstance(document, ms);
        //             writer.PageEvent = new PdfFooter(); // Reuse the PdfFooter class for the footer
        //             document.Open();

        //             // Add header information
        //             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
        //             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

        //             Paragraph title = new Paragraph(
        //                 $"Custom Report for Entity: {entity.Name}",
        //                 headerFont
        //             );
        //             title.Alignment = Element.ALIGN_CENTER;
        //             title.SpacingAfter = 10f;
        //             document.Add(title);

        //             Paragraph dateRange = new Paragraph(
        //                 $"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}",
        //                 normalFont
        //             );
        //             dateRange.Alignment = Element.ALIGN_CENTER;
        //             dateRange.SpacingAfter = 20f;
        //             document.Add(dateRange);

        //             // Create the table
        //             PdfPTable table = new PdfPTable(6);
        //             table.WidthPercentage = 100;
        //             table.SetWidths(new float[] { 1.5f, 2f, 2f, 2f, 1.5f, 2f });

        //             // Add table headers
        //             string[] headers =
        //             {
        //                 "Column 1",
        //                 "Column 2",
        //                 "Column 3",
        //                 "Column 4",
        //                 "Column 5",
        //                 "Column 6",
        //             };
        //             foreach (string header in headers)
        //             {
        //                 var cell = new PdfPCell(new Phrase(header, headerFont))
        //                 {
        //                     HorizontalAlignment = Element.ALIGN_CENTER,
        //                     Padding = 6,
        //                     BackgroundColor = BaseColor.LightGray,
        //                 };
        //                 table.AddCell(cell);
        //             }

        //             // Add table data
        //             foreach (var row in reportData)
        //             {
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Column1.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_LEFT,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Column2.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_LEFT,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Column3.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_LEFT,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Column4.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_LEFT,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Column5.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_LEFT,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Column6.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_LEFT,
        //                     }
        //                 );
        //             }

        //             document.Add(table);

        //             // Add summary (optional)
        //             Paragraph summary = new Paragraph(
        //                 $"\nTotal Records: {reportData.Count}",
        //                 headerFont
        //             );
        //             summary.SpacingBefore = 20f;
        //             document.Add(summary);

        //             document.Close();
        //             return File(
        //                 ms.ToArray(),
        //                 "application/pdf",
        //                 $"CustomReport_{entityId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
        //             );
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         return BadRequest($"Error generating PDF: {ex.Message}");
        //     }
        // }

        // public class PdfFooter : PdfPageEventHelper
        // {
        //     public override void OnEndPage(PdfWriter writer, Document document)
        //     {
        //         string footerText = $"Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        //         Font footerFont = FontFactory.GetFont(
        //             FontFactory.HELVETICA,
        //             8,
        //             Font.NORMAL,
        //             BaseColor.Gray
        //         );

        //         PdfPTable footerTable = new PdfPTable(1);
        //         footerTable.TotalWidth =
        //             document.PageSize.Width - document.LeftMargin - document.RightMargin;
        //         footerTable.DefaultCell.Border = Rectangle.NO_BORDER;
        //         footerTable.DefaultCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //         footerTable.AddCell(new Phrase(footerText, footerFont));

        //         footerTable.WriteSelectedRows(
        //             0,
        //             -1,
        //             document.LeftMargin,
        //             document.BottomMargin - 10,
        //             writer.DirectContent
        //         );
        //     }
        // }

        public class PdfFooter : PdfPageEventHelper
        {
            public override void OnEndPage(PdfWriter writer, Document document)
            {
                string dateTimeStamp = DateTime.Now.ToString("yyyy-MM-dd hh:mm tt");
                Font footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                PdfPTable footerTable = new PdfPTable(1);
                footerTable.TotalWidth =
                    document.PageSize.Width - document.LeftMargin - document.RightMargin;
                footerTable.DefaultCell.Border = Rectangle.NO_BORDER;
                footerTable.DefaultCell.HorizontalAlignment = Element.ALIGN_CENTER;
                footerTable.AddCell(new Phrase($"Printed on: {dateTimeStamp}", footerFont));
                footerTable.WriteSelectedRows(0,-1,document.LeftMargin,document.BottomMargin - 10,writer.DirectContent);
            }
        }

        // [HttpGet("clinic-report-pdf/{clinicId}")]
        // public IActionResult GenerateClinicReportPdf(long clinicId,[FromQuery] string fromDate,[FromQuery] string toDate)
        // {
        //     try
        //     {
        //         var parsedFromDate = DateTime.Parse(fromDate);
        //         var parsedToDate = DateTime.Parse(toDate);
        //         var clinic = _db
        //             .Clinics.Include(c => c.Doctor)
        //             .FirstOrDefault(c => c.Id == clinicId);

        //         if (clinic == null)
        //         {
        //             return NotFound("Clinic not found.");
        //         }

        //         var doctorName = clinic.Doctor?.DisplayName ?? "Unknown Doctor";
        //         var additionalInfo = clinic.Doctor?.AdditionalInfo ?? "No additional info";
        //         var clinicName = clinic.Name ?? "Unknown Clinic";
        //         var monogramImage = clinic.MonogramImage ?? "default-monogram.png";
        //         var address = clinic.Address ?? "Unknown Address";
        //         var phoneNumber = clinic.PhoneNumber ?? "Unknown Phone Number";


        //         if (!schedules.Any())
        //         {
        //             return NotFound("No data found for the specified clinic and date range.");
        //         }

        //         var groupedSchedules = schedules
        //             .GroupBy(s => new { s.Id, s.Name })
        //             .Select(patientGroup => new
        //             {
        //                 Patient = patientGroup.Key,
        //                 Dates = patientGroup.GroupBy(s => s.GivenDate.Date),
        //             });

        //         using (MemoryStream ms = new MemoryStream())
        //         {
        //             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
        //             PdfWriter writer = PdfWriter.GetInstance(document, ms);
        //             writer.PageEvent = new PdfFooter();
        //             document.Open();
        //             PdfPTable upperTable = new PdfPTable(2);
        //             float[] upperTableWidths = new float[] { 350f, 160f };
        //             upperTable.HorizontalAlignment = 0;
        //             upperTable.TotalWidth = 510f;
        //             upperTable.LockedWidth = true;
        //             upperTable.SetWidths(upperTableWidths);
        //             Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
        //             Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
        //             Phrase phrase = new Phrase();
        //             phrase.Add(new Chunk(doctorName + "\n", boldFont));
        //             phrase.Add(new Chunk(additionalInfo + "\n", regularFont));
        //             phrase.Add(new Chunk(clinicName + "\n", boldFont));
        //             phrase.Add(new Chunk(address + "\n", regularFont));
        //             phrase.Add(new Chunk(phoneNumber, regularFont));
        //             PdfPCell leftCell = new PdfPCell(phrase)
        //             {
        //                 Border = 0,
        //                 HorizontalAlignment = Element.ALIGN_LEFT,
        //                 Padding = 5,
        //             };

        //             upperTable.AddCell(leftCell);

        //             var logoPath = Path.Combine(_host.ContentRootPath, monogramImage);
        //             PdfPCell imageCell = new PdfPCell(new Phrase(""))
        //             {
        //                 Border = 0,
        //                 FixedHeight = 50f,
        //                 HorizontalAlignment = Element.ALIGN_RIGHT,
        //             };
        //             if (System.IO.File.Exists(logoPath))
        //             {
        //                 var img = Image.GetInstance(logoPath);
        //                 img.ScaleAbsolute(160f, 50f);
        //                 imageCell = new PdfPCell(img, false)
        //                 {
        //                     Border = 0,
        //                     FixedHeight = 50f,
        //                     HorizontalAlignment = Element.ALIGN_RIGHT,
        //                 };
        //             }
        //             upperTable.AddCell(imageCell);

        //             document.Add(upperTable);

        //             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
        //             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);
        //             Paragraph item = new Paragraph(
        //                 "ITEM NAME: BCG",
        //                 normalFont
        //             );
        //             item.Alignment = Element.ALIGN_CENTER;
        //             document.Add(item);

        //             Paragraph itemtext = new Paragraph(
        //                 "ITEM REPORT",
        //                 headerFont
        //             );
        //             itemtext.Alignment = Element.ALIGN_CENTER;
        //             document.Add(itemtext);

        //             Paragraph dateRange = new Paragraph(
        //                 $"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}",
        //                 normalFont
        //             );
        //             dateRange.Alignment = Element.ALIGN_CENTER;
        //             dateRange.SpacingAfter = 10f;
        //             document.Add(dateRange);

        //             PdfPTable table = new PdfPTable(6);
        //             table.WidthPercentage = 100;
        //             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f, 2f });

        //             string[] headers ={"Date","Opening Stock","Sold","Purchased","Adjust","Stock In Hand",};
        //             foreach (string header in headers)
        //             {
        //                 var cell = new PdfPCell(new Phrase(header, headerFont))
        //                 {
        //                     HorizontalAlignment = Element.ALIGN_CENTER,
        //                     Padding = 6,
        //                     BackgroundColor = BaseColor.LightGray,
        //                 };
        //                 table.AddCell(cell);
        //             }

        //             decimal grandTotalConsultationFee = 0;

        //             foreach (var row in reportData)
        //             {
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_CENTER,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.OpeningStock.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_CENTER,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Sold.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_CENTER,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Purchased.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_CENTER,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.Adjust.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_CENTER,
        //                     }
        //                 );
        //                 table.AddCell(
        //                     new PdfPCell(new Phrase(row.StockInHand.ToString(), normalFont))
        //                     {
        //                         HorizontalAlignment = Element.ALIGN_CENTER,
        //                     }
        //                 );
        //             }
        //             document.Add(table);

        //             // Paragraph summary = new Paragraph(
        //             //     $"\nTotal Patients: {groupedSchedules.Count()}"
        //             //         + $"\nTotal Vaccination Fee: ₹{grandTotalConsultationFee:N2}"
        //             //         + $"\nTotal Items Price: ₹{schedules.Sum(s => s.InvoicePrice):N2}"
        //             //         + $"\nGrand Total Cash: ₹{schedules.Sum(s => s.InvoicePrice) + grandTotalConsultationFee:N2}",
        //             //     headerFont
        //             // );
        //             // summary.SpacingBefore = 20f;
        //             // document.Add(summary);

        //             document.Close();
        //             return File(
        //                 ms.ToArray(),
        //                 "application/pdf",
        //                 $"ItemReport_{clinicId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
        //             );
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         return BadRequest($"Error generating PDF: {ex.Message}");
        //     }
        // }

        [HttpGet("brand-stock-report-pdf")]
        public IActionResult GenerateBrandStockReportPdf(
            [FromQuery] long clinicId,
            [FromQuery] long brandId,
            [FromQuery] string fromDate,
            [FromQuery] string toDate
        )
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);

                var clinic = _db
                    .Clinics.Include(c => c.Doctor)
                    .FirstOrDefault(c => c.Id == clinicId);
                if (clinic == null)
                {
                    return NotFound("Clinic not found.");
                }

                var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
                if (brand == null)
                {
                    return NotFound("Brand not found.");
                }

                var doctorName = clinic.Doctor?.DisplayName ?? "Unknown Doctor";
                var additionalInfo = clinic.Doctor?.AdditionalInfo ?? "No additional info";
                var clinicName = clinic.Name ?? "Unknown Clinic";
                var monogramImage = clinic.MonogramImage ?? "default-monogram.png";
                var address = clinic.Address ?? "Unknown Address";
                var phoneNumber = clinic.PhoneNumber ?? "Unknown Phone Number";
                var today = DateTime.Today;

                var brandAmount = _db.BrandAmounts.FirstOrDefault(b =>
                    b.BrandId == brandId && b.ClinicId == clinicId);
                if (brandAmount == null)
                    return NotFound("Brand amount not found.");

                int todaysInventory = brandAmount.Count;

                var schedules = _db
                    .Schedules.Where(s =>
                        s.BrandId == brandId
                        && s.GivenDate >= parsedFromDate
                        && s.GivenDate <= today)
                    .ToList();

                var stockPurchases = _db
                    .Stocks.Join(
                        _db.Bills,
                        stock => stock.BillId,
                        bill => bill.Id,
                        (stock, bill) => new { stock, bill }
                    )
                    .Where(sb =>
                        sb.stock.BrandId == brandId
                        && sb.bill.BillDate >= parsedFromDate
                        && sb.bill.BillDate <= parsedToDate
                    )
                    .GroupBy(sb => sb.bill.BillDate.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

                var vaccineGroups = schedules
                    .GroupBy(s => s.GivenDate)
                    .ToDictionary(g => g.Key, g => g.Count());

                var stockAdjustments = _db
                    .AdjustStocks.Where(a =>
                        a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate
                    )
                    .GroupBy(a => a.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

                var allDates = Enumerable
                    .Range(0, (parsedToDate - parsedFromDate).Days + 1)
                    .Select(offset => parsedFromDate.AddDays(offset))
                    .ToList();

                var reportData =
                    new List<(
                        DateTime Date,
                        int Inventory,
                        int VaccinesDone,
                        int StockPurchased,
                        int StockAdjusted,
                        int StockInHand
                    )>();

                foreach (var date in allDates)
                {
                    int vaccinesDoneToday = vaccineGroups.ContainsKey(date)
                        ? vaccineGroups[date]
                        : 0;
                    int stockPurchasedToday = stockPurchases.ContainsKey(date)
                        ? stockPurchases[date]
                        : 0;
                    int stockAdjustedToday = stockAdjustments.ContainsKey(date)
                        ? stockAdjustments[date]
                        : 0;

                    int totalFutureVaccines = schedules
                        .Where(s => s.GivenDate >= date && s.GivenDate <= today)
                        .Count();

                    int cumulativeStockPurchased = stockPurchases
                        .Where(kvp => kvp.Key >= date && kvp.Key <= today)
                        .Sum(kvp => kvp.Value);

                    int cumulativeStockAdjusted = stockAdjustments
                        .Where(kvp => kvp.Key >= date && kvp.Key <= today)
                        .Sum(kvp => kvp.Value);

                    int inventory =
                        todaysInventory
                        - totalFutureVaccines
                        - cumulativeStockPurchased
                        - cumulativeStockAdjusted;
                    int stockInHand =
                        todaysInventory
                        + totalFutureVaccines
                        + cumulativeStockPurchased
                        + cumulativeStockAdjusted;
                    int cumulativeVaccinesDone = schedules.Where(s => s.GivenDate <= date).Count();

                    int cumulativePurchased = stockPurchases
                        .Where(kvp => kvp.Key <= date)
                        .Sum(kvp => kvp.Value);

                    int cumulativeAdjusted = stockAdjustments
                        .Where(kvp => kvp.Key <= date)
                        .Sum(kvp => kvp.Value);

                    reportData.Add(
                        (
                            date,
                            inventory,
                            vaccinesDoneToday,
                            stockPurchasedToday,
                            stockAdjustedToday,
                            stockInHand
                        )
                    );
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    writer.PageEvent = new PdfFooter(); // Custom footer if you have one
                    document.Open();

                    PdfPTable upperTable = new PdfPTable(2);
                    float[] upperTableWidths = new float[] { 350f, 160f };
                    upperTable.HorizontalAlignment = 0;
                    upperTable.TotalWidth = 510f;
                    upperTable.LockedWidth = true;
                    upperTable.SetWidths(upperTableWidths);

                    Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    Phrase phrase = new Phrase();
                    phrase.Add(new Chunk(doctorName + "\n", boldFont));
                    phrase.Add(new Chunk(additionalInfo + "\n", regularFont));
                    phrase.Add(new Chunk(clinicName + "\n", boldFont));
                    phrase.Add(new Chunk(address + "\n", regularFont));
                    phrase.Add(new Chunk(phoneNumber, regularFont));

                    PdfPCell leftCell = new PdfPCell(phrase)
                    {
                        Border = 0,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 5,
                    };
                    upperTable.AddCell(leftCell);

                    var imageCell = new PdfPCell(new Phrase(""))
                    {
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    };

                    if (!string.IsNullOrEmpty(monogramImage))
                    {
                        var logoPath = Path.Combine(_host.ContentRootPath, monogramImage);
                        if (System.IO.File.Exists(logoPath))
                        {
                            var img = Image.GetInstance(logoPath);
                            img.ScaleAbsolute(160f, 50f);
                            imageCell = new PdfPCell(img, false)
                            {
                                Border = 0,
                                FixedHeight = 50f,
                                HorizontalAlignment = Element.ALIGN_RIGHT,
                            };
                        }
                    }

                    upperTable.AddCell(imageCell);
                    document.Add(upperTable);
                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    document.Add(
                        new Paragraph($"ITEM NAME: {brand?.Name ?? "Unknown"}", normalFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );

                    document.Add(
                        new Paragraph("ITEM REPORT", headerFont)
                        {
                            Alignment = Element.ALIGN_CENTER,
                        }
                    );

                    document.Add(
                        new Paragraph(
                            $"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}",
                            normalFont
                        )
                        {
                            Alignment = Element.ALIGN_CENTER,
                            SpacingAfter = 10f,
                        }
                    );
                    PdfPTable table = new PdfPTable(6) { WidthPercentage = 100 };
                    table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f, 2f });

                    string[] headers =
                    {
                        "Date",
                        "Opening Stock",
                        "Sold",
                        "Purchased",
                        "Adjusted",
                        "Stock In Hand",
                    };
                    foreach (var header in headers)
                    {
                        table.AddCell(
                            new PdfPCell(new Phrase(header, headerFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                                BackgroundColor = BaseColor.LightGray,
                                Padding = 5,
                            }
                        );
                    }

                    foreach (var row in reportData)
                    {
                        if (
                            row.VaccinesDone == 0
                            && row.StockPurchased == 0
                            && row.StockAdjusted == 0
                        )
                        {
                            continue;
                        }
                        table.AddCell(
                            new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase((row.Inventory+row.VaccinesDone).ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont))
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                        table.AddCell(
                            new PdfPCell(
                                new Phrase(
                                    (
                                        row.Inventory
                                        + row.StockPurchased
                                        + row.StockAdjusted
                                    ).ToString(),
                                    normalFont
                                )
                            )
                            {
                                HorizontalAlignment = Element.ALIGN_CENTER,
                            }
                        );
                    }
                    document.Add(table);
                    document.Close();

                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        $"BrandStockReport_Clinic_{clinicId}_Brand_{brandId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
                    );
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
            }
        }

//      [HttpGet("brand-stock-report-pdf1")]
// public IActionResult GenerateBrandStockReportPdf1(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;
//         var today = DateTime.Today;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to today
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= today)
//             .ToList();

//         // Create grouped vaccine count
//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone)>();

//         foreach (var date in allDates)
//         {
//             // Vaccines done on this day
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;

//             // Vaccines done from this day to today (inclusive)
//             int totalFutureVaccines = schedules
//                 .Where(s => s.GivenDate >= date && s.GivenDate <= today)
//                 .Count();

//             int inventory = todaysInventory - totalFutureVaccines;

//             reportData.Add((date, inventory, vaccinesDoneToday));
//         }

//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(3) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }


// //     [HttpGet("brand-stock-report-pdf11")]
// // public IActionResult GenerateBrandStockReportPdf11(
// //     [FromQuery] long clinicId,
// //     [FromQuery] long brandId,
// //     [FromQuery] string fromDate,
// //     [FromQuery] string toDate
// // )
// // {
// //     try
// //     {
// //         var parsedFromDate = DateTime.Parse(fromDate).Date;
// //         var parsedToDate = DateTime.Parse(toDate).Date;
// //         var today = DateTime.Today;

// //         // Get brand
// //         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
// //         if (brand == null)
// //             return NotFound("Brand not found.");

// //         // Get today's inventory
// //         var brandAmount = _db.BrandAmounts
// //             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

// //         if (brandAmount == null)
// //             return NotFound("Brand amount not found.");

// //         int todaysInventory = brandAmount.Count;

// //         // Get all schedules from fromDate to today
// //         var schedules = _db.Schedules
// //             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= today)
// //             .ToList();

// //         var stockPurchases = _db.Stocks
// //             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
// //             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
// //             .GroupBy(sb => sb.bill.BillDate.Date)
// //             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

// //         var vaccineGroups = schedules
// //             .GroupBy(s => s.GivenDate)
// //             .ToDictionary(g => g.Key, g => g.Count());

// //         var stockAdjustments = _db.AdjustStocks
// //             .Where(a => a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate)
// //             .GroupBy(a => a.Date)
// //             .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

// //         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
// //             .Select(offset => parsedFromDate.AddDays(offset))
// //             .ToList();

// //         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone, int StockPurchased, int StockAdjusted)>();

// //         foreach (var date in allDates)
// //         {
// //             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;
// //             int stockPurchasedToday = stockPurchases.ContainsKey(date) ? stockPurchases[date] : 0;
// //             int stockAdjustedToday = stockAdjustments.ContainsKey(date) ? stockAdjustments[date] : 0;

// //             int totalFutureVaccines = schedules
// //                 .Where(s => s.GivenDate >= date && s.GivenDate <= today)
// //                 .Count();

// //             int cumulativeStockPurchased = stockPurchases
// //                 .Where(kvp => kvp.Key >= date && kvp.Key <= today)
// //                 .Sum(kvp => kvp.Value);

// //             int cumulativeStockAdjusted = stockAdjustments
// //                 .Where(kvp => kvp.Key >= date && kvp.Key <= today)
// //                 .Sum(kvp => kvp.Value);

// //             int inventory = todaysInventory - totalFutureVaccines + cumulativeStockPurchased + cumulativeStockAdjusted;

// //             reportData.Add((date, inventory, vaccinesDoneToday, stockPurchasedToday, stockAdjustedToday));
// //         }

// //         // Generate PDF
// //         using (var ms = new MemoryStream())
// //         {
// //             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
// //             PdfWriter.GetInstance(document, ms);
// //             document.Open();

// //             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
// //             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

// //             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
// //             {
// //                 Alignment = Element.ALIGN_CENTER,
// //                 SpacingAfter = 10f
// //             });

// //             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
// //             document.Add(new Paragraph($"Inventory as of Today ({today:dd-MM-yyyy}): {todaysInventory}", normalFont));
// //             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
// //             document.Add(new Paragraph("\n", normalFont));

// //             PdfPTable table = new PdfPTable(5) { WidthPercentage = 100 };
// //             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f });

// //             string[] headers = { "Date", "Inventory", "Vaccines Done", "Stock Purchased", "Stock Adjusted" };
// //             foreach (var header in headers)
// //             {
// //                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
// //                 {
// //                     HorizontalAlignment = Element.ALIGN_CENTER,
// //                     BackgroundColor = BaseColor.LightGray,
// //                     Padding = 5
// //                 });
// //             }

// //             foreach (var row in reportData)
// //             {
// //                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
// //                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
// //                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
// //                 table.AddCell(new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
// //                 table.AddCell(new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
// //             }

// //             document.Add(table);
// //             document.Close();

// //             return File(ms.ToArray(), "application/pdf",
// //                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
// //         }
// //     }
// //     catch (Exception ex)
// //     {
// //         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
// //     }
// // }

// [HttpGet("brand-stock-report-pdf11")]
// public IActionResult GenerateBrandStockReportPdf11(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;
//         var today = DateTime.Today;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to today
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= today)
//             .ToList();

//         var stockPurchases = _db.Stocks
//             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
//             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
//             .GroupBy(sb => sb.bill.BillDate.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var stockAdjustments = _db.AdjustStocks
//             .Where(a => a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate)
//             .GroupBy(a => a.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone, int StockPurchased, int StockAdjusted, int StockInHand)>();

//         foreach (var date in allDates)
//         {
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;
//             int stockPurchasedToday = stockPurchases.ContainsKey(date) ? stockPurchases[date] : 0;
//             int stockAdjustedToday = stockAdjustments.ContainsKey(date) ? stockAdjustments[date] : 0;

//             int totalFutureVaccines = schedules
//                 .Where(s => s.GivenDate >= date && s.GivenDate <= today)
//                 .Count();

//             int cumulativeStockPurchased = stockPurchases
//                 .Where(kvp => kvp.Key >= date && kvp.Key <= today)
//                 .Sum(kvp => kvp.Value);

//             int cumulativeStockAdjusted = stockAdjustments
//                 .Where(kvp => kvp.Key >= date && kvp.Key <= today)
//                 .Sum(kvp => kvp.Value);

//             int inventory = todaysInventory - totalFutureVaccines - cumulativeStockPurchased - cumulativeStockAdjusted;
//             int stockInHand = todaysInventory + totalFutureVaccines + cumulativeStockPurchased + cumulativeStockAdjusted;
//             // New logic for stock in hand (up to current date)
//             int cumulativeVaccinesDone = schedules
//                 .Where(s => s.GivenDate <= date)
//                 .Count();

//             int cumulativePurchased = stockPurchases
//                 .Where(kvp => kvp.Key <= date)
//                 .Sum(kvp => kvp.Value);

//             int cumulativeAdjusted = stockAdjustments
//                 .Where(kvp => kvp.Key <= date)
//                 .Sum(kvp => kvp.Value);

//             // int stockInHand = todaysInventory - cumulativeVaccinesDone + cumulativePurchased + cumulativeAdjusted;

//             reportData.Add((date, inventory, vaccinesDoneToday, stockPurchasedToday, stockAdjustedToday, stockInHand));
//         }

//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30); // Rotated for more width
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(6) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done", "Stock Purchased", "Stock Adjusted", "Stock In Hand" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase((row.Inventory + row.VaccinesDone + row.StockPurchased + row.StockAdjusted).ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }



// [HttpGet("brand-stock-report-pdf12")]
// public IActionResult GenerateBrandStockReportPdf12(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;
//         var today = DateTime.Today;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to today
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= today)
//             .ToList();

//         var stockPurchases = _db.Stocks
//             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
//             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
//             .GroupBy(sb => sb.bill.BillDate.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

//         // Create grouped vaccine count
//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone)>();

//         foreach (var date in allDates)
//         {
//             // Vaccines done on this day
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;

//             // Vaccines done from this day to today (inclusive)
//             int totalFutureVaccines = schedules
//                 .Where(s => s.GivenDate >= date && s.GivenDate <= today)
//                 .Count();

//             int inventory = todaysInventory - totalFutureVaccines;

//             reportData.Add((date, inventory, vaccinesDoneToday));
//         }

//         // Sort reportData in reverse order by date
//         reportData = reportData.OrderByDescending(r => r.Date).ToList();

//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(3) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }

// [HttpGet("brand-stock-report-pdf123")]
// public IActionResult GenerateBrandStockReportPdf123(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to toDate
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= parsedToDate)
//             .ToList();

//         // Get all stock purchases based on BillDate
//         var stockPurchases = _db.Stocks
//             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
//             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
//             .GroupBy(sb => sb.bill.BillDate.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

//         // Get all stock adjustments from fromDate to toDate
//         var stockAdjustments = _db.AdjustStocks
//             .Where(a => a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate)
//             .GroupBy(a => a.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

//         // Create grouped vaccine count
//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone, int StockPurchased, int StockAdjusted)>();

//         foreach (var date in allDates)
//         {
//             // Vaccines done on this day
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;

//             // Stock purchased on this day
//             int stockPurchasedToday = stockPurchases.ContainsKey(date) ? stockPurchases[date] : 0;

//             // Stock adjusted on this day
//             int stockAdjustedToday = stockAdjustments.ContainsKey(date) ? stockAdjustments[date] : 0;

//             // Inventory calculation
//             int inventory = todaysInventory - schedules.Count(s => s.GivenDate <= date);

//             reportData.Add((date, inventory, vaccinesDoneToday, stockPurchasedToday, stockAdjustedToday));
//         }

//         // Sort reportData in reverse order by date
//         reportData = reportData.OrderByDescending(r => r.Date).ToList();

//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({DateTime.Today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(5) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done", "Stock Purchased", "Stock Adjusted" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }

// [HttpGet("brand-stock-report-pdf1234")]
// public IActionResult GenerateBrandStockReportPdf1234(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to toDate
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= parsedToDate)
//             .ToList();

//         // Get all stock purchases based on BillDate
//         var stockPurchases = _db.Stocks
//             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
//             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
//             .GroupBy(sb => sb.bill.BillDate.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

//         // Get all stock adjustments from fromDate to toDate
//         var stockAdjustments = _db.AdjustStocks
//             .Where(a => a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate)
//             .GroupBy(a => a.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

//         // Create grouped vaccine count
//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone, int StockPurchased, int StockAdjusted)>();

//         foreach (var date in allDates)
//         {
//             // Vaccines done on this day
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;

//             // Stock purchased on this day
//             int stockPurchasedToday = stockPurchases.ContainsKey(date) ? stockPurchases[date] : 0;

//             // Stock adjusted on this day
//             int stockAdjustedToday = stockAdjustments.ContainsKey(date) ? stockAdjustments[date] : 0;

//             // Inventory calculation
//             int inventory = todaysInventory - schedules.Count(s => s.GivenDate <= date);

//             reportData.Add((date, inventory, vaccinesDoneToday, stockPurchasedToday, stockAdjustedToday));
//         }

//         // Sort reportData in reverse order by date
//         reportData = reportData.OrderByDescending(r => r.Date).ToList();

//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({DateTime.Today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(5) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done", "Stock Purchased", "Stock Adjusted" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }

// [HttpGet("brand-stock-report-pdf12345")]
// public IActionResult GenerateBrandStockReportPdf12345(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to toDate
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= parsedToDate)
//             .ToList();

//         // Get all stock purchases based on BillDate
//         var stockPurchases = _db.Stocks
//             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
//             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
//             .GroupBy(sb => sb.bill.BillDate.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

//         // Get all stock adjustments from fromDate to toDate
//         var stockAdjustments = _db.AdjustStocks
//             .Where(a => a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate)
//             .GroupBy(a => a.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

//         // Create grouped vaccine count
//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         // reportData = reportData.OrderByDescending(r => r.Inventory).ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone, int StockPurchased, int StockAdjusted)>();

//         foreach (var date in allDates)
//         {
//             // Vaccines done on this day
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;

//             // Stock purchased on this day
//             int stockPurchasedToday = stockPurchases.ContainsKey(date) ? stockPurchases[date] : 0;

//             // Stock adjusted on this day
//             int stockAdjustedToday = stockAdjustments.ContainsKey(date) ? stockAdjustments[date] : 0;

//             // Inventory calculation
//             int inventory = todaysInventory - schedules.Count(s => s.GivenDate <= date);

//             reportData.Add((date, inventory, vaccinesDoneToday, stockPurchasedToday, stockAdjustedToday));
//         }

//         // Sort reportData by Inventory in descending order
//         reportData = reportData.OrderByDescending(r => r.Inventory).ToList();
//         // reportData = reportData.OrderByDescending(r => r.Date).ToList();
//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({DateTime.Today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(5) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done", "Stock Purchased", "Stock Adjusted" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }

// [HttpGet("brand-stock-report-pdf123456")]
// public IActionResult GenerateBrandStockReportPdf123456(
//     [FromQuery] long clinicId,
//     [FromQuery] long brandId,
//     [FromQuery] string fromDate,
//     [FromQuery] string toDate
// )
// {
//     try
//     {
//         var parsedFromDate = DateTime.Parse(fromDate).Date;
//         var parsedToDate = DateTime.Parse(toDate).Date;

//         // Get brand
//         var brand = _db.Brands.FirstOrDefault(b => b.Id == brandId);
//         if (brand == null)
//             return NotFound("Brand not found.");

//         // Get today's inventory
//         var brandAmount = _db.BrandAmounts
//             .FirstOrDefault(b => b.BrandId == brandId && b.ClinicId == clinicId);

//         if (brandAmount == null)
//             return NotFound("Brand amount not found.");

//         int todaysInventory = brandAmount.Count;

//         // Get all schedules from fromDate to toDate
//         var schedules = _db.Schedules
//             .Where(s => s.BrandId == brandId && s.GivenDate >= parsedFromDate && s.GivenDate <= parsedToDate)
//             .ToList();

//         // Get all stock purchases based on BillDate
//         var stockPurchases = _db.Stocks
//             .Join(_db.Bills, stock => stock.BillId, bill => bill.Id, (stock, bill) => new { stock, bill })
//             .Where(sb => sb.stock.BrandId == brandId && sb.bill.BillDate >= parsedFromDate && sb.bill.BillDate <= parsedToDate)
//             .GroupBy(sb => sb.bill.BillDate.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(sb => sb.stock.Quantity));

//         // Get all stock adjustments from fromDate to toDate
//         var stockAdjustments = _db.AdjustStocks
//             .Where(a => a.BrandId == brandId && a.Date >= parsedFromDate && a.Date <= parsedToDate)
//             .GroupBy(a => a.Date)
//             .ToDictionary(g => g.Key, g => g.Sum(a => a.Adjustment));

//         // Create grouped vaccine count
//         var vaccineGroups = schedules
//             .GroupBy(s => s.GivenDate)
//             .ToDictionary(g => g.Key, g => g.Count());

//         var allDates = Enumerable.Range(0, (parsedToDate - parsedFromDate).Days + 1)
//             .Select(offset => parsedFromDate.AddDays(offset))
//             .ToList();

//         var reportData = new List<(DateTime Date, int Inventory, int VaccinesDone, int StockPurchased, int StockAdjusted, int StockInHand)>();

//         foreach (var date in allDates)
//         {
//             // Vaccines done on this day
//             int vaccinesDoneToday = vaccineGroups.ContainsKey(date) ? vaccineGroups[date] : 0;

//             // Stock purchased on this day
//             int stockPurchasedToday = stockPurchases.ContainsKey(date) ? stockPurchases[date] : 0;

//             // Stock adjusted on this day
//             int stockAdjustedToday = stockAdjustments.ContainsKey(date) ? stockAdjustments[date] : 0;

//             // Inventory calculation
//             int inventory = todaysInventory - schedules.Count(s => s.GivenDate <= date);

//             // Stock in hand calculation
//             int stockInHand = inventory + stockPurchasedToday + stockAdjustedToday;

//             reportData.Add((date, inventory, vaccinesDoneToday, stockPurchasedToday, stockAdjustedToday, stockInHand));
//         }

//         // Sort reportData in reverse order by date
//         reportData = reportData.OrderByDescending(r => r.Date).ToList();

//         // Generate PDF
//         using (var ms = new MemoryStream())
//         {
//             Document document = new Document(PageSize.A4, 25, 25, 30, 30);
//             PdfWriter.GetInstance(document, ms);
//             document.Open();

//             Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
//             Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

//             document.Add(new Paragraph("BRAND DAILY STOCK REPORT", headerFont)
//             {
//                 Alignment = Element.ALIGN_CENTER,
//                 SpacingAfter = 10f
//             });

//             document.Add(new Paragraph($"Brand Name: {brand.Name}", normalFont));
//             document.Add(new Paragraph($"Inventory as of Today ({DateTime.Today:dd-MM-yyyy}): {todaysInventory}", normalFont));
//             document.Add(new Paragraph($"Date Range: {parsedFromDate:dd-MM-yyyy} to {parsedToDate:dd-MM-yyyy}", normalFont));
//             document.Add(new Paragraph("\n", normalFont));

//             PdfPTable table = new PdfPTable(6) { WidthPercentage = 100 };
//             table.SetWidths(new float[] { 2f, 2f, 2f, 2f, 2f, 2f });

//             string[] headers = { "Date", "Inventory", "Vaccines Done", "Stock Purchased", "Stock Adjusted", "Stock In Hand" };
//             foreach (var header in headers)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(header, headerFont))
//                 {
//                     HorizontalAlignment = Element.ALIGN_CENTER,
//                     BackgroundColor = BaseColor.LightGray,
//                     Padding = 5
//                 });
//             }

//             foreach (var row in reportData)
//             {
//                 table.AddCell(new PdfPCell(new Phrase(row.Date.ToString("dd-MM-yyyy"), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.Inventory.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.VaccinesDone.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockPurchased.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockAdjusted.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//                 table.AddCell(new PdfPCell(new Phrase(row.StockInHand.ToString(), normalFont)) { HorizontalAlignment = Element.ALIGN_CENTER });
//             }

//             document.Add(table);
//             document.Close();

//             return File(ms.ToArray(), "application/pdf",
//                 $"BrandStock_{brand.Name}_{parsedFromDate:yyyyMMdd}_to_{parsedToDate:yyyyMMdd}.pdf");
//         }
//     }
//     catch (Exception ex)
//     {
//         return BadRequest($"Error generating PDF: {ex.Message}\n{ex.StackTrace}");
//     }
// }
    }
}
