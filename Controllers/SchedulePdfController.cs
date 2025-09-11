using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SchedulePdfController : ControllerBase
    {
        private readonly Context _db;
        private readonly IWebHostEnvironment _host;
        private readonly IMapper _mapper;

        public SchedulePdfController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _host = host;
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("doctor-sales-pdf/{doctorId}")]
        public IActionResult GetDoctorSalesPdf(long doctorId)
        {
            try
            {
                var today = DateTime.Today;
                var schedules = _db.Schedules
                    .Include(s => s.Child)
                    .Include(s => s.Dose)
                        .ThenInclude(d => d.Vaccine)
                    .Include(s => s.Brand)
                        .ThenInclude(b => b.BrandAmounts)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date == today
                            && s.IsDone == true)
                    .OrderBy(s => s.Child.Name) // Order by patient name
                    .ToList();

                if (!schedules.Any())
                    return NotFound("No vaccines administered today");

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    document.Open();

                    // Header setup
                    Font titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    Paragraph title = new Paragraph("Daily Vaccine Sales Report", titleFont);
                    title.Alignment = Element.ALIGN_CENTER;
                    title.SpacingAfter = 20f;
                    document.Add(title);

                    // Create table
                    PdfPTable table = new PdfPTable(6);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1.5f, 2f, 1.5f, 1.2f, 1.2f, 1.2f });

                    // Add headers
                    string[] headers = { "Patient", "Vaccines", "Purchase Value", "Sale Value", "Profit", "Consultation" };
                    foreach (string header in headers)
                    {
                        var cell = new PdfPCell(new Phrase(header, headerFont))
                        {
                            // BackgroundColor = BaseColor.LIGHT_GRAY,
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5
                        };
                        table.AddCell(cell);
                    }

                    string currentPatient = "";
                    int index = 1;
                    decimal totalPurchase = 0;
                    decimal totalSale = 0;
                    decimal totalProfit = 0;

                    foreach (var schedule in schedules)
                    {
                        var brandAmount = schedule.Brand?.BrandAmounts?
                            .FirstOrDefault(ba => ba.DoctorId == doctorId);
                        decimal purchaseAmount = brandAmount?.PurchasedAmt ?? 0;
                        decimal saleAmount = brandAmount?.Amount ?? 0;
                        decimal profit = saleAmount - purchaseAmount;
                        decimal consultation = 0;

                        totalPurchase += purchaseAmount;
                        totalSale += saleAmount;
                        totalProfit += profit;

                        // Add row
                        // table.AddCell(new PdfPCell(new Phrase(index++.ToString(), normalFont))
                        // { HorizontalAlignment = Element.ALIGN_CENTER });

                        // Only show patient name if it's different from the previous row
                        if (currentPatient != schedule.Child.Name)
                        {
                            currentPatient = schedule.Child.Name;
                            table.AddCell(new PdfPCell(new Phrase(currentPatient, normalFont))
                            { HorizontalAlignment = Element.ALIGN_LEFT });
                        }
                        else
                        {
                            table.AddCell(new PdfPCell(new Phrase("", normalFont))
                            { HorizontalAlignment = Element.ALIGN_CENTER });
                        }

                        // table.AddCell(new PdfPCell(new Phrase(schedule.Dose.Vaccine.Name, normalFont))
                        // { HorizontalAlignment = Element.ALIGN_LEFT });

                        table.AddCell(new PdfPCell(new Phrase(schedule.Brand?.Name ?? "", normalFont))
                        { HorizontalAlignment = Element.ALIGN_LEFT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{purchaseAmount:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{saleAmount:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{profit:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });

                        table.AddCell(new PdfPCell(new Phrase($"₹{consultation:N0}", normalFont))
                        { HorizontalAlignment = Element.ALIGN_RIGHT });
                    }

                    // Add totals row
                    var totalCell = new PdfPCell(new Phrase("Totals", headerFont))
                    {
                        Colspan = 4,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        // BackgroundColor = BaseColor.LIGHT_GRAY
                    };
                    table.AddCell(totalCell);

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalPurchase:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalSale:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalProfit:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    table.AddCell(new PdfPCell(new Phrase($"₹{totalProfit:N0}", headerFont))
                    { HorizontalAlignment = Element.ALIGN_RIGHT, });

                    document.Add(table);

                    // Add summary
                    Paragraph summary = new Paragraph(
                        $"\nTotal Patients: {schedules.Select(s => s.Child.Name).Distinct().Count()}" +
                        $"\nTotal Vaccines: {schedules.Count}" +
                        $"\nTotal Purchase Value: {schedules.Count}" +
                        $"\nTotal Sale Value: ₹{totalSale:N0}" +
                        $"\nTotal Profit: ₹{totalProfit:N0}" +
                        $"\nTotal Consultation: ₹{totalPurchase:N0}" +
                        $"\nGrand total cash: ₹{totalSale:N0}",
                        headerFont);
                    summary.SpacingBefore = 20f;
                    document.Add(summary);

                    document.Close();
                    return File(ms.ToArray(), "application/pdf", $"DailySales_{today:yyyyMMdd}.pdf");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }

        [HttpGet("clinic-report-pdf/{clinicId}")]
        public IActionResult GenerateClinicReportPdf(long clinicId, [FromQuery] string fromDate, [FromQuery] string toDate)
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

                var doctorName = clinic.Doctor?.DisplayName ?? "Unknown Doctor";
                var additionalInfo = clinic.Doctor?.AdditionalInfo ?? "No additional info";
                var clinicName = clinic.Name ?? "Unknown Clinic";
                var monogramImage = clinic.MonogramImage ?? "default-monogram.png";
                var address = clinic.Address ?? "Unknown Address";
                var phoneNumber = clinic.PhoneNumber ?? "Unknown Phone Number";

                var schedules = _db
                    .Schedules.Include(s => s.Child)
                    .ThenInclude(c => c.Clinic)
                    .ThenInclude(clinic => clinic.Doctor)
                    .Include(s => s.Dose)
                    .ThenInclude(d => d.Vaccine)
                    .Include(s => s.Brand)
                    .Where(s =>
                        s.Child.ClinicId == clinicId
                        && s.GivenDate.HasValue
                        && s.GivenDate.Value.Date >= parsedFromDate.Date
                        && s.GivenDate.Value.Date <= parsedToDate.Date
                        && s.IsDone == true
                    )
                    .Select(s => new
                    {
                        s.Child.Id,
                        s.Child.Name,
                        s.DoseId,
                        VaccineName = s.Dose.Vaccine.Name,
                        DoseName = s.Dose.Name,
                        GivenDate = s.GivenDate.Value,
                        DoctorName = s.Child.Clinic.Doctor.DisplayName,
                        InvoicePrice = _db.Invoices.Where(i =>
                                i.ChildId == s.ChildId
                                && i.DoctorId == s.Child.Clinic.DoctorId
                                && i.ClinicId == s.Child.ClinicId
                                && i.DoseId == s.DoseId
                            )
                            .Select(i => (decimal?)i.Amount)
                            .FirstOrDefault() ?? 0m,
                        ConsultationFee = _db.Fee.Where(f =>
                                f.InvoiceId
                                == _db.Invoices.Where(i =>
                                        i.ChildId == s.ChildId
                                        && i.DoctorId == s.Child.Clinic.DoctorId
                                        && i.ClinicId == s.Child.ClinicId
                                        && i.DoseId == s.DoseId
                                    )
                                    .Select(i => i.InvoiceId)
                                    .FirstOrDefault()
                            )
                            .Select(f => (decimal?)f.Amount)
                            .FirstOrDefault() ?? 0m,
                        BrandName = s.Brand.Name ?? "Unknown Brand",
                    })
                    .OrderBy(s => s.GivenDate)
                    .ToList();

                if (!schedules.Any())
                {
                    return NotFound("No data found for the specified clinic and date range.");
                }

                var groupedSchedules = schedules
                    .GroupBy(s => new { s.Id, s.Name })
                    .Select(patientGroup => new
                    {
                        Patient = patientGroup.Key,
                        Dates = patientGroup.GroupBy(s => s.GivenDate.Date),
                    });

                using (MemoryStream ms = new MemoryStream())
                {
                    Document document = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter writer = PdfWriter.GetInstance(document, ms);
                    writer.PageEvent = new PdfFooter();
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

                    // Create the cell
                    PdfPCell leftCell = new PdfPCell(phrase)
                    {
                        Border = 0,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 5,
                    };

                    upperTable.AddCell(leftCell);

                    var logoPath = Path.Combine(_host.ContentRootPath, monogramImage);
                    PdfPCell imageCell = new PdfPCell(new Phrase(""))
                    {
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                    };
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
                    upperTable.AddCell(imageCell);

                    document.Add(upperTable);

                    Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                    Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

                    Paragraph titletext = new Paragraph(
                        $"Sales Report",
                        headerFont
                    );
                    titletext.Alignment = Element.ALIGN_CENTER;
                    document.Add(titletext);

                    Paragraph dateRange = new Paragraph(
                        $"FROM {parsedFromDate:dd-MM-yyyy} TO {parsedToDate:dd-MM-yyyy}",
                        normalFont
                    );
                    dateRange.Alignment = Element.ALIGN_CENTER;
                    dateRange.SpacingAfter = 10f;
                    document.Add(dateRange);

                    PdfPTable table = new PdfPTable(6);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1.5f, 2f, 2f, 2f, 1.5f, 2f });

                    string[] headers = { "Date", "Patient", "Consultation Fee", "Item", "Quantity", "Price", };
                    foreach (string header in headers)
                    {
                        var cell = new PdfPCell(new Phrase(header, headerFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 6,
                            BackgroundColor = BaseColor.LightGray,
                        };
                        table.AddCell(cell);
                    }

                    decimal grandTotalConsultationFee = 0;

                    foreach (var patientGroup in groupedSchedules)
                    {
                        decimal totalConsultationForPatient = patientGroup
                            .Dates.SelectMany(d => d)
                            .GroupBy(s => s.ConsultationFee) // Group by ConsultationFee to ensure it's added only once per invoice
                            .Select(g => g.Key) // Get unique ConsultationFee values
                            .Sum();

                        grandTotalConsultationFee += totalConsultationForPatient;

                        decimal totalPriceForPatient = patientGroup.Dates.Sum(d =>
                            d.Sum(s => s.InvoicePrice)
                        );

                        decimal totalPrice = totalPriceForPatient + totalConsultationForPatient;

                        foreach (var dateGroup in patientGroup.Dates)
                        {
                            bool isFirstRowForDate = true;
                            foreach (var schedule in dateGroup)
                            {
                                if (isFirstRowForDate)
                                {
                                    table.AddCell(
                                        new PdfPCell(
                                            new Phrase(
                                                schedule.GivenDate.ToString("dd-MM-yyyy"),
                                                normalFont
                                            )
                                        )
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(
                                            new Phrase(
                                                patientGroup.Patient.Name ?? "Unknown Patient",
                                                headerFont
                                            )
                                        )
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(
                                            new Phrase(
                                                $"₹{schedule.ConsultationFee:N2}",
                                                normalFont
                                            )
                                        )
                                        {
                                            HorizontalAlignment = Element.ALIGN_RIGHT,
                                        }
                                    );

                                    isFirstRowForDate = false;
                                }
                                else
                                {
                                    table.AddCell(
                                        new PdfPCell(new Phrase("", normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(new Phrase("", normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                    table.AddCell(
                                        new PdfPCell(new Phrase("", normalFont))
                                        {
                                            HorizontalAlignment = Element.ALIGN_LEFT,
                                        }
                                    );
                                }

                                table.AddCell(
                                    new PdfPCell(
                                        new Phrase(
                                            schedule.BrandName ?? "Unknown Vaccine",
                                            normalFont
                                        )
                                    )
                                    {
                                        HorizontalAlignment = Element.ALIGN_LEFT,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(new Phrase("1", normalFont))
                                    {
                                        HorizontalAlignment = Element.ALIGN_RIGHT,
                                    }
                                );
                                table.AddCell(
                                    new PdfPCell(
                                        new Phrase($"₹{schedule.InvoicePrice:N2}", normalFont)
                                    )
                                    {
                                        HorizontalAlignment = Element.ALIGN_RIGHT,
                                    }
                                );
                            }
                        }

                        var totalCell = new PdfPCell(
                            new Phrase(
                                $"Total for {patientGroup.Patient.Name}: ₹{totalPrice:N2}",
                                headerFont
                            )
                        )
                        {
                            Colspan = 6,
                            HorizontalAlignment = Element.ALIGN_RIGHT,
                            Padding = 6,
                        };
                        table.AddCell(totalCell);
                    }


                    document.Add(table);

                    Paragraph summary = new Paragraph(
                        $"\nTotal Patients: {groupedSchedules.Count()}"
                            + $"\nTotal Vaccination Fee: ₹{grandTotalConsultationFee:N2}"
                            + $"\nTotal Items Price: ₹{schedules.Sum(s => s.InvoicePrice):N2}"
                            + $"\nGrand Total Cash: ₹{schedules.Sum(s => s.InvoicePrice) + grandTotalConsultationFee:N2}",
                        headerFont
                    );
                    summary.SpacingBefore = 20f;
                    document.Add(summary);

                    document.Close();
                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        $"SalesReport_{clinicId}_{parsedFromDate:yyyyMMdd}_{parsedToDate:yyyyMMdd}.pdf"
                    );
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating PDF: {ex.Message}");
            }
        }

        private PdfPCell CreateCell(
            string text,
            string fontStyle,
            int colspan,
            string alignment,
            string description
        )
        {
            Font font =
                fontStyle == "bold"
                    ? FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)
                    : FontFactory.GetFont(FontFactory.HELVETICA, 10);

            PdfPCell cell = new PdfPCell(new Phrase(text, font))
            {
                Colspan = colspan,
                Border = 0,
                HorizontalAlignment =
                    alignment == "left" ? Element.ALIGN_LEFT : Element.ALIGN_RIGHT,
                Padding = 5,
            };

            return cell;
        }

        public class PdfFooter : PdfPageEventHelper
        {
            public override void OnEndPage(PdfWriter writer, Document document)
            {
                TimeZoneInfo pakistanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
                DateTime pakistanTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pakistanTimeZone);
                string dateTimeStamp = pakistanTime.ToString("yyyy-MM-dd hh:mm tt");
                Font footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                PdfPTable footerTable = new PdfPTable(1);
                footerTable.TotalWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
                footerTable.DefaultCell.Border = Rectangle.NO_BORDER;
                footerTable.DefaultCell.HorizontalAlignment = Element.ALIGN_CENTER;
                footerTable.AddCell(new Phrase($"Printed on: {dateTimeStamp}", footerFont));
                footerTable.WriteSelectedRows(0, -1, document.LeftMargin, document.BottomMargin - 10, writer.DirectContent);
            }
        }
    }
}
