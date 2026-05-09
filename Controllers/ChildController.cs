using System.Globalization;
using System.Net.Mail;
using System.Text;
using AutoMapper;
using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using QRCoder;
using iTextSharpImage = iTextSharp.text.Image;
using iTextSharpFont = iTextSharp.text.Font;
using System.Collections.Generic;
using System.IO;

// using WebApi.Out3Cache.V2;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChildController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
        private readonly IWebHostEnvironment _host;
        public ChildController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _db = context;
            _mapper = mapper;
            _host = host;
        }

        [HttpPut("{id:long}/toggle-active")]
        public ActionResult<Response<ChildDTO>> ToggleActiveStatus(long id)
        {
            try
            {
                var child = _db.Childs
                    .FirstOrDefault(c => c.Id == id);
                if (child == null)
                {
                    return NotFound(new Response<ChildDTO>(false, $"Child not found with ID: {id}", null));
                }

                // Toggle the IsInactive status
                child.IsInactive = !child.IsInactive;

                // Track changes explicitly
                _db.Entry(child).State = EntityState.Modified;

                // Save changes and capture the number of affected rows
                var affectedRows = _db.SaveChanges();

                if (affectedRows > 0)
                {
                    var childDTO = _mapper.Map<ChildDTO>(child);
                    return Ok(new Response<ChildDTO>(true, $"Active status updated successfully for child ID: {id}", childDTO));
                }
                else
                {
                    return StatusCode(500, new Response<ChildDTO>(false, $"Failed to update child with ID: {id}", null));
                }
            }
            catch (Exception ex)
            {
                // Log the full exception details
                Console.WriteLine($"Error in ToggleActiveStatus: {ex}");
                return StatusCode(500, new Response<ChildDTO>(false, $"An error occurred: {ex.Message}", null));
            }
        }

       [HttpGet("invoice-id")]
       public ActionResult<Response<InvoiceDTO>> GetInvoiceId([FromQuery] long doseId, [FromQuery] long childId)
       {
           var invoice = _db.Invoices.FirstOrDefault(i => i.DoseId == doseId && i.ChildId == childId);
           if (invoice != null)
           {
               var invoiceDto = new InvoiceDTO
               {
                   InvoiceId = invoice.InvoiceId,
                   Amount = invoice.Amount,
               };
               return Ok(new Response<InvoiceDTO>(true, $"Invoice found against given Child Id & Dose Id: {doseId} & {childId}", invoiceDto));
           }
           else
           {
               return NotFound(new Response<InvoiceDTO>(false, "Invoice not found for the given DoseId and ChildId.", null));
           }
       }

       [HttpGet("schedule-amount")]
        public ActionResult<Response<decimal>> GetScheduleAmount(long Id, long doseId, long childId)
        {
            var schedule = _db.Schedules
                .FirstOrDefault(s => s.DoseId == doseId && s.ChildId == childId && s.Id == Id);

            if (schedule != null && schedule.Amount != null)
            {
                return Ok(new Response<decimal>(true, "Amount found.", schedule.Amount.Value));
            }
            else
            {
                return NotFound(new Response<decimal>(false, "Amount not found for the given DoseId and ChildId.", 0));
            }
        }
       
       [HttpGet("consultation-fee/{invoiceId}")]
        public ActionResult<decimal> GetConsultationFeeByInvoiceId(string invoiceId)
        {
            var fee = _db.Fee.FirstOrDefault(f => f.InvoiceId == invoiceId);
            if (fee != null)
            {
                var FeeDto = new FeeDTO
                    {
                        InvoiceId = fee.InvoiceId,
                        Amount = fee.Amount,
                    };
                return Ok(new Response<FeeDTO>(true, $"Fee found for invoice id: {invoiceId}", FeeDto));
            }
            else
            {
                return NotFound("Consultation fee not found for the given invoice id.");
            }
        }

        [HttpGet("/forgetemail/{email}")]
        public ActionResult ForgetChildDetailsByEmail(string email)
        {
            try
            {
                if (string.IsNullOrEmpty(email))
                {
                    return BadRequest("Email cannot be null or empty");
                }

                var child = _db.Childs.FirstOrDefault(c => c.Email == email);
                if (child == null)
                {
                    return NotFound("Child not found");
                }


                var userDetails = _db.Users.FirstOrDefault(u => u.Id == child.UserId);
                if (userDetails != null)
                {
                    var body = "Hi " + child.Name + " " + child.FatherName + ",\n" +
                        "Welcome to vaccinationcentre.com\n\n" +
                        "Your account credentials are:\n" +
                        "ID/Mobile Number: " + userDetails.MobileNumber + "\n" +
                        "Password: " + userDetails.Password + "\n" +
                        "Web Link: https://doctor.vaccinationcentre.com/";
                    try
                    {
                        UserEmail.SendEmail(child.Email, body);
                        return Ok("Email sent successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error sending email: " + ex.Message);
                        return StatusCode(500, "Error sending email: " + ex.Message);
                    }
                }
                else
                {
                    return StatusCode(500, "User details not found for the child");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return StatusCode(500, "Internal server error: " + ex.Message);
            }
        }


        [HttpGet]
        public Response<IEnumerable<ChildDTO>> Get()
        {
            var dbChilds = _db.Childs.Include(x => x.User).ToList();
            List<ChildDTO> childDTOs = new List<ChildDTO>();
            foreach (var child in dbChilds)
            {
                ChildDTO childDTO = _mapper.Map<ChildDTO>(child);
                childDTO.CountryCode = child.User.CountryCode;
                childDTO.MobileNumber = child.User.MobileNumber;
                childDTOs.Add(childDTO);
            }

            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }

        [HttpGet("clinic/{id}/{page}")]
        public Response<IEnumerable<ChildDTO>> GetChildByClinic(long id, int page)
        {
            var dbChilds = _db.Childs.Include(x => x.User)
                               .Where(x => x.ClinicId == id)
                               .OrderByDescending(x => x.Id)
                               .Skip(10 * page)
                               .Take(10)
                               .ToList();
            List<ChildDTO> childDTOs = new List<ChildDTO>();
            foreach (var child in dbChilds)
            {
                ChildDTO childDTO = _mapper.Map<ChildDTO>(child);
                childDTO.CountryCode = child.User.CountryCode;
                childDTO.MobileNumber = child.User.MobileNumber;
                childDTOs.Add(childDTO);
            }

            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }


        [HttpGet("user/{id}")]
        public Response<IEnumerable<ChildDTO>> GetChildByUser(long id)
        {
            var dbChilds = _db.Childs.Include(x => x.User).Where(x => x.UserId == id).OrderByDescending(x => x.Id).ToList();
            List<ChildDTO> childDTOs = new List<ChildDTO>();
            foreach (var child in dbChilds)
            {
                ChildDTO childDTO = _mapper.Map<ChildDTO>(child);
                childDTO.CountryCode = child.User.CountryCode;
                childDTO.MobileNumber = child.User.MobileNumber;
                childDTOs.Add(childDTO);
            }

            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }

        [HttpGet("{Id}")]
        public Response<ChildDTO> GetSingle(int Id)
        {
            var dbChild = _db.Childs.Include(c => c.User).Where(c => c.Id == Id).FirstOrDefault();
            if (dbChild == null)
            {
                return new Response<ChildDTO>(false, "Child not found", null);
            }
            ChildDTO childDTO = _mapper.Map<ChildDTO>(dbChild);
            childDTO.CountryCode = dbChild.User.CountryCode;
            childDTO.MobileNumber = dbChild.User.MobileNumber;
            return new Response<ChildDTO>(true, null, childDTO);
        }

       [HttpGet("{id}/schedule")]
       public async Task<Response<IEnumerable<ScheduleDTO>>> GetChildSchedule(int id)
       {
           try
           {
               var child = await _db.Childs
                   .AsNoTracking()
                   .Include(c => c.User)
                   .Include(c => c.Schedules)
                       .ThenInclude(s => s.Dose)  
                   .Include(c => c.Schedules)
                       .ThenInclude(s => s.Brand)
                   .FirstOrDefaultAsync(c => c.Id == id);
       
           if (child == null)
           {
               return new Response<IEnumerable<ScheduleDTO>>(false, "Child not found", null);
           }
           var schedulesDTO = _mapper.Map<List<ScheduleDTO>>(child.Schedules.OrderBy(x => x.Date).ToList());
           return new Response<IEnumerable<ScheduleDTO>>(true, null, schedulesDTO);
           }
           catch (Exception ex)
           {
           return new Response<IEnumerable<ScheduleDTO>>( false, $"An error occurred: {ex.Message}", null);
           }
       }

        [HttpGet("{id}/downloadcsv")]
        public IActionResult MyExportAction(int id)
        {
            var schedule =
                _db.Schedules.Where(x => x.ChildId == id).Include(x => x.Dose).ThenInclude(x => x.Vaccine).ToList();
            DateTime nextvisitDate = getNextDate(schedule);
            var progresses =
                _db.Childs.Include(x => x.User)
                    .Where(x => x.Id == id)
                    .ToList()
                    .Select(progress => new ChildCsvDTO()
                    {
                        Name = progress.Name,
                        FatherName = progress.FatherName,
                        DOB = progress.DOB.ToShortDateString(),
                        City = progress.City,
                        Next_Due_Date = nextvisitDate.ToString("yyyy/MM/dd"),
                        Next_Due_Vaccines = getNextVaccine(schedule, nextvisitDate),
                        Phone = progress.User.MobileNumber,
                        Email = progress.Email
                    });
            var stream = new MemoryStream();
            using (var writeFile = new StreamWriter(stream, Encoding.UTF8, 512, true))
            {
                var csv = new CsvWriter(writeFile, CultureInfo.InvariantCulture);
                csv.WriteRecords(progresses);
                csv.WriteRecords(progresses);
            }
            stream.Position = 0;  // reset stream
            return File(stream, "application/octet-stream", "Reports.csv");
        }


        [HttpGet("downloadcsv")]
        public IActionResult MyExportAction2([FromQuery(Name = "arr[]")] long[] arr)
        {
            List<Child> alerts = new List<Child>();
            var stream = new MemoryStream();
            using (var writeFile = new StreamWriter(stream, Encoding.UTF8, 512, true))
            {
                var csv = new CsvWriter(writeFile, CultureInfo.InvariantCulture);
                foreach (long id in arr)
                {
                    var schedule =
                        _db.Schedules.Where(x => x.ChildId == id).Include(x => x.Dose).ThenInclude(x => x.Vaccine).ToList();
                    DateTime nextvisitDate = getNextDate(schedule);
                    DateTime currentVisitDate = getDueDate(schedule);
                    var progresses = _db.Childs.Include(x => x.User)
                                         .Where(x => x.Id == id)
                                         .ToList()
                                         .Select(progress => new ChildCsvDTO()
                                         {
                                             Name = progress.Name,
                                             FatherName = progress.FatherName,
                                             DOB = progress.DOB.ToShortDateString(),
                                             City = progress.City,
                                             Due_Date = currentVisitDate.ToString("yyyy/MM/dd"),
                                             Due_Vaccines = getDueVaccine(schedule, currentVisitDate),
                                             Next_Due_Date = nextvisitDate.ToString("yyyy/MM/dd"),
                                             Next_Due_Vaccines = getNextVaccine(schedule, nextvisitDate),
                                             Phone = progress.User.MobileNumber,
                                             Email = progress.Email
                                         });
                    csv.WriteRecords(progresses);
                }
            }
            stream.Position = 0;  // reset stream
            return File(stream, "application/octet-stream", "Reports.csv");
        }

        private DateTime getDueDate(List<Schedule> schedul)
        {
            DateTime Now = DateTime.Now;
            foreach (var sch in schedul)
                if (sch.Date.Equals(Now)) return sch.Date;
            return Now;
        }

        private DateTime getNextDate(List<Schedule> schedul)
        {
            DateTime Now = DateTime.Now;
            foreach (var sch in schedul)
                if (sch.Date > Now) return sch.Date;
            return Now;
        }

        private string getDueVaccine(List<Schedule> schedu, DateTime dueDate)
        {
            string dueVaccines = "";
            foreach (var sch in schedu)
                if (sch.Date.Date.Equals(dueDate.Date))  // && sch.ChildId == 7970)
                    dueVaccines += (sch.Dose.Name + ",");
            return dueVaccines;
        }

        private string getNextVaccine(List<Schedule> schedu, DateTime nextDate)
        {
            string nextVaccines = "";
            foreach (var sch in schedu)
                if (sch.Date == nextDate)
                    nextVaccines += (sch.Dose.Name + ",");
            return nextVaccines;
        }

        [HttpGet("{id}/GetChildAgainstMobile")]
        public Response<IEnumerable<ChildDTO>> GetChildAgainstMobile(string id)
        {
            User? user = _db.Users.Where(x => x.MobileNumber == id).FirstOrDefault();
            if (user != null)
            {
                var children = _db.Childs.Where(c => c.UserId == user.Id).ToList();
                IEnumerable<ChildDTO> childDTOs = _mapper.Map<IEnumerable<ChildDTO>>(children);
                return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
            }
            else
            {
                return new Response<IEnumerable<ChildDTO>>(false, "Childs not found", null);
            }
        }

        [HttpGet("{id}/GetCustomScheduleAgainsClinic")]
        public Response<DoctorScheduleDTO> GetCustomScheduleAgainsClinic(int id)
        {
            var clinic = _db.Clinics.Where(c => c.Id == id).FirstOrDefault();
            if (clinic?.Doctor == null)
            {
                return new Response<DoctorScheduleDTO>(false, "Clinic or doctor not found", null);
            }
            var doctorSchedule = clinic.Doctor.DoctorSchedules.FirstOrDefault();
            if (doctorSchedule != null)
            {
                DoctorScheduleDTO doctorScheduleDTO = _mapper.Map<DoctorScheduleDTO>(doctorSchedule);
                return new Response<DoctorScheduleDTO>(true, null, doctorScheduleDTO);
            }
            else
            {
                return new Response<DoctorScheduleDTO>(false, "Custom schedule is not added", null);
            }
        }

        [HttpGet("{id}/ScheduleVerify")]
        public IActionResult DownloadSchedulePDF(int id)
        {
            Child? dbScheduleChild;
            { dbScheduleChild = _db.Childs.Where(x => x.Id == id).FirstOrDefault(); }
            if (dbScheduleChild == null)
            {
                return NotFound("Child not found");
            }
            var stream = CreateSchedulePdf(id);
            var FileName = dbScheduleChild.Name.Replace(" ", "") + "_Schedule_" +
                           DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
            return File(stream, "application/pdf", FileName);
        }

        [HttpGet("{id}/verification-Schedule-PDF")]
        public IActionResult ViewSchedulePDF(int id)
        {
            Child? dbScheduleChild;
            { dbScheduleChild = _db.Childs.Where(x => x.Id == id).FirstOrDefault(); }
            if (dbScheduleChild == null)
            {
                return NotFound("Child not found");
            }
            var stream = CreateSchedulePdf(id);
            var FileName = dbScheduleChild.Name.Replace(" ", "") + "_Schedule_" +
                           DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
            Response.Headers.Add("X-Frame-Options", "ALLOWALL");
            Response.Headers.Add("Content-Disposition", $"inline; filename={FileName}");
            return File(stream, "application/pdf");
        }

        private string GetYearOrMonthFromDaysSchedule(int days)
        {
        var ageMap = new Dictionary<int, string>
          {
        { 0, "At Birth" },
        { 1, "1 Day" },
        { 2, "2 Days" },
        { 3, "3 Days" },
        { 4, "4 Days" },
        { 5, "5 Days" },
        { 6, "6 Days" },
        { 7, "1 Week" },
        { 8, "8 Days" },
        { 9, "9 Days" },
        { 10, "10 Days" },
        { 11, "11 Days" },
        { 12, "12 Days" },
        { 13, "13 Days" },
        { 14, "2 Weeks" },
        { 21, "3 Weeks" },
        { 28, "4 Weeks" },
        { 35, "5 Weeks" },
        { 42, "6 Weeks" },
        { 49, "7 Weeks" },
        { 56, "8 Weeks" },
        { 63, "9 Weeks" },
        { 70, "10 Weeks" },
        { 77, "11 Weeks" },
        { 84, "3 Months" },
        { 91, "13 Weeks" },
        { 98, "14 Weeks" },
        { 105, "15 Weeks" },
        { 112, "16 Weeks" },
        { 119, "17 Weeks" },
        { 126, "18 Weeks" },
        { 133, "19 Weeks" },
        { 140, "20 Weeks" },
        { 147, "21 Weeks" },
        { 154, "22 Weeks" },
        { 161, "23 Weeks" },
        { 168, "6 Months" },
        { 212, "7 Months" },
        { 243, "8 Months" },
        { 274, "9 Months" },
        { 304, "10 Months" },
        { 334, "11 Months" },
        { 365, "1 Year" },
        { 395, "13 Months" },
        { 426, "14 Months" },
        { 456, "15 Months" },
        { 486, "16 Months" },
        { 517, "17 Months" },
        { 547, "18 Months" },
        { 578, "19 Months" },
        { 608, "20 Months" },
        { 639, "21 Months" },
        { 669, "22 Months" },
        { 699, "23 Months" },
        { 730, "2 Years" },
        { 760, "25 Months" },
        { 791, "26 Months" },
        { 821, "27 Months" },
        { 851, "28 Months" },
        { 882, "29 Months" },
        { 912, "30 Months" },
        { 943, "31 Months" },
        { 973, "32 Months" },
        { 1004, "33 Months" },
        { 1034, "34 Months" },
        { 1064, "35 Months" },
        { 1095, "3 Years" },
        { 1125, "37 Months" },
        { 1156, "38 Months" },
        { 1186, "39 Months" },
        { 1216, "40 Months" },
        { 1247, "41 Months" },
        { 1277, "42 Months" },
        { 1308, "43 Months" },
        { 1338, "44 Months" },
        { 1369, "45 Months" },
        { 1399, "46 Months" },
        { 1429, "47 Months" },
        { 1460, "4 Years" },
        { 1490, "49 Months" },
        { 1521, "50 Months" },
        { 1551, "51 Months" },
        { 1582, "52 Months" },
        { 1612, "53 Months" },
        { 1642, "54 Months" },
        { 1673, "55 Months" },
        { 1703, "56 Months" },
        { 1734, "57 Months" },
        { 1764, "58 Months" },
        { 1795, "59 Months" },
        { 1825, "5 Years" },
        { 2190, "6 Years" },
        { 2555, "7 Years" },
        { 2920, "8 Years" },
        { 3285, "9 Years" },
        { 3315, "9 Year 1 Month" },
        { 3650, "10 Years" },
        { 3833, "10 Year 6 Months" },
        { 4015, "11 Years" },
        { 4380, "12 Years" },
        { 4745, "13 Years" },
        { 5110, "14 Years" },
        { 5475, "15 Years" },
        { 5840, "16 Years" },
        { 6205, "17 Years" },
        { 6570, "18 Years" },
        { 6600, "18 Years 1 Month" },
        { 6631, "18 Years 2 Months" },
        { 6661, "18 Years 3 Months" },
        { 6691, "18 Years 4 Months" },
        { 6722, "18 Years 5 Months" },
        { 6752, "18 Years 6 Months" },
        { 6783, "18 Years 7 Months" },
        { 6813, "18 Years 8 Months" },
        { 6843, "18 Years 9 Months" },
        { 6874, "18 Years 10 Months" },
        { 6904, "18 Years 11 Months" },
        { 6935, "19 Years" },
        { 6965, "19 Years 1 Month" },
        { 6996, "19 Years 2 Months" },
        { 7026, "19 Years 3 Months" },
        { 7056, "19 Years 4 Months" },
        { 7087, "19 Years 5 Months" },
        { 7117, "19 Years 6 Months" },
        { 7148, "19 Years 7 Months" },
        { 7178, "19 Years 8 Months" },
        { 7208, "19 Years 9 Months" },
        { 7239, "19 Years 10 Months" },
        { 7269, "19 Years 11 Months" },
        { 7300, "20 Years" },
        { 7665, "21 Years" },
        { 8030, "22 Years" },
        { 8395, "23 Years" },
        { 8760, "24 Years" },
        { 9125, "25 Years" },
        { 30000, "Life Time" }
          };
          var closest = ageMap
              .Where(kvp => kvp.Key <= days)
              .OrderByDescending(kvp => kvp.Key)
              .FirstOrDefault();                             
          return closest.Value ?? $"{days} Days";
        }

        private int GetRowSpanCountForAge(string age, List<Schedule> schedules)
        {
            return schedules.Count(s => GetYearOrMonthFromDaysSchedule(s.Dose.MinAge) == age);
        }

        private Stream CreateSchedulePdf(int childId)
        {
            var dbChild = _db.Childs
                                  .Include(x => x.User)
                                  .Include(x => x.Clinic)
                                  .ThenInclude(x => x.Doctor)
                                  .Where(x => x.Id == childId)
                                  .FirstOrDefault();

            if (dbChild == null) 
            {
                return Stream.Null;
            }

            var dbDoctor = dbChild.Clinic?.Doctor;
            var child = _db.Childs
                                .Include(x => x.Schedules.Where(s => s.IsSkip != true)) // Exclude skipped schedules
                                .ThenInclude(x => x.Dose)
                                .Include(x => x.Schedules.Where(s => s.IsSkip != true))
                                .ThenInclude(x => x.Brand) 
                                .FirstOrDefault(c => c.Id == childId);

            var child1 = _db.Childs
                                .Include(x => x.Clinic)
                                .ThenInclude(x => x.Doctor)
                                .ThenInclude(d => d.DoctorSchedules)
                                .FirstOrDefault(c => c.Id == childId);                    
            if (child == null)
            {
                return Stream.Null;
            }

            var dbSchedules = child.Schedules
            .ToList();

            var Gender = 1;
            if (dbChild.Gender == "Girl") Gender = 2;
            int count = 0;

            var document = new Document(PageSize.A4, 45, 45, 30, 30);
            {
                var output = new MemoryStream();
                var writer = PdfWriter.GetInstance(document, output);
                writer.CloseStream = false;
                writer.PageEvent = new PDFFooter(child);
                document.Open();
                var baseUrl = "https://myapi.vaccinationcentre.com/api";
                var qrCodeUrl = $"{baseUrl}/Child/{childId}/Download-Schedule-PDF";

                try
                {
                    using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeUrl, QRCodeGenerator.ECCLevel.Q))
                    {
                        var qrCode = new BitmapByteQRCode(qrCodeData);
                        byte[] qrCodeImage = qrCode.GetGraphic(18);
                        if (qrCodeImage != null && qrCodeImage.Length > 0)
                        {
                            using (MemoryStream ms = new MemoryStream(qrCodeImage))
                            {
                                var pdfQrCode = iTextSharpImage.GetInstance(ms.ToArray());
                                pdfQrCode.ScaleAbsolute(60f, 60f);
                                float marginLeft = document.PageSize.Width / 2 - pdfQrCode.ScaledWidth / 2;
                                float qrCodeXPosition = marginLeft;
                                float marginTop = 0f - 4f;
                                float qrCodeYPosition = document.PageSize.Height - 100f - marginTop;
                                pdfQrCode.SetAbsolutePosition(qrCodeXPosition, qrCodeYPosition);
                                writer.DirectContent.AddImage(pdfQrCode);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Warning: QR code image for child ID {childId} was null or empty.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating QR code: {ex.Message}");
                }
                PdfPTable upperTable = new PdfPTable(3);
                float[] upperTableWidths = new float[] { 230f, 75f, 230f };
                upperTable.HorizontalAlignment = 0;
                upperTable.TotalWidth = 510f;
                upperTable.LockedWidth = true;
                upperTable.SetWidths(upperTableWidths);
                upperTable.AddCell(CreateCell(dbDoctor?.DisplayName ?? "", "bold", 2, "left", "description"));

                var imgPath = dbChild.Clinic?.MonogramImage != null ? Path.Combine(_host.ContentRootPath, dbChild.Clinic.MonogramImage) : null;
                var logoPath = dbChild.Clinic?.MonogramImage != null ?
                    Path.Combine(_host.ContentRootPath, dbChild.Clinic.MonogramImage) : null;
                PdfPCell imageCell = new PdfPCell(new Phrase(""))
                {
                    Colspan = 1,
                    Rowspan = 2,
                    Border = 0,
                    FixedHeight = 50f,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                if (logoPath != null && System.IO.File.Exists(logoPath))
                {
                    var img = Image.GetInstance(logoPath);
                    img.ScaleAbsolute(160f, 50f);
                    imageCell = new PdfPCell(img, false)
                    {
                        Colspan = 1,
                        Rowspan = 2,
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT
                    };
                }
                upperTable.AddCell(imageCell);

                upperTable.AddCell(CreateCell(dbDoctor?.AdditionalInfo ?? "", "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "right", "description"));
                upperTable.AddCell(CreateCell("", "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "right", "description"));
                upperTable.AddCell(CreateCell("", "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "right", "description"));

                string patientName = child.Name;
                string relation = child.FatherName;
                DateTime dob = child.DOB;
                string passport = child.CNIC;
                string city = child.City;
                string Nationality = child.Nationality;
                string mrNumber = child.City;
                string clinicName = child.Clinic.Name;
                string doctorDetails = child.Clinic.Doctor.DisplayName;
                string additionalInfo = child.Clinic.Doctor.AdditionalInfo;
                string clinicAddress = child.Clinic.Address;
                string clinicPhoneNumber = child.Clinic.PhoneNumber;
                string userPhoneNumber = "+" + dbChild.User.CountryCode + "-" + dbChild.User.MobileNumber;
                string userEmail = child.Email;
                string cnic= child.CNIC;
                document.Add(upperTable);
                Font greenFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11,new BaseColor(0, 100, 0));
                Paragraph title = new Paragraph($"IMMUNIZATION RECORD (MR No: {childId})", greenFont);
                title.Alignment = Element.ALIGN_CENTER;
                document.Add(title);
                var patientTable = new PdfPTable(4) { WidthPercentage = 100 };
                patientTable.SetWidths(new float[] { 2, 2, 2, 2 });
                patientTable.DefaultCell.BorderColor = new BaseColor(159, 226, 191);
                patientTable.DefaultCell.BorderWidth = 0.5f;
                var cellFontBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var cellFontNormal = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                patientTable.AddCell(CreateCell1("Name:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(patientName, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell1("S/D/W/o:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(relation, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell1("Date of Birth:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(dob.ToString("dd/MM/yyyy"), cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell1("Phone No:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(userPhoneNumber, cellFontNormal, BaseColor.White));
                
                // Third row: Passport or CNIC (if available) and City (if available)
                if(cnic!=null && cnic != "")
                {
                    patientTable.AddCell(CreateCell1("Passport or CNIC:", cellFontBold, new BaseColor(159, 226, 191)));
                    patientTable.AddCell(CreateCell1(cnic, cellFontNormal, BaseColor.White));
                    
                    // Add city on same row if available, otherwise add empty cells
                    if(city != null && city != "")
                    {
                        patientTable.AddCell(CreateCell1("City:", cellFontBold, new BaseColor(159, 226, 191)));
                        patientTable.AddCell(CreateCell1(city, cellFontNormal, BaseColor.White));
                    }
                    else
                    {
                        patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                        patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                    }
                }
                else if(city != null && city != "")
                {
                    // If no CNIC/Passport but city exists, show city on third row
                    patientTable.AddCell(CreateCell1("City:", cellFontBold, new BaseColor(159, 226, 191)));
                    patientTable.AddCell(CreateCell1(city, cellFontNormal, BaseColor.White));
                    patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                    patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                }

                document.Add(new Paragraph(" ", FontFactory.GetFont(FontFactory.HELVETICA, 10)) { SpacingBefore = -10f });
                document.Add(patientTable);
                PdfPCell CreateCell1(string text, Font font, BaseColor backgroundColor)
                {
                    var cell = new PdfPCell(new Phrase(text, font))
                    {
                        BackgroundColor = backgroundColor,
                        BorderColor = BaseColor.Gray,
                        BorderWidth = 1f
                    };
                    return cell;
                }
               
                float[] widths = new float[] { 60f, 135f, 80f, 60f, 70f,70f,60f,60f };

                PdfPTable table = new PdfPTable(8);
                table.HorizontalAlignment = 0;
                table.TotalWidth = 510f;
                table.LockedWidth = true;
                table.SpacingBefore = 5;
                table.SetWidths(widths);
                BaseColor lightGreen = new BaseColor(144, 238, 144);
                table.AddCell(CreateCell("Age", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Vaccine", "LightGreen", 1, "center", "scheduleRecords"))                                                                           ;
                table.AddCell(CreateCell("Brand", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Status", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Date", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Weight", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Height", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("OFC/BMI", "LightGreen", 1, "center", "scheduleRecords"));

                var flu1Date = "";
                var flu2Date = "";
                var flu3Date = "";
                var flu1Brand = "";
                var flu2Brand = "";
                var flu3Brand = "";
                var flu1GivenDate = "";
                var flu2GivenDate = "";
                var flu3GivenDate = "";
                string flustatus1 = "";
                string flustatus2 = "";
                string flustatus3 = "";

                var type1Date = "";
                var type2Date = "";
                var type3Date = "";
                var type1Brand = "";
                var type2Brand = "";
                var type3Brand = "";
                var type1GivenDate = "";
                var type2GivenDate = "";
                var type3GivenDate = "";
                string typestatus1 = "";
                string typestatus2 = "";
                string typestatus3 = "";

                var vit1Date = "";
                var vit2Date = "";
                var vit3Date = "";
                var vit1Brand = "";
                var vit2Brand = "";
                var vit3Brand = "";
                var vit1GivenDate = "";
                var vit2GivenDate = "";
                var vit3GivenDate ="";
                string vitstatus1 = "";
                string vitstatus2 = "";
                string vitstatus3 = "";

                bool hasvitDone = false;
                bool hasFluDone = false;
                bool hasTyphoidDone = false;
                bool hasVitaminA = false; // Track if any non-skipped Vitamin A exists
                bool type = false;
                HashSet<string> addedAges = new HashSet<string>();
                string previousAgeLabel = null;
                var infiniteVaccineNames = new[] { "Typhoid", "Flu", "Vitamin A" };
                var selectedInfiniteDoses = infiniteVaccineNames
                    .Select(name =>
                    {
                        var given = dbSchedules
                            .Where(s => s.IsSkip != true && s.IsDone == true 
                            && s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(s => s.GivenDate ?? s.Date)
                            .FirstOrDefault();
                        if (given != null) return given;

                        var due = dbSchedules
                            .Where(s => s.IsSkip != true && s.IsDone == false 
                            && s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(s => s.Date)
                            .FirstOrDefault();
                        return due;
                    })
                    .Where(s => s != null)
                    .ToList();

                var orderedDbSchedules = dbSchedules
                    .Where(s =>
                    {
                        if (s.IsSkip == true)
                            return false;

                        bool isInfinite = infiniteVaccineNames.Any(name =>
                            s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

                        if (isInfinite)
                        {
                            return selectedInfiniteDoses.Any(x => x.Id == s.Id);
                        }
                        return true;
                    })
                    .OrderBy(s => child1?.Clinic?.Doctor?.DoctorSchedules
                        ?.FirstOrDefault(ds => ds.DoseId == s.DoseId)?.GapInDays ?? s.Dose.MinAge)
                    .ToList();

                var lastGivenInfiniteDoses = dbSchedules
                    .Where(s => s.IsSkip != true && s.IsDone == true && infiniteVaccineNames.Any(name =>
                        s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(s => infiniteVaccineNames.First(name =>
                        s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => g.OrderByDescending(s => s.GivenDate ?? s.Date).First())
                    .ToList();

                var dueInfiniteDoses = dbSchedules
                    .Where(s => s.IsSkip != true && s.IsDone == false && infiniteVaccineNames.Any(name =>
                        s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(s => infiniteVaccineNames.First(name =>
                        s.Dose.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => g.OrderBy(s => s.Date).First())
                    .ToList();
                
                var otherInfiniteDoses = lastGivenInfiniteDoses
                    .Concat(dueInfiniteDoses)
                    .OrderBy(s => s.Dose.Name)
                    .ThenBy(s => s.GivenDate ?? s.Date)
                    .ToList();

                var lastTyphoid = lastGivenInfiniteDoses
                    .FirstOrDefault(s => s.Dose.Name.StartsWith("Typhoid", StringComparison.OrdinalIgnoreCase));
                if (lastTyphoid != null)
                {
                    typestatus1 = GetStatus(lastTyphoid);
                    type1GivenDate = lastTyphoid.GivenDate?.ToString("dd/MM/yyyy");
                    type1Brand = lastTyphoid.Brand?.Name ?? "OHF";
                }

                var lastFlu = lastGivenInfiniteDoses
                    .FirstOrDefault(s => s.Dose.Name.StartsWith("Flu", StringComparison.OrdinalIgnoreCase));
                if (lastFlu != null)
                {
                    flustatus1 = GetStatus(lastFlu);
                    flu1GivenDate = lastFlu.GivenDate?.ToString("dd/MM/yyyy");
                    flu1Brand = lastFlu.Brand?.Name ?? "OHF";
                }

                var lastVitA = lastGivenInfiniteDoses
                    .FirstOrDefault(s => s.Dose.Name.StartsWith("Vitamin A", StringComparison.OrdinalIgnoreCase));
                if (lastVitA != null)
                {
                    vitstatus1 = GetStatus(lastVitA);
                    vit1GivenDate = lastVitA.GivenDate?.ToString("dd/MM/yyyy");
                    vit1Brand = lastVitA.Brand?.Name ?? "OHF";
                    hasVitaminA = true; // Mark that we have Vitamin A data
                }

                string GetStatusColor(string status)
                {
                    switch (status)
                    {
                        case "Given": return "#008000"; 
                        case "Missed": return "#FF0000"; 
                        case "Due": return "#808080"; 
                        case "Diseased": return "#808080"; 
                        default: return "#808080";
                    }
                }

                var groupedSchedules = orderedDbSchedules.GroupBy(s =>
                    GetYearOrMonthFromDaysSchedule(
                        child1?.Clinic?.Doctor?.DoctorSchedules
                            ?.FirstOrDefault(ds => ds.DoseId == s.DoseId)?.GapInDays ?? s.Dose.MinAge
                    )
                );
                 Console.WriteLine($"Dose Name: {groupedSchedules.Select(g => g.Key).FirstOrDefault()}");
                
                string GetStatus(Schedule dbSchedule)
                {
                    if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                        return "Given";
                    else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && !checkForMissed(dbSchedule.Date))
                        return "Due";
                    else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && checkForMissed(dbSchedule.Date))
                        return "Missed";
                    else
                        return "Diseased";
                }

               Font GetStatusFont(string status)
                {
                    string colorHex = GetStatusColor(status);
                    // Remove '#' and parse RGB
                    int r = int.Parse(colorHex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    int g = int.Parse(colorHex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
                    int b = int.Parse(colorHex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
                    BaseColor color = new BaseColor(r, g, b);
                    return FontFactory.GetFont(FontFactory.HELVETICA, 11, color);
                }

                var upperTableDates = new HashSet<string>();
                foreach (var group in groupedSchedules)
                {
                bool isFirstRow = true;
                int rowSpanCount = group.Count();
                foreach (var dbSchedule in group)
                {            
                        Paragraph p = new Paragraph();
                        count++;
                        Font font = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                        Font rangevaluefont = FontFactory.GetFont(FontFactory.HELVETICA, 9);
                        Font boldfont = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
                        Font rangevaluefont1 = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                        Font rangefont = FontFactory.GetFont(FontFactory.HELVETICA, 6);
                        Font boldfont1 = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD, new BaseColor(0, 128, 0));
                        Font italicfont = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.ITALIC);
                        Font italicfont1 = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.ITALIC, new BaseColor(255, 0, 0));
                        {
                             if (isFirstRow)
                              {
                                  PdfPCell ageCell = new PdfPCell(new Phrase(group.Key, font));
                                  ageCell.Rowspan = rowSpanCount;
                                  ageCell.VerticalAlignment = Element.ALIGN_MIDDLE;
                                  ageCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                  ageCell.BorderColor = GrayColor.LightGray;
                                  table.AddCell(ageCell);
                                  isFirstRow = false;
                              }
                           
                            PdfPCell dosenameCell = new PdfPCell(new Phrase(dbSchedule.Dose.Name, rangevaluefont));
                            dosenameCell.HorizontalAlignment = Element.ALIGN_LEFT;
                            dosenameCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(dosenameCell);

                            string brandName = "";
                            if (dbSchedule.BrandId != null && dbSchedule.IsDone != false)
                            {
                                brandName = dbSchedule.Brand.Name.ToString();
                            }
                            else if (dbSchedule.BrandId == null && dbSchedule.IsDone != false && dbSchedule.IsDisease != true)
                            {
                                brandName = "OHF*";
                            }

                            PdfPCell brandCell = new PdfPCell(new Phrase(brandName, font));
                            brandCell.HorizontalAlignment = Element.ALIGN_LEFT;
                            brandCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(brandCell);

                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Given", boldfont1));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI == true)
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("By EPI", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true &&
                                        !checkForMissed(dbSchedule.Date))
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Due", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true &&
                                        checkForMissed(dbSchedule.Date))
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Missed", italicfont1));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Diseased", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }

                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.GivenDate?.ToString("dd/MM/yyyy") ?? "", font));
                                dateCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                                string dateStr = dbSchedule.GivenDate?.ToString("dd/MM/yyyy") ?? "";
                                if (!string.IsNullOrEmpty(dateStr))
                                    upperTableDates.Add(dateStr);
                            }
                            else if (dbSchedule.IsDisease == true)
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.Date.Date.ToString("yyyy") + " Y", font));
                                dateCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                                //  string dateStr = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                                // if (!string.IsNullOrEmpty(dateStr))
                                //     upperTableDates.Add(dateStr);
                            }
                            else
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.Date.Date.ToString("dd/MM/yyyy"), font));
                                dateCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                                //  string dateStr = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                                // if (!string.IsNullOrEmpty(dateStr))
                                //     upperTableDates.Add(dateStr);
                            }
                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                DateTime currentDate = DateTime.UtcNow.AddHours(5);
                                var ageInMonths = Convert.ToInt32((dbSchedule.GivenDate?.Date.Year - dbChild.DOB.Date.Year) * 12 +
                                                                    dbSchedule.GivenDate?.Date.Month - dbChild.DOB.Date.Month +
                                                                    (dbSchedule.GivenDate?.Day >= dbChild.DOB.Date.Day ? 0
                                                                    : -1));
                                NormalRange normalrange =
                                    _db.NormalRanges.Where(x => x.Age == ageInMonths && x.Gender == Gender).FirstOrDefault();

                                Paragraph pw = new Paragraph("", rangevaluefont);
                                if (dbSchedule.Weight > 0 && normalrange != null)
                                {
                                    pw.Add(new Chunk(dbSchedule.Weight.ToString(), rangevaluefont));
                                    pw.Add(new Chunk(" (" + normalrange.WeightMin + "-" + normalrange.WeightMax + ")", rangefont));
                                }

                                PdfPCell weightCell = new PdfPCell(pw);
                                weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                weightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(weightCell);

                                Paragraph ph = new Paragraph("", rangevaluefont);
                                if (dbSchedule.Height > 0 && normalrange != null)
                                {
                                    ph.Add(new Chunk(dbSchedule.Height.ToString(), rangevaluefont));
                                    ph.Add(new Chunk(" (" + normalrange.HeightMin + "-" + normalrange.HeightMax + ")", rangefont));
                                }

                                PdfPCell heightCell = new PdfPCell(ph);
                                heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                heightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(heightCell);

                                // Use smaller fonts for OFC/BMI to fit in one line
                                Font ofcValueFont = FontFactory.GetFont(FontFactory.HELVETICA, 7);
                                Font ofcRangeFont = FontFactory.GetFont(FontFactory.HELVETICA, 5);
                                
                                Paragraph pc = new Paragraph("", ofcValueFont);
                                if (dbSchedule.Circle > 0 && normalrange != null && ageInMonths < 25)
                                {
                                    pc.Add(new Chunk(dbSchedule.Circle.ToString(), ofcValueFont));
                                    pc.Add(new Chunk(" (" + normalrange.OfcMin + "-" + normalrange.OfcMax + ")", ofcRangeFont));
                                }

                                if (dbSchedule.Height > 0 && dbSchedule.Weight > 0 && normalrange != null && ageInMonths > 24)
                                {
                                    double BMI = (double)(dbSchedule.Weight / (dbSchedule.Height * dbSchedule.Height / 10000));
                                    BMI = Math.Round(BMI, 1);
                                    pc.Add(new Chunk(BMI.ToString(), ofcValueFont));
                                    pc.Add(new Chunk(" (" + normalrange.OfcMin + "-" + normalrange.OfcMax + ")", ofcRangeFont));
                                }
                                PdfPCell circleCell = new PdfPCell(pc);
                                circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                circleCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(circleCell);
                            }
                            else
                            {
                                PdfPCell weightCell = new PdfPCell(new Phrase("", font));
                                weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                weightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(weightCell);

                                PdfPCell heightCell = new PdfPCell(new Phrase("", font));
                                heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                heightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(heightCell);

                                PdfPCell circleCell = new PdfPCell(new Phrase("", font));
                                circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                circleCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(circleCell);
                            }
                    }
                }

                foreach (var dbSchedule in dueInfiniteDoses)
                {
                    if (dbSchedule.Dose.Name.StartsWith("Flu"))
                    {
                        hasFluDone = true;
                            if (String.IsNullOrEmpty(flu2GivenDate))
                            { 
                                if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                                {
                                    flustatus2 = "Given";
                                }
                                else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && !checkForMissed(dbSchedule.Date))
                                {
                                    flustatus2 = "Due";
                                }
                                else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && checkForMissed(dbSchedule.Date))
                                {
                                   flustatus2 = "Missed";
                                }
                                else
                                {
                                    flustatus2 = "Diseased";
                                }
                                if (dbSchedule.IsDone == true)
                                {
                                    flu2GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                                }
                                else
                                {
                                    flu2GivenDate = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                                }
                                flu2Brand = dbSchedule.Brand?.Name.ToString();
                            }
                    }

                    if (dbSchedule.Dose.Name.StartsWith("Typhoid"))
                    {
                        hasTyphoidDone = true;
                            if (String.IsNullOrEmpty(type2GivenDate))
                            {
                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                typestatus2 = "Given";
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && !checkForMissed(dbSchedule.Date))
                            {
                                typestatus2 = "Due";
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && checkForMissed(dbSchedule.Date))
                            {
                               typestatus2 = "Missed";
                            }
                            else
                            {
                                typestatus2 = "Diseased";
                            }
                            if (dbSchedule.IsDone == true )
                            {
                                type2GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            }
                            else
                            {
                                type2GivenDate = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                            }
                                type2Brand = dbSchedule.Brand?.Name.ToString();
                            }
                    }

                    if (dbSchedule.Dose.Name.StartsWith("Vitamin A"))
                    {
                        hasvitDone = true;
                        hasVitaminA = true; // Mark that we have Vitamin A data
                            if (String.IsNullOrEmpty(vit2GivenDate))
                            {
                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                vitstatus2 = "Given";
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && !checkForMissed(dbSchedule.Date))
                            {
                                vitstatus2 = "Due";
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && checkForMissed(dbSchedule.Date))
                            {
                               vitstatus2 = "Missed";
                            }
                            else
                            {
                                vitstatus2 = "Diseased";
                            }
                            if (dbSchedule.IsDone == true )
                            {
                                vit2GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            }
                            else
                            {
                                vit2GivenDate = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                            }
                                vit2Brand = dbSchedule.Brand?.Name.ToString();
                            }
                    }
                }
                }
                document.Add(table);
                // if ( !string.IsNullOrEmpty(type1GivenDate) ||
                //      !string.IsNullOrEmpty(type2GivenDate) ||
                //      !string.IsNullOrEmpty(type3GivenDate) ||
                //      !string.IsNullOrEmpty(flu1GivenDate) ||
                //      !string.IsNullOrEmpty(flu2GivenDate) ||
                //      !string.IsNullOrEmpty(flu3GivenDate)||
                //      !string.IsNullOrEmpty(vit1GivenDate) ||
                //      !string.IsNullOrEmpty(vit2GivenDate) ||
                //      !string.IsNullOrEmpty(vit3GivenDate))
                // {
                float[] lowerwidths2 = new float[] { 85f,85f, 85f, 85f, 85f, 85f };
                PdfPTable lowertable2 = new PdfPTable(6);
                lowertable2.HorizontalAlignment = 0;
                lowertable2.TotalWidth = 510f;
                lowertable2.LockedWidth = true;
                lowertable2.SpacingBefore = 10;
                lowertable2.SetWidths(lowerwidths2);
                lowertable2.AddCell(CreateCell("", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Last Dose", "LightGreen", 3, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Next Dose", "LightGreen", 2, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Vaccine", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Status", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Date", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Brand", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Status", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Date", "LightGreen", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Flu", "", 1, "center", "scheduleRecords")); 

                if (!string.IsNullOrEmpty(flu1GivenDate)){
                lowertable2.AddCell(new PdfPCell(new Phrase(flustatus1, GetStatusFont(flustatus1))) {
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    FixedHeight = 15f,
                    BorderColor = GrayColor.LightGray,
                    BorderWidth = 1f
                });
                lowertable2.AddCell(CreateCell(flu1GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu1Brand, "", 1, "center", "scheduleRecords"));
                }
                else
                {
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                }
                
                if (hasFluDone && !string.IsNullOrEmpty(flu2GivenDate)){
                    lowertable2.AddCell(new PdfPCell(new Phrase(flustatus2, GetStatusFont(flustatus2))) {
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    FixedHeight = 15f,
                    BorderColor = GrayColor.LightGray,
                    BorderWidth = 1f
                });
                lowertable2.AddCell(CreateCell(flu2GivenDate, "", 1, "center", "scheduleRecords"));
                }
                else
                {
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                }

                lowertable2.AddCell(CreateCell("Typhoid", "", 1, "center", "scheduleRecords")); 
                if (!string.IsNullOrEmpty(type1GivenDate)){
                   lowertable2.AddCell(new PdfPCell(new Phrase(typestatus1, GetStatusFont(typestatus1)))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        FixedHeight = 15f,
                        BorderColor = GrayColor.LightGray, 
                        // BorderWidth = 1f              
                    });
                lowertable2.AddCell(CreateCell(type1GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type1Brand, "", 1, "center", "scheduleRecords"));
                }
                else
                {
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                }

                if (hasTyphoidDone && !string.IsNullOrEmpty(type2GivenDate)){
                lowertable2.AddCell(new PdfPCell(new Phrase(typestatus2, GetStatusFont(typestatus2))) {
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    FixedHeight = 15f,
                    BorderColor = GrayColor.LightGray, 
                });
                lowertable2.AddCell(CreateCell(type2GivenDate, "", 1, "center", "scheduleRecords"));
                }
                else
                {       
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                }

                // Only show Vitamin A row if there are non-skipped Vitamin A doses
                if (hasVitaminA)
                {
                    lowertable2.AddCell(CreateCell("Vitamin A", "", 1, "center", "scheduleRecords"));
                    if (!string.IsNullOrEmpty(vit1GivenDate) && !upperTableDates.Contains(vit2GivenDate)) { 
                    lowertable2.AddCell(new PdfPCell(new Phrase(vitstatus1, GetStatusFont(vitstatus1))) {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        FixedHeight = 15f,
                        BorderColor = GrayColor.LightGray, 
                    });
                    lowertable2.AddCell(CreateCell(vit1GivenDate, "", 1, "center", "scheduleRecords"));
                    lowertable2.AddCell(CreateCell(vit1Brand, "", 1, "center", "scheduleRecords"));
                    }
                    else
                    {
                        lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                        lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                        lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    }

                    if (!string.IsNullOrEmpty(vit2GivenDate)){ 
                    lowertable2.AddCell(new PdfPCell(new Phrase(vitstatus2, GetStatusFont(vitstatus2))) {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        FixedHeight = 15f
                    });
                    lowertable2.AddCell(CreateCell(vit2GivenDate, "", 1, "center", "scheduleRecords"));
                    }
                    else
                    {
                        lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                        lowertable2.AddCell(CreateCell("", "", 1, "center", "scheduleRecords"));
                    }
                }
                document.Add(lowertable2);
                // }
                document.Close();
                output.Seek(0, SeekOrigin.Begin);
                return output;
            }
        }

        [HttpGet("{id}/Download-Schedule-PDF")]
        public IActionResult GenerateVerifySchedule(int id)
        {
            var child = _db.Childs
                .Include(c => c.Schedules.Where(s => s.IsSkip != true)) // Exclude skipped schedules
                    .ThenInclude(s => s.Dose)
                .Include(c => c.Schedules.Where(s => s.IsSkip != true))
                    .ThenInclude(s => s.Brand)
                .Include(c => c.Clinic)
                    .ThenInclude(cl => cl.Doctor)
                .FirstOrDefault(c => c.Id == id);

            if (child == null)
            {
                return NotFound($"Child with ID {id} not found");
            }

            var allSchedules = child.Schedules.OrderBy(s => s.Date).ToList();

           var fileUrl = $"https://myapi.vaccinationcentre.com/api/Child/{id}/Verification-Schedule-PDF";

            var vaccineRows = new StringBuilder();
            foreach (var schedule in allSchedules)
            {
                string status;
                if (schedule.IsDone == true && schedule.IsDisease != true && schedule.Due2EPI != true)
                {
                    status = "Given";
                }
                else if (schedule.IsDone == true && schedule.IsDisease != true && schedule.Due2EPI == true)
                {
                    status = "By EPI";
                }
                else if (schedule.IsDone == false && schedule.IsDisease != true && !checkForMissed(schedule.Date))
                {
                    status = "Due";
                }
                else if (schedule.IsDone == false && schedule.IsDisease != true && checkForMissed(schedule.Date))
                {
                    status = "Missed";
                }
                else
                {
                    status = "Diseased";
                }

                vaccineRows.Append($@"
            <tr>
                <td>{schedule.Dose?.Name}</td>
                <td>{status}</td>
                <td>{schedule.Brand?.Name}</td>
                <td>{schedule.Manufacturer}</td>
                <td>{schedule.Lot}</td>
                <td>{(schedule.IsDone == true ? schedule.GivenDate?.ToString("dd MMM yyyy") : schedule.Date.ToString("dd MMM yyyy"))}</td>
                <td>{GetYearOrMonthFromDays((int?)schedule.Validity ?? 0)}</td>
            </tr>");
            }

            string htmlContent = $@"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='UTF-8'>
                <title>Vaccination Record</title>
                <style>
                    body {{
                        font-family: Arial, sans-serif;
                        background-color: #f4f4f4;
                        padding: 20px;
                        margin: 0;
                    }}
                    .container {{
                        max-width: 800px;
                        margin: auto;
                        background-color: #fff;
                        padding: 20px;
                        border-radius: 10px;
                        box-shadow: 0 0 10px rgba(0,0,0,0.1);
                    }}
                    table {{
                        width: 100%;
                        border-collapse: collapse;
                        margin-bottom: 20px;
                    }}
                    th, td {{
                        padding: 6px;
                        border: 1px solid #ddd;
                        text-align: left;
                    }}
                    .success-box {{
                        background-color: #d4edda;
                        padding: 10px 15px;
                        border-left: 5px solid #28a745;
                        border-radius: 5px;
                        margin-bottom: 20px;
                    }}
                    /* REMOVING .fetch-btn style as it's no longer used */
                </style>
            </head>
            <body>
                <div class='container'>
                    <!-- Fetch Record Link (styled with radio button appearance) -->
                    <div style='padding: 15px; border-bottom: 1px solid #ddd;'>
                        <input type='radio' checked style='margin-right: 10px;'> <a href='{fileUrl}' target='_blank' style='text-decoration: none; color: inherit;'>Click here to fetch record</a>
                    </div>

                    <!-- Status -->
                    <div class='success-box'>
                        <strong>Status:</strong> {(child.IsInactive == true ? "Inactive" : "Vaccinated")}
                    </div>

                    <!-- Patient Info -->
                    <table>
                        <tr><td><strong>MR No.</strong></td><td>{child.Id}</td></tr>
                        <tr><td><strong>Name</strong></td><td>{child.Name}</td></tr>
                        <tr><td><strong>S/D/W/o</strong></td><td>{child.FatherName}</td></tr>
                        <tr><td><strong>Passport/CNIC</strong></td><td>{child.CNIC}</td></tr>
                        <tr><td><strong>City</strong></td><td>{child.City}</td></tr>
                    </table>

                    <!-- Vaccine Table -->
                    <h3>Vaccines</h3>
                    <table>
                        <thead>
                            <tr>
                                <th>Vaccine</th>
                                <th>Status</th>
                                <th>Brand</th>
                                <th>Manufacturer</th>
                                <th>Batch/Lot</th>
                                <th>Date</th>
                                <th>Validity</th>
                            </tr>
                        </thead>
                        <tbody>
                            {vaccineRows}
                        </tbody>
                    </table>

                    <!-- Doctor Info -->
                    <p><strong>Physician/Doctor:</strong> {child.Clinic?.Doctor?.DisplayName} - {child.Clinic?.Doctor?.AdditionalInfo}</p>
                    <p><strong>Center:</strong> {child.Clinic?.Name} ({child.Clinic?.RegNo})</p>
                </div>
            </body>
            </html>";

            return new ContentResult
            {
                Content = htmlContent,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        [HttpGet("{id}/CustomVerify")]
        public IActionResult DownloadCustomPDF(int id)
        {
            Child dbScheduleChild;
            { dbScheduleChild = _db.Childs.Where(x => x.Id == id).FirstOrDefault(); }
            var stream = CreateCustomPdf(id);
            var FileName = dbScheduleChild.Name.Replace(" ", "") + "_Schedule_" +
                           DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
            return File(stream, "application/pdf", FileName);
        }

        [HttpGet("{id}/verification-Custom-PDF")]
        public IActionResult ViewCustomPDF(int id)
        {
            Child dbScheduleChild;
            { dbScheduleChild = _db.Childs.Where(x => x.Id == id).FirstOrDefault(); }
            var stream = CreateCustomPdf(id);
            var FileName = dbScheduleChild.Name.Replace(" ", "") + "_Schedule_" +
                           DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
            Response.Headers.Add("X-Frame-Options", "ALLOWALL");
            Response.Headers.Add("Content-Disposition", $"inline; filename={FileName}");
            return File(stream, "application/pdf");
        }

        private Stream CreateCustomPdf(int childId)
        {
            var dbChild = _db.Childs
                                  .Include(x => x.User)
                                  .Include(x => x.Clinic)
                                  .ThenInclude(x => x.Doctor)
                                  .ThenInclude(x => x.User)
                                  .Where(x => x.Id == childId)
                                  .FirstOrDefault();
            if (dbChild == null) 
            {
                return null;
            }
            var dbDoctor = dbChild.Clinic?.Doctor;
            var child = _db.Childs
                                .Include(x => x.Schedules.Where(s => s.IsSkip != true)) // Exclude skipped schedules
                                .ThenInclude(x => x.Dose)
                                .Include(x => x.Schedules.Where(s => s.IsSkip != true))
                                .ThenInclude(x => x.Brand)
                                .FirstOrDefault(c => c.Id == childId);
            var dbSchedules = child.Schedules.ToList();
            var Gender = 1;
            if (dbChild.Gender == "Girl") Gender = 2;
            foreach (var sch in dbSchedules)
            {
                if (sch.IsDone == true) sch.Date = sch.GivenDate ?? DateTime.Now;
            }
            dbSchedules = dbSchedules.OrderBy(x => x.Date).ToList();
            int count = 0;
            var document = new Document(PageSize.A4, 45, 45, 30, 30);
            {
                var output = new MemoryStream();
                var writer = PdfWriter.GetInstance(document, output);
                writer.CloseStream = false;
                writer.PageEvent = new PDFFooter(child);
                document.Open();
                // QR Code URL
                var baseUrl = "https://myapi.vaccinationcentre.com/api";
                var qrCodeUrl = $"{baseUrl}/Child/{childId}/Download-Custom-PDF";
                try
                {
                    using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeUrl, QRCodeGenerator.ECCLevel.Q))
                    {
                        var qrCode = new BitmapByteQRCode(qrCodeData);
                        byte[] qrCodeImage = qrCode.GetGraphic(18);
                        if (qrCodeImage != null && qrCodeImage.Length > 0)
                        {
                            using (MemoryStream ms = new MemoryStream(qrCodeImage))
                            {
                                var pdfQrCode = iTextSharpImage.GetInstance(ms.ToArray());
                                pdfQrCode.ScaleAbsolute(60f, 60f);
                                float marginLeft = document.PageSize.Width / 2 - pdfQrCode.ScaledWidth / 2;
                                float qrCodeXPosition = marginLeft;
                                float marginTop = 0f - 4f;
                                float qrCodeYPosition = document.PageSize.Height - 100f - marginTop;
                                pdfQrCode.SetAbsolutePosition(qrCodeXPosition, qrCodeYPosition);
                                writer.DirectContent.AddImage(pdfQrCode);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Warning: QR code image for child ID {childId} was null or empty.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating QR code: {ex.Message}");
                }
                PdfPTable upperTable = new PdfPTable(3);
                float[] upperTableWidths = new float[] { 230f, 75f, 230f };
                upperTable.HorizontalAlignment = 0;
                upperTable.TotalWidth = 510f;
                upperTable.LockedWidth = true;
                upperTable.SetWidths(upperTableWidths);
                upperTable.AddCell(CreateCell(dbDoctor?.DisplayName ?? "", "bold", 2, "left", "description"));
                var imgPath = dbChild.Clinic?.MonogramImage != null ? Path.Combine(_host.ContentRootPath, dbChild.Clinic.MonogramImage) : null;
                // Handle clinic logo
                var logoPath = dbChild.Clinic?.MonogramImage != null ?
                    Path.Combine(_host.ContentRootPath, dbChild.Clinic.MonogramImage) : null;
                PdfPCell imageCell = new PdfPCell(new Phrase(""))
                {
                    Colspan = 1,
                    Rowspan = 2,
                    Border = 0,
                    FixedHeight = 50f,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                if (logoPath != null && System.IO.File.Exists(logoPath))
                {
                    var img = Image.GetInstance(logoPath);
                    img.ScaleAbsolute(160f, 50f);
                    imageCell = new PdfPCell(img, false)
                    {
                        Colspan = 1,
                        Rowspan = 2,
                        Border = 0,
                        FixedHeight = 50f,
                        HorizontalAlignment = Element.ALIGN_RIGHT
                    };
                }
                upperTable.AddCell(imageCell);
                upperTable.AddCell(CreateCell(dbDoctor?.AdditionalInfo ?? "", "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "right", "description"));
                upperTable.AddCell(CreateCell("", "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "right", "description"));
                upperTable.AddCell(CreateCell("", "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "right", "description"));
                string patientName = child.Name;
                string relation = child.FatherName;
                DateTime dob = child.DOB;
                string passport = child.CNIC;
                string city = child.City;
                string Nationality = child.Nationality;
                string mrNumber = child.City;
                string clinicName = child.Clinic.Name;
                string doctorDetails = child.Clinic.Doctor.DisplayName;
                string additionalInfo = child.Clinic.Doctor.AdditionalInfo;
                string clinicAddress = child.Clinic.Address;
                string clinicPhoneNumber = child.Clinic.PhoneNumber;
                string userPhoneNumber = "+" + dbChild.User.CountryCode + "-" + dbChild.User.MobileNumber;
                string userEmail = child.Email;
                string cnic= child.CNIC;
                document.Add(upperTable);
                Font greenFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11,new BaseColor(0, 100, 0));
                Paragraph title = new Paragraph("IMMUNIZATION RECORD", greenFont);
                title.Alignment = Element.ALIGN_CENTER;
                document.Add(title);
                var patientTable = new PdfPTable(4) { WidthPercentage = 100 };
                patientTable.SetWidths(new float[] { 2, 2, 2, 2 });
                patientTable.DefaultCell.BorderColor = new BaseColor(159, 226, 191);
                patientTable.DefaultCell.BorderWidth = 0.5f;
                var cellFontBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var cellFontNormal = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                patientTable.AddCell(CreateCell1("Name:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(patientName, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell1("S/D/W/o:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(relation, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell1("Date of Birth:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(dob.ToString("dd/MM/yyyy"), cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell1("Phone No:", cellFontBold, new BaseColor(159, 226, 191)));
                patientTable.AddCell(CreateCell1(userPhoneNumber, cellFontNormal, BaseColor.White));
                
                // Third row: Passport or CNIC (if available) and City (if available)
                if(cnic!=null && cnic != "")
                {
                    patientTable.AddCell(CreateCell1("Passport / CNIC:", cellFontBold, new BaseColor(159, 226, 191)));
                    patientTable.AddCell(CreateCell1(cnic, cellFontNormal, BaseColor.White));
                    
                    // Add city on same row if available, otherwise add empty cells
                    if(city != null && city != "")
                    {
                        patientTable.AddCell(CreateCell1("City:", cellFontBold, new BaseColor(159, 226, 191)));
                        patientTable.AddCell(CreateCell1(city, cellFontNormal, BaseColor.White));
                    }
                    else
                    {
                        patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                        patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                    }
                }
                else if(city != null && city != "")
                {
                    // If no CNIC/Passport but city exists, show city on third row
                    patientTable.AddCell(CreateCell1("City:", cellFontBold, new BaseColor(159, 226, 191)));
                    patientTable.AddCell(CreateCell1(city, cellFontNormal, BaseColor.White));
                    patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                    patientTable.AddCell(CreateCell1("", cellFontNormal, BaseColor.White));
                }
                else{}
                document.Add(new Paragraph(" ", FontFactory.GetFont(FontFactory.HELVETICA, 10)) { SpacingBefore = -10f });
                document.Add(patientTable);
                PdfPCell CreateCell1(string text, Font font, BaseColor backgroundColor)
                {
                    var cell = new PdfPCell(new Phrase(text, font))
                    {
                        BackgroundColor = backgroundColor,
                        BorderColor = BaseColor.Gray,
                        BorderWidth = 1f
                    };
                    return cell;
                }
                float[] widths = new float[] { 20f, 145f, 50f, 70, 70f, 60f, 60f, 60f };
                PdfPTable table = new PdfPTable(8);
                table.HorizontalAlignment = 0;
                table.TotalWidth = 510f;
                table.LockedWidth = true;
                table.SpacingBefore = 5;
                table.SetWidths(widths);
                BaseColor lightGreen = new BaseColor(144, 238, 144);
                table.AddCell(CreateCell("Sr", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Vaccine", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Status", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Date", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Brand", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Weight", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Height", "LightGreen", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("OFC/BMI", "LightGreen", 1, "center", "scheduleRecords"));
                foreach (var dbSchedule in dbSchedules)
                {
                    if (dbSchedule.IsSkip != true
                    //  && !dbSchedule.Dose.Name.StartsWith("Flu") &&
                    //     !dbSchedule.Dose.Name.StartsWith("Typhoid")
                        )
                    {
                        int doseCount = 0;
                        Paragraph p = new Paragraph();
                        count++;
                        doseCount++;
                        Font font = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                        Font rangevaluefont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                        Font rangefont = FontFactory.GetFont(FontFactory.HELVETICA, 6);
                        Font boldfont = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
                        Font boldfont1 = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD, new BaseColor(0, 128, 0));
                        Font italicfont = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.ITALIC);
                        Font italicfont1 = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.ITALIC, new BaseColor(255, 0, 0));
                        {
                            PdfPCell ageCell = new PdfPCell(new Phrase(count.ToString(), font));
                            ageCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            ageCell.FixedHeight = 15f;
                            ageCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(ageCell);
                            PdfPCell dosenameCell = new PdfPCell(new Phrase(dbSchedule.Dose.Name, font));
                            dosenameCell.HorizontalAlignment = Element.ALIGN_LEFT;
                            dosenameCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(dosenameCell);
                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Given", boldfont1));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI == true)
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("By EPI", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true &&
                                        !checkForMissed(dbSchedule.Date))
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Due", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true &&
                                        checkForMissed(dbSchedule.Date))
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Missed", italicfont1));
                                statusCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Diseased", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy"), font));
                                dateCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                            }
                            else if (dbSchedule.IsDisease == true)
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.Date.Date.ToString("yyyy") + " Y", font));
                                dateCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                            }
                            else
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.Date.Date.ToString("dd/MM/yyyy"), font));
                                dateCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                            }
                            string brandName = " ";
                            if (dbSchedule.BrandId != null && dbSchedule.IsDone != false)
                            {
                                brandName = dbSchedule.Brand.Name.ToString();
                            }
                            else if (dbSchedule.BrandId == null && dbSchedule.IsDone != false && dbSchedule.IsDisease != true)
                            {
                                brandName = "OHF*";
                            }
                            PdfPCell brandCell = new PdfPCell(new Phrase(brandName, font));
                            brandCell.HorizontalAlignment = Element.ALIGN_LEFT;
                            brandCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(brandCell);
                            if (dbSchedule.IsDone == true && dbSchedule.IsDisease != true && dbSchedule.Due2EPI != true)
                            {
                                DateTime currentDate = DateTime.UtcNow.AddHours(5);
                                var ageInMonths = Convert.ToInt32((dbSchedule.GivenDate?.Date.Year - dbChild.DOB.Date.Year) * 12 +
                                                                    dbSchedule.GivenDate?.Date.Month - dbChild.DOB.Date.Month +
                                                                    (dbSchedule.GivenDate?.Day >= dbChild.DOB.Date.Day ? 0
                                                                    : -1));
                                NormalRange normalrange =
                                    _db.NormalRanges.Where(x => x.Age == ageInMonths && x.Gender == Gender).FirstOrDefault();
                                Paragraph pw = new Paragraph("", rangevaluefont);
                                if (dbSchedule.Weight > 0 && normalrange != null)
                                {
                                    pw.Add(new Chunk(dbSchedule.Weight.ToString(), rangevaluefont));
                                    pw.Add(new Chunk(" (" + normalrange.WeightMin + "-" + normalrange.WeightMax + ")", rangefont));
                                }
                                PdfPCell weightCell = new PdfPCell(pw);
                                weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                weightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(weightCell);
                                Paragraph ph = new Paragraph("", rangevaluefont);
                                if (dbSchedule.Height > 0 && normalrange != null)
                                {
                                    ph.Add(new Chunk(dbSchedule.Height.ToString(), rangevaluefont));
                                    ph.Add(new Chunk(" (" + normalrange.HeightMin + "-" + normalrange.HeightMax + ")", rangefont));
                                }
                                PdfPCell heightCell = new PdfPCell(ph);
                                heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                heightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(heightCell);
                                Paragraph pc = new Paragraph("", rangevaluefont);
                                if (dbSchedule.Circle > 0 && normalrange != null && ageInMonths < 25)
                                {
                                    pc.Add(new Chunk(dbSchedule.Circle.ToString(), rangevaluefont));
                                    pc.Add(new Chunk(" (" + normalrange.OfcMin + "-" + normalrange.OfcMax + ")", rangefont));
                                }
                                // FOR BMI
                                if (dbSchedule.Height > 0 && dbSchedule.Weight > 0 && normalrange != null && ageInMonths > 24)
                                {
                                    double BMI = (double)(dbSchedule.Weight / (dbSchedule.Height * dbSchedule.Height / 10000));
                                    BMI = Math.Round(BMI, 1);
                                    pc.Add(new Chunk(BMI.ToString(), rangevaluefont));
                                    pc.Add(new Chunk(" (" + normalrange.OfcMin + "-" + normalrange.OfcMax + ")", rangefont));
                                }
                                PdfPCell circleCell = new PdfPCell(pc);
                                circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                circleCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(circleCell);
                            }
                            else
                            {
                                PdfPCell weightCell = new PdfPCell(new Phrase("", font));
                                weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                weightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(weightCell);
                                PdfPCell heightCell = new PdfPCell(new Phrase("", font));
                                heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                heightCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(heightCell);
                                PdfPCell circleCell = new PdfPCell(new Phrase("", font));
                                circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                circleCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(circleCell);
                            }
                        }
                    }
                }
                document.Add(table);
                document.Close();
                output.Seek(0, SeekOrigin.Begin);
                return output;
            }
        }

        [HttpGet("{Id}/Download-Custom-PDF")]
        public IActionResult GenerateVerifyCustomPdf(int Id)
        {
            var fileUrl = $"https://myapi.vaccinationcentre.com/api/Child/{Id}/verification-Custom-PDF";

            string htmlContent = $@"
                                    <!DOCTYPE html>
                                    <html lang='en'>
                                    <head>
                                        <meta charset='UTF-8'>
                                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                                        <title>QR Code Viewer</title>
                                    </head>
                                    <body style='font-family: Arial, sans-serif; text-align: center; padding: 20px;'>
                                        <h1>Immunization Record</h1>
                                        <p><a href='{fileUrl}' target='_blank'>click here</a> to view the details.</p>
                                        <iframe src='{fileUrl}' width='600' height='700' style='border: 1px solid #ccc;'></iframe>
                                    </body>
                                    </html>";

            return new ContentResult
            {
                Content = htmlContent,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        [HttpGet("check-for-missed")]
        public bool checkForMissed(DateTime DueDate)
        {
            DateTime todayDate = DateTime.Now;
            if (todayDate > DueDate)
                return true;
            else
                return false;
        }

        [HttpGet("{keyword}/search")]
        public Response<IEnumerable<ChildDTO>> SearchChildren(string keyword)
        {
            {
                List<Child> dbChildrenResults = new List<Child>();
                List<ChildDTO> childDTOs = new List<ChildDTO>();

                dbChildrenResults = _db.Childs
                                        .Where(c => c.Name.ToLower().Contains(keyword.ToLower()) ||
                                                    c.FatherName.ToLower().Contains(keyword.ToLower()))
                                        .ToList();
                childDTOs.AddRange(_mapper.Map<List<ChildDTO>>(dbChildrenResults));

                foreach (var item in childDTOs)
                {
                    item.MobileNumber = dbChildrenResults.Where(x => x.Id == item.Id).FirstOrDefault().User.MobileNumber;
                }

                return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
            }
        }

        [HttpGet("search")]
        public Response<IEnumerable<ChildDTO>> SearchChildrenByCity(
            [FromQuery] string name = "", [FromQuery] string city = "", [FromQuery] string fromdob = "",
            [FromQuery] string todob = "", [FromQuery] string gender = "", [FromQuery] string vaccineid = "",
            [FromQuery] string doctorid = "")
        {
            List<Child> dbChildrenResults = new List<Child>();

            // if (!String.IsNullOrEmpty (vaccineid) && !String.IsNullOrEmpty (doctorid))
            // {
            //     var dose = _db.Doses.Where(x => x.VaccineId == Convert.ToInt32(vaccineid)).FirstOrDefault();
            //     if (dose != null ) {
            //     schedules = _db.Schedules.Where(x=>x.DoseId == dose.Id).Include(x => x.Child).ToList();
            //     dbChildrenResults = schedules.Select(x=>x.Child).ToList ();
            //     }
            // }
            // List<Child> dbChildrenResults = _db.Childs.Include (x => x.User).ToList ();
            List<ChildDTO> childDTOs = new List<ChildDTO>();
            List<Schedule> schedules = new List<Schedule>();
            List<Clinic> clinics = new List<Clinic>();
            Dose dose = new Dose();
            Doctor doctor = new Doctor();
            if (!String.IsNullOrEmpty(name))
                dbChildrenResults = dbChildrenResults
                                        .Where(c => c.Name.ToLower().Contains(name.Trim().ToLower()) ||
                                                    c.FatherName.ToLower().Contains(name.Trim().ToLower()))
                                        .ToList();

            if (!String.IsNullOrEmpty(city))
                dbChildrenResults =
                    dbChildrenResults.Where(c => c.City != null && c.City.ToLower().Contains(city.Trim().ToLower())).ToList();

            if (!String.IsNullOrEmpty(fromdob) && !String.IsNullOrEmpty(todob))
                dbChildrenResults =
                    dbChildrenResults
                        .Where(c => c.DOB >= Convert.ToDateTime(fromdob).Date && c.DOB <= Convert.ToDateTime(todob).Date)
                        .ToList();

            if (!String.IsNullOrEmpty(gender)) dbChildrenResults = dbChildrenResults.Where(c => c.Gender == gender).ToList();

            if (!String.IsNullOrEmpty(vaccineid))
            {
                // dose = _db.Doses.Where(x => x.VaccineId == Convert.ToInt32(vaccineid)).FirstOrDefault();
                // if (dose != null ) {
                // schedules = _db.Schedules.Where(x=>x.DoseId == dose.Id).Include(x => x.Child).ToList();
                // dbChildrenResults = schedules.Select(x=>x.Child).ToList ();
                // }
            }

            if (!String.IsNullOrEmpty(doctorid))
            {
                doctor = _db.Doctors.Where(x => x.Id == Convert.ToInt32(doctorid)).Include(x => x.Clinics).FirstOrDefault();
                clinics = doctor.Clinics.ToList();
                foreach (var clinic in clinics)
                {
                    dbChildrenResults = dbChildrenResults.Where(c => c.ClinicId == clinic.Id).ToList();
                }
            }

            childDTOs.AddRange(_mapper.Map<List<ChildDTO>>(dbChildrenResults));

            foreach (var item in childDTOs)
            {
                item.MobileNumber = dbChildrenResults.Where(x => x.Id == item.Id).FirstOrDefault().User.MobileNumber;
            }

            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }

        [HttpPost]
        public Response<ChildDTO> Post(ChildDTO childDTO)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            childDTO.Name = textInfo.ToTitleCase(childDTO.Name);
            childDTO.FatherName = textInfo.ToTitleCase(childDTO.FatherName);
            {
                Child childDB = _mapper.Map<Child>(childDTO);
                childDB.IsPAApprove = childDTO.IsPAApprove;
                childDB.CreatedAt = DateTime.UtcNow;
                childDB.AddedByPaId = childDTO.AddedByPaId;
                User user = _db.Users.Where(x => x.MobileNumber == childDTO.MobileNumber && x.UserType == "PARENT").FirstOrDefault();
                if (user == null)
                {
                    User userDB = new User();
                    userDB.MobileNumber = childDTO.MobileNumber;
                    userDB.Password = childDTO.Password;
                    userDB.CountryCode = childDTO.CountryCode;
                    userDB.UserType = "PARENT";
                    _db.Users.Add(userDB);
                    _db.SaveChanges();
                    childDB.UserId = userDB.Id;
                    _db.Childs.Add(childDB);
                    _db.SaveChanges();
                }
                else
                {
                    childDTO.Password = user.Password;
                    Child existingChild = _db.Childs.FirstOrDefault(x => x.Name.Equals(childDTO.Name) && x.UserId == user.Id);
                    if (existingChild != null)
                        return new Response<ChildDTO>(
                            false,
                            "Children with same name & number already exists. Parent should login and start change doctor process.",
                            null);
                    childDB.UserId = user.Id;
                    _db.Childs.Add(childDB);
                    _db.SaveChanges();
                }
                childDTO.Id = childDB.Id;
                if (childDTO.Type == "regular")
                {
                    Clinic clinic = _db.Clinics.Where(x => x.Id == childDTO.ClinicId).Include(x => x.Doctor).FirstOrDefault();
                    Doctor doctor = clinic.Doctor;
                    List<DoctorSchedule> dss = _db.DoctorSchedules
                        .Where(x => x.DoctorId == doctor.Id)
                        .Include(x => x.Dose)
                        .ThenInclude(d => d.Vaccine)
                        .OrderBy(x => x.Dose.VaccineId)
                        .ThenBy(x => x.Dose.DoseOrder)
                        .ToList();
                    // Track last scheduled date per vaccine so Dose 2+ can use MinGap from previous dose
                    var lastDateByVaccineId = new Dictionary<long, DateTime>();
                    foreach (DoctorSchedule ds in dss)
                    {
                        var dbDose = ds.Dose;
                        if (dbDose == null) continue;
                        {
                            Schedule cvd = new Schedule();
                            cvd.ChildId = childDTO.Id;
                            cvd.DoseId = ds.DoseId;
                            if (childDTO.Gender == "Boy" && dbDose.Name.StartsWith("HPV"))
                                continue;

                            if (childDTO.IsSkip == true && ds.IsActive != true)
                                continue;

                            if (childDTO.IsEPIDone)
                            {
                                var dob = childDTO.DOB.Date;
                                DateTime comparisonDate2002 = DateTime.Parse("01/01/2002");
                                DateTime comparisonDate2009 = DateTime.Parse("01/01/2009");
                                DateTime comparisonDate2015 = DateTime.Parse("01/01/2015");
                                DateTime comparisonDate2021 = DateTime.Parse("01/04/2021");
                                if (dob < comparisonDate2002)
                                {
                                    if (dbDose.Name.Equals("OPV/IPV+HBV+DPT+Hib 1"))
                                    {
                                        cvd.DoseId = 130;
                                        ds.GapInDays = 0;
                                    }
                                }
                                else if (dob > comparisonDate2021)
                                {
                                    if (dbDose.Name.Equals("OPV/IPV+HBV+DPT+Hib 1"))
                                    {
                                        cvd.DoseId = 135;
                                        ds.GapInDays = 0;
                                    }
                                }
                                else if (dob > comparisonDate2002 && dob < comparisonDate2009)
                                {
                                    if (dbDose.Name.Equals("OPV/IPV+HBV+DPT+Hib 1"))
                                    {
                                        cvd.DoseId = 131;
                                        ds.GapInDays = 0;
                                    }
                                }
                                else if (dob > comparisonDate2009 && dob < comparisonDate2015)
                                {
                                    if (dbDose.Name.Equals("OPV/IPV+HBV+DPT+Hib 1"))
                                    {
                                        cvd.DoseId = 132;
                                        ds.GapInDays = 0;
                                    }
                                }
                                else
                                {
                                    cvd.DoseId = ds.DoseId;
                                }
                            }
                            if (dbDose.Name.StartsWith("HPV") && dbDose.DoseOrder == 3) cvd.IsSkip = true;

                            // Dose 2+: schedule from previous dose date + MinGap
                            // Dose 1: schedule from DOB + GapInDays (absolute minimum age)
                            if (dbDose.DoseOrder > 1 && dbDose.MinGap.HasValue && dbDose.MinGap.Value > 0
                                && lastDateByVaccineId.ContainsKey(dbDose.VaccineId))
                            {
                                cvd.Date = calculateDate(lastDateByVaccineId[dbDose.VaccineId], dbDose.MinGap.Value);
                            }
                            else
                            {
                                cvd.Date = calculateDate(childDTO.DOB, ds.GapInDays);
                            }
                            lastDateByVaccineId[dbDose.VaccineId] = cvd.Date;

                            cvd.DiseaseYear = "";
                            _db.Schedules.Add(cvd);
                            _db.SaveChanges();
                        }
                    }
                    var dob2 = childDTO.DOB.Date;
                    DateTime comparisonDate2012 = DateTime.Parse("01/01/2012");
                    DateTime comparisonDate2018 = DateTime.Parse("01/01/2018");
                    DateTime comparisonDate2020 = DateTime.Parse("01/01/2020");
                    if (childDTO.IsEPIDone)
                    {
                        if (dob2 > comparisonDate2020)
                        {
                            Schedule cvd3 = new Schedule();
                            cvd3.DoseId = 136;
                            var mingap = 0;
                            cvd3.DiseaseYear = "";
                            cvd3.Date = calculateDate(childDTO.DOB, mingap);
                            cvd3.ChildId = childDTO.Id;
                            _db.Schedules.Add(cvd3);
                        }
                        else if (dob2 > comparisonDate2018)
                        {
                            Schedule cvd2 = new Schedule();
                            cvd2.DoseId = 134;
                            var mingap = 0;
                            cvd2.DiseaseYear = "";
                            cvd2.Date = calculateDate(childDTO.DOB, mingap);
                            cvd2.ChildId = childDTO.Id;
                            _db.Schedules.Add(cvd2);
                        }
                        else if (dob2 > comparisonDate2012)
                        {
                            Schedule cvd1 = new Schedule();
                            cvd1.DoseId = 133;
                            var mingap = 0;
                            cvd1.DiseaseYear = "";
                            cvd1.Date = calculateDate(childDTO.DOB, mingap);
                            cvd1.ChildId = childDTO.Id;
                            _db.Schedules.Add(cvd1);
                        }
                        else
                        {
                            ;
                        }
                    }
                    _db.SaveChanges();
                }
                Child c = _db.Childs.Where(x => x.Id == childDTO.Id)
                              .Include(x => x.User)
                              .Include(x => x.Clinic)
                              .Include(x => x.Clinic.Doctor.User)
                              .FirstOrDefault();
                try
                {
                    if (c.Email != "") UserEmail.ParentEmail(c);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                return new Response<ChildDTO>(true, null, childDTO);
            }
        }

        [HttpPost("followup")]
        public Response<List<FollowUpDTO>> GetFollowUp(FollowUpDTO followUpDto)
        {
            {
                if (followUpDto.DoctorId < 1)
                {
                    var dbChild = _db.Childs.Include("Clinic").FirstOrDefault();
                    followUpDto.DoctorId = dbChild.Clinic.DoctorId;
                }
                var dbFollowUps = _db.FollowUps.Include(x => x.Child)
                                      .Where(f => f.DoctorId == followUpDto.DoctorId && f.ChildId == followUpDto.ChildId)
                                      .OrderByDescending(x => x.CurrentVisitDate)
                                      .ToList();
                List<FollowUpDTO> followUpDTOs = _mapper.Map<List<FollowUpDTO>>(dbFollowUps);
                return new Response<List<FollowUpDTO>>(true, null, followUpDTOs);
            }
        }

        private static void GetPDFHeading(Document document, String headingText)
        {
            Font ColFont = FontFactory.GetFont(FontFactory.HELVETICA, 26, Font.BOLD);
            Chunk chunkCols = new Chunk(headingText, ColFont);
            Paragraph chunkParagraph = new Paragraph();
            chunkParagraph.Alignment = Element.ALIGN_CENTER;
            chunkParagraph.Add(chunkCols);
            document.Add(chunkParagraph);
            document.Add(new Paragraph(""));
            document.Add(new Chunk("\n"));
        }

        protected PdfPCell CreateCell(string value, string color, int colpan, string alignment, string table)
        {
            Font font = FontFactory.GetFont(FontFactory.HELVETICA, 11);
            if (color == "bold" || color == "backgroudLightGray")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11);
                font.Size = 11;
            }

            if (table == "inwordamount")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA, 11, Font.ITALIC);
            }

            if (color == "unbold")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA, 11);
            }

            if (color == "sitetitle")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA, 16);
            }

            // if (table != "description" && color != "backgroudLightGray") {
            //     font.Size = 7;
            // }
            PdfPCell cell = new PdfPCell(new Phrase(value, font));
            cell.BorderColor = GrayColor.LightGray;
            if (color == "backgroudLightGray")
            {
                cell.BackgroundColor = new BaseColor(224, 218, 218);

                //  cell.BackgroundColor = GrayColor.LightGray;
                cell.FixedHeight = 20f;
            }
            if (color == "LightGreen")
            {
                cell.BackgroundColor = new BaseColor(159, 226, 191);
                cell.FixedHeight = 20f;
            }
            if (alignment == "right")
            {
                cell.HorizontalAlignment = Element.ALIGN_RIGHT;
            }
            if (alignment == "left")
            {
                cell.HorizontalAlignment = Element.ALIGN_LEFT;
            }
            if (alignment == "center")
            {
                cell.HorizontalAlignment = Element.ALIGN_CENTER;
            }
            cell.Colspan = colpan;
            if (table == "description")
            {
                cell.Border = 0;
                cell.Padding = 2f;
            }
            if (table == "scheduleRecords")
            {
                cell.FixedHeight = 15f;
            }

            if (table == "invoiceRecords" || table == "inwordamount")
            {
                cell.FixedHeight = 18f;
            }

            return cell;
        }

        protected PdfPCell CreateInvoiceCell(string value, string color, int colpan, int rowspan, string alignment)
        {
            Font font = FontFactory.GetFont(FontFactory.HELVETICA, 11);
            if (color == "bold" || color == "backgroudLightGray")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11);
                font.Size = 11;
            }

            if (color == "unbold")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA, 11);
            }

            if (color == "sitetitle")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA, 16);
            }

            PdfPCell cell = new PdfPCell(new Phrase(value, font));
            if (color == "backgroudLightGray")
            {
                cell.BackgroundColor = GrayColor.LightGray;
                cell.FixedHeight = 20f;
            }
            if (color == "LightGreen")
            {
                cell.BackgroundColor = new BaseColor(224, 218, 218);
                cell.FixedHeight = 20f;
            }
    
            if (alignment == "right")
            {
                cell.HorizontalAlignment = Element.ALIGN_RIGHT;
            }
            if (alignment == "left")
            {
                cell.HorizontalAlignment = Element.ALIGN_LEFT;
            }
            if (alignment == "center")
            {
                cell.HorizontalAlignment = Element.ALIGN_CENTER;
            }
            cell.Colspan = colpan;
            cell.Rowspan = rowspan;
            return cell;
        }

        [HttpGet("{Id}/{IsBrand}/{IsConsultationFee}/{InvoiceDate}/{DoctorId}/Download-Invoice-PDF")]
        public IActionResult DownloadInvoicePDF(int Id, bool IsBrand, bool IsConsultationFee, DateTime InvoiceDate,
                                                int DoctorId)
        {
            Stream stream;
            decimal amount = 0.00M;
            int count = 1;
            int col = 3;
            decimal consultaionFee = 0.00M;
            string childName = "";
            var document = new Document(PageSize.A4, 50, 50, 25, 25);

            var output = new MemoryStream();

            var writer = PdfWriter.GetInstance(document, output);
            writer.CloseStream = false;

            document.Open();

            GetPDFHeading(document, "INVOICE");
            var dbDoctor = _db.Doctors.Where(x => x.Id == DoctorId).Include(x => x.User).FirstOrDefault();
            dbDoctor.InvoiceNumber = (dbDoctor.InvoiceNumber > 0) ? dbDoctor.InvoiceNumber + 1 : 1;
            var dbChild = _db.Childs.Include("Clinic").Where(x => x.Id == Id).FirstOrDefault();
            var dbSchedules = _db.Schedules.Include(x => x.Dose)
                                  .ThenInclude(x => x.Vaccine)
                                  .Include("Brand")
                                  .Where(x => x.ChildId == Id)
                                  .ToList();
            childName = dbChild.Name;
            PdfPTable upperTable = new PdfPTable(2);
            float[] upperTableWidths = new float[] { 250f, 250f };
            upperTable.HorizontalAlignment = 0;
            upperTable.TotalWidth = 500f;
            upperTable.LockedWidth = true;

            // upperTable.DefaultCell.PaddingLeft = 4;
            upperTable.SetWidths(upperTableWidths);

            upperTable.AddCell(CreateCell("Dr " + dbDoctor.DisplayName, "bold", 1, "left", "description"));
            upperTable.AddCell(CreateCell("Invoice # " + dbDoctor.InvoiceNumber, "", 1, "right", "description"));
            upperTable.AddCell(CreateCell(dbDoctor.Qualification, "", 1, "left", "description"));

            // upperTable.AddCell(CreateCell("Date: " + DateTime.UtcNow.AddHours(5), "", 1, "right", "description"));
            upperTable.AddCell(CreateCell("Date: " + InvoiceDate.ToString("dd-MM-yyyy"), "", 1, "right", "description"));
            upperTable.AddCell(CreateCell(dbDoctor.AdditionalInfo, "", 1, "left", "description"));
            upperTable.AddCell(CreateCell("Bill To: " + dbChild.Name, "bold", 1, "right", "description"));

            upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "", 1, "left", "description"));

            // upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "noColor", 1, "left",
            // "description"));
            upperTable.AddCell(CreateCell("", "", 1, "right", "description"));

            if (IsConsultationFee)
            {
                consultaionFee = (int)dbChild.Clinic.ConsultationFee;
            }

            upperTable.AddCell(CreateCell("", "", 1, "left", "description"));
            upperTable.AddCell(CreateCell("", "", 1, "right", "description"));
            upperTable.AddCell(CreateCell("P: " + dbDoctor.PhoneNo, "", 1, "left", "description"));
            upperTable.AddCell(CreateCell("", "", 1, "right", "description"));
            upperTable.AddCell(CreateCell("M: " + dbDoctor.User.MobileNumber, "", 1, "left", "description"));
            upperTable.AddCell(CreateCell("", "", 1, "right", "description"));

            document.Add(upperTable);
            document.Add(new Paragraph(""));
            document.Add(new Chunk("\n"));

            // 2nd Table
            float[] widths = new float[] { 30f, 200f, 100f };
            if (IsBrand)
            {
                col = 4;
                widths = new float[] { 30f, 200f, 150f, 100f };
            }

            PdfPTable table = new PdfPTable(col);

            // table.WidthPercentage = 100;
            table.HorizontalAlignment = 0;
            table.TotalWidth = 500f;
            table.LockedWidth = true;
            table.SetWidths(widths);

            table.AddCell(CreateCell("#", "backgroudLightGray", 1, "center", "invoiceRecords"));
            table.AddCell(CreateCell("Item", "backgroudLightGray", 1, "center", "invoiceRecords"));
            if (IsBrand)
            {
                table.AddCell(CreateCell("Brand", "backgroudLightGray", 1, "center", "invoiceRecords"));
            }
            table.AddCell(CreateCell("Amount", "backgroudLightGray", 1, "center", "invoiceRecords"));

            // Rows
            table.AddCell(CreateCell(count.ToString(), "", 1, "center", "invoiceRecords"));

            // col = (col > 3) ? col - 3 : col-2;
            if (col - 2 < 2)
            {
                table.AddCell(CreateCell("Consultation Fee", "", col - 2, "center", "invoiceRecords"));
            }
            else
            {
                table.AddCell(CreateCell("Consultation Fee", "", 1, "center", "invoiceRecords"));
                table.AddCell(CreateCell("------------------", "", 1, "center", "invoiceRecords"));
            }
            table.AddCell(CreateCell(consultaionFee.ToString(), "", 1, "right", "invoiceRecords"));
            if (dbSchedules.Count != 0)
            {
                foreach (var schedule in dbSchedules)
                {
                    // date is static due to date conversion issue
                    //  && schedule.Date.Date == DateTime.Now.Date
                    // when we add bulk injection we don't add brandId in schedule
                    if (schedule.IsDone && schedule.BrandId > 0)
                    {
                        count++;
                        table.AddCell(CreateCell(count.ToString(), "", 1, "center", "invoiceRecords"));
                        table.AddCell(CreateCell(schedule.Dose.Vaccine.Name, "", 1, "center", "invoiceRecords"));
                        if (IsBrand)
                        {
                            table.AddCell(CreateCell(schedule.Brand.Name, "", 1, "center", "invoiceRecords"));
                        }
                        var brandAmount =
                            _db.BrandAmounts.Where(x => x.BrandId == schedule.BrandId && x.DoctorId == DoctorId).FirstOrDefault();
                        if (brandAmount != null)
                        {
                            amount = amount + Convert.ToInt32(brandAmount.Amount);
                            table.AddCell(CreateCell(brandAmount.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                        }
                        else
                        {
                            table.AddCell(CreateCell("0", "", 1, "right", "invoiceRecords"));
                        }
                    }
                }
            }

            // table.AddCell(CreateCell("Total(PKR)", "", col - 1, "right", "invoiceRecords"));
            // add consultancy fee
            if (IsConsultationFee)
            {
                amount = amount + (int)dbChild.Clinic.ConsultationFee;
            }

            _db.SaveChanges();
            document.Add(table);

            document.Add(new Paragraph(""));
            document.Add(new Chunk("\n"));

            // Table 3 for description above amounts table
            PdfPTable bottomTable = new PdfPTable(2);
            float[] bottomTableWidths = new float[] { 200f, 200f };
            bottomTable.HorizontalAlignment = 0;
            bottomTable.TotalWidth = 400f;
            bottomTable.LockedWidth = true;
            bottomTable.SetWidths(bottomTableWidths);

            bottomTable.AddCell(CreateCell("Thank you for your visit", "bold", 1, "left", "description"));
            bottomTable.AddCell(CreateCell("Total Amount: " + amount.ToString() + "/-", "bold", 1, "right", "description"));

            var imgcellLeft = CreateCell("", "", 1, "left", "description");
            imgcellLeft.PaddingTop = 5;
            bottomTable.AddCell(imgcellLeft);

            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            stream = output;

            //}
            var FileName = childName.Replace(" ", "") + "_Invoice" + "_" +
                           DateTime.UtcNow.AddHours(5).Date.ToString("yy") + ".pdf";
            return File(stream, "application/pdf", FileName);
        }

        private string GenerateSequentialInvoiceNumber(long doseId, long childId)
        {
            // Get the last two digits of the current year
            var currentYear = DateTime.UtcNow.Year;
            var yearPrefix = currentYear.ToString().Substring(2); // "25" for 2025

            // Check if an invoice already exists for the given doseId and childId
            var existingInvoice = _db.Invoices.FirstOrDefault(i => i.DoseId == doseId && i.ChildId == childId);
            if (existingInvoice != null)
            {
                return existingInvoice.InvoiceId;
            }

            // Get all valid invoice numbers for the current year
            var validInvoiceNumbers = _db.Invoices
                .AsEnumerable()
                .Select(i => i.InvoiceId)
                .Where(id => !string.IsNullOrEmpty(id) && id.StartsWith(yearPrefix) && long.TryParse(id.Substring(2), out _))
                .Select(id => long.Parse(id.Substring(2))) // Extract the numeric part after the year prefix
                .ToList();

            // Determine the next invoice number
            var nextInvoiceNumber = validInvoiceNumbers.Any()
                ? validInvoiceNumbers.Max() + 1
                : 1; // Start from 1 if no invoices exist for the current year

            // Format the invoice number as "YY000001"
            string invoiceNumber = $"{yearPrefix}{nextInvoiceNumber:D6}";

            return invoiceNumber;
        }

        [HttpGet("{Id}/{ScheduleDate}/{InvoiceDate}/{ConsultationFee}/Verify-Invoice-PDF")]
        public IActionResult DownloadInvoicePDFUpdated(int Id, DateTime ScheduleDate, DateTime InvoiceDate, int ConsultationFee)
        {
            var output = CreateInvoiceCell(Id, ScheduleDate, InvoiceDate, ConsultationFee);
            if (output == null)
            {
                return NotFound(new { message = "Child not found." });
            }

            // Convert Id to long before using Find()
            long childId = (long)Id;
            var dbChild = _db.Childs.Find(childId);

            if (dbChild == null)
            {
                return NotFound(new { message = "Child not found." });
            }

            var fileName = $"{dbChild.Name.Replace(" ", "")}_Invoice_{DateTime.UtcNow.AddHours(5).Date:MMMM-dd-yyyy}.pdf";
            return File(output.ToArray(), "application/pdf", fileName);
        }

        [HttpGet("{Id}/{ScheduleDate}/{InvoiceDate}/{ConsultationFee}/Verification-Invoice-PDF")]
        public IActionResult VerificationInvoicePDFUpdated(int Id, DateTime ScheduleDate, DateTime InvoiceDate, int ConsultationFee)
        {
            try
            {
                // First, check if child exists using long id
                long childId = (long)Id;
                var dbChild = _db.Childs.Find(childId);
                if (dbChild == null)
                {
                    return NotFound(new { message = "Child not found." });
                }

                // Generate invoice PDF
                var output = CreateInvoiceCell(Id, ScheduleDate, InvoiceDate, ConsultationFee);
                if (output == null)
                {
                    return StatusCode(500, new { message = "Error generating invoice PDF." });
                }

                // Create sanitized filename
                var fileName = $"{dbChild.Name?.Replace(" ", "")}_Invoice_{DateTime.UtcNow.AddHours(5).Date:MMMM-dd-yyyy}.pdf";

                // Add required headers for inline PDF viewing
                Response.Headers.Add("X-Frame-Options", "ALLOWALL");
                Response.Headers.Add("Content-Disposition", $"inline; filename={fileName}");

                // Return PDF file
                return File(output.ToArray(), "application/pdf");
            }
            catch (InvalidCastException)
            {
                return BadRequest(new { message = "Invalid ID format." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Internal server error: {ex.Message}" });
            }
        }

        private MemoryStream CreateInvoiceCell(int Id, DateTime ScheduleDate, DateTime InvoiceDate, int ConsultationFee)
        {
            int amount = 0;
            int count = 0;
            int consultaionFee = ConsultationFee;
            string childName = "";
            var document = new Document(PageSize.A4, 60, 60, 30, 30);
            var output = new MemoryStream();
            var writer = PdfWriter.GetInstance(document, output);
            writer.CloseStream = false;

            document.Open();

            // Access db data
            var dbChild = _db.Childs.Include(x => x.Clinic)
                                    .ThenInclude(x => x.Doctor)
                                    .ThenInclude(y => y.User)
                                    .Where(x => x.Id == Id)
                                    .FirstOrDefault();
            if (dbChild == null)
            {
                return null;
            }

            var dbDoctor = dbChild.Clinic.Doctor;
            var DoctorId = dbDoctor.Id;

            dbDoctor.InvoiceNumber = (dbDoctor.InvoiceNumber > 0) ? dbDoctor.InvoiceNumber + 1 : 1;

            var dbSchedules = _db.Schedules.Include(x => x.Dose)
                                  .ThenInclude(x => x.Vaccine)
                                  .Include(x => x.Brand)
                                  .Where(x => x.ChildId == Id && x.Date.Date == ScheduleDate.Date && x.IsSkip != true &&
                                              x.IsDone == true && x.IsDisease != true)
                                  .ToList();
            var latestSchedule = dbSchedules.OrderByDescending(s => s.GivenDate).FirstOrDefault();
            DateTime givendate = latestSchedule?.GivenDate ?? DateTime.Now;

            childName = dbChild.Name;
            var doseId = latestSchedule?.Dose?.Id ?? 0;
            if (doseId == 0)
            {
                throw new Exception("Dose ID not found for generating invoice number.");
            }
            string invoiceNumber = GenerateSequentialInvoiceNumber(doseId, Id);

            if (string.IsNullOrEmpty(invoiceNumber))
            {
                throw new Exception("Invoice number already exists for the given Dose ID.");
            }

            // Table 1 for description above amounts table
            PdfPTable upperTable = new PdfPTable(2);
            float[] upperTableWidths = new float[] { 250f, 250f };
            upperTable.HorizontalAlignment = 0;
            upperTable.TotalWidth = 470f;
            upperTable.LockedWidth = true;
            upperTable.SetWidths(upperTableWidths);

            upperTable.AddCell(CreateCell(dbDoctor.DisplayName, "bold", 1, "left", "description"));

            // image code start
            var imgPath = Path.Combine(_host.ContentRootPath, dbChild.Clinic.MonogramImage);

            if (System.IO.File.Exists(imgPath))
            {
                Image img = Image.GetInstance(imgPath);
                img.ScaleAbsolute(160f, 50f);
                PdfPCell imageCell = new PdfPCell(img, false)
                {
                    Colspan = 1,
                    Rowspan = 2,
                    Border = 0,
                    FixedHeight = 50f,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                upperTable.AddCell(imageCell);
            }
            else
            {
                PdfPCell emptyCell = new PdfPCell
                {
                    Colspan = 1,
                    Rowspan = 2,
                    Border = 0,
                    FixedHeight = 50f,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                upperTable.AddCell(emptyCell);
            }

            upperTable.AddCell(CreateCell(dbDoctor.AdditionalInfo, "unbold", 1, "left", "description"));

            upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "bold", 1, "left", "description"));
            upperTable.AddCell(CreateCell("info@vaccinationcentre.com", "", 1, "right", "description"));
            upperTable.AddCell(CreateCell(dbChild.Clinic.Address, "unbold", 1, "left", "description"));
            upperTable.AddCell(CreateCell(givendate.ToString("dd-MM-yyyy"), "", 1, "right", "description"));
            upperTable.AddCell(CreateCell("Phone: " + dbChild.Clinic.PhoneNumber, "unbold", 1, "left", "description"));
            upperTable.AddCell(CreateCell("#StayHome #GetVaccinated", "", 1, "right", "description"));
            upperTable.AddCell(CreateCell("", "", 2, "left", "description"));

            upperTable.AddCell(CreateCell("", "", 2, "left", "description"));

            upperTable.AddCell(CreateCell("Invoice # " + invoiceNumber, "bold", 2, "right", "description"));

            document.Add(upperTable);

            // 2nd Table
            float[] widths = new float[] { 170f, 300f };
            PdfPTable childtable = new PdfPTable(2);
            childtable.HorizontalAlignment = 0;
            childtable.TotalWidth = 470f;
            childtable.LockedWidth = true;
            childtable.SetWidths(widths);
            childtable.SpacingBefore = 10;
            childtable.SpacingAfter = 10;

            childtable.AddCell(CreateCell("Name of Kid/Patient:", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.Name, " ", 1, "left", "invoiceRecords"));

            childtable.AddCell(CreateCell("Father/Mother/Husband Name:", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.FatherName, "", 1, "left", "invoiceRecords"));

            childtable.AddCell(CreateCell("Date of Birth:", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.DOB.ToString("dd/MM/yyyy"), "", 1, "left", "invoiceRecords"));

            childtable.AddCell(CreateCell("City:", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.City, " ", 1, "left", "invoiceRecords"));
            if (!String.IsNullOrEmpty(dbChild.CNIC))
            {
                childtable.AddCell(CreateCell("CNIC/Passport: ", "backgroudLightGray", 1, "left", "invoiceRecords"));
                childtable.AddCell(CreateCell(dbChild.CNIC, " ", 1, "left", "invoiceRecords"));
            }
            else
            {
                childtable.AddCell(CreateCell("", "", 1, "right", "invoiceRecords"));
                childtable.AddCell(CreateCell("", "", 1, "right", "invoiceRecords"));
            }

            _db.SaveChanges();
            document.Add(childtable);

            Paragraph vaccinetitle = new Paragraph("VACCINATION DETAILS");
            vaccinetitle.Font = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
            vaccinetitle.Alignment = Element.ALIGN_CENTER;
            document.Add(vaccinetitle);

            // table 3 for vaccination details
            float[] vaccinationwidths = new float[] { 10f, 70, 50, 30f, 30f, 40f };
            PdfPTable vaccinetable = new PdfPTable(6);
            vaccinetable.HorizontalAlignment = 0;
            vaccinetable.TotalWidth = 470f;
            vaccinetable.LockedWidth = true;
            vaccinetable.SetWidths(vaccinationwidths);
            vaccinetable.SpacingBefore = 10;
            vaccinetable.SpacingAfter = 50;

            vaccinetable.AddCell(CreateCell("#", " ", 1, "center", "invoiceRecords"));
            vaccinetable.AddCell(CreateCell("Vaccine", "backgroudLightGray", 1, "left", "invoiceRecords"));
            vaccinetable.AddCell(CreateCell("Brand", "backgroudLightGray", 1, "left", "invoiceRecords"));
            vaccinetable.AddCell(CreateCell("Quantity", "backgroudLightGray", 1, "center", "invoiceRecords"));
            vaccinetable.AddCell(CreateCell("Price", "backgroudLightGray", 1, "left", "invoiceRecords"));
            vaccinetable.AddCell(CreateCell("Amount", "backgroudLightGray", 1, "center", "invoiceRecords"));

            // loop start
            if (dbSchedules.Count != 0)
            {
                foreach (var schedule in dbSchedules)
                {
                    if (schedule.IsDone == true)
                    {
                        count++;
                        vaccinetable.AddCell(CreateCell(count.ToString(), "", 1, "center", "invoiceRecords"));
                        vaccinetable.AddCell(CreateCell(schedule.Dose.Vaccine.Name, "", 1, "left", "invoiceRecords"));
                        // Assuming 'schedule' is defined and contains the necessary properties
                        var childId = schedule.ChildId;
                        var doctorId = schedule.Child.Clinic.DoctorId;
                        var clinicId = schedule.Child.ClinicId;

                        // Retrieve the brand amount
                        var brandAmount = _db.BrandAmounts
                            .FirstOrDefault(x => x.BrandId == schedule.BrandId && x.DoctorId == doctorId && x.Clinic.IsOnline == true);

                        // Check if the invoice already exists
                        var existingInvoice = _db.Invoices
                            .FirstOrDefault(i => i.DoseId == schedule.Dose.Id
                                                && i.ChildId == schedule.ChildId
                                                && i.DoctorId == doctorId
                                                && i.ClinicId == schedule.Child.ClinicId);

                        // If the invoice doesn't exist, create a new one
                        if (existingInvoice == null)
                        {
                            existingInvoice = new Invoice
                            {
                                InvoiceId = invoiceNumber,
                                DoseId = schedule.Dose.Id,
                                ChildId = schedule.ChildId,
                                DoctorId = doctorId,
                                ClinicId = schedule.Child.ClinicId
                            };
                            _db.Invoices.Add(existingInvoice);
                        }
                          var existingFee = _db.Fee
                            .FirstOrDefault(f => f.InvoiceId == invoiceNumber); 

                        if (existingFee == null)
                        {
                              if(consultaionFee != 0)
                            {
                                var fee = new Fee
                                {
                                     InvoiceId = invoiceNumber,
                                     Amount = consultaionFee,
                                };
                                _db.Fee.Add(fee);
                            }
                        }
                        if (existingFee != null)
                        {
                            existingFee.Amount = consultaionFee;
                            _db.Entry(existingFee).State = EntityState.Modified;
                            _db.SaveChanges();
                        }

                        bool isAmountEmptyOrZero = schedule.Amount == null || schedule.Amount == 0 || schedule.Amount.ToString().Trim() == string.Empty;

                        if (brandAmount != null && isAmountEmptyOrZero)
                        {
                            existingInvoice.Amount = brandAmount.Amount != 0 ? brandAmount.Amount : 0;
                        }
                        else if (schedule.Amount != null && schedule.Amount != 0)
                        {
                            existingInvoice.Amount = (decimal)(schedule?.Amount ?? 0);
                        }
                        _db.SaveChanges();
                        _db.Entry(existingInvoice).State = EntityState.Modified;

                        if (schedule.BrandId > 0)
                        {
                            vaccinetable.AddCell(CreateCell(schedule.Brand.Name, "", 1, "left", "invoiceRecords"));
                        }
                        else
                        {
                            vaccinetable.AddCell(CreateCell(" ", "", 1, "center", "invoiceRecords"));
                        }

                        vaccinetable.AddCell(CreateCell("1", " ", 1, "right", "invoiceRecords"));

                        var brandAmount1 =
                            _db.BrandAmounts.Where(x => x.BrandId == schedule.BrandId && x.DoctorId == DoctorId && x.Clinic.IsOnline == true).FirstOrDefault();
                        if (brandAmount != null && schedule.Amount == null)
                        {
                            amount = amount + Convert.ToInt32(brandAmount.Amount);
                            vaccinetable.AddCell(CreateCell(brandAmount.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                            vaccinetable.AddCell(CreateCell(brandAmount.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                        }
                        else if (brandAmount != null && schedule.Amount != null)
                        {
                            amount = amount + Convert.ToInt32(schedule.Amount);
                            vaccinetable.AddCell(CreateCell(schedule.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                            vaccinetable.AddCell(CreateCell(schedule.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                        }
                        else if (brandAmount == null && schedule.Amount != null)
                        {
                            amount = amount + Convert.ToInt32(schedule.Amount);
                            vaccinetable.AddCell(CreateCell(schedule.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                            vaccinetable.AddCell(CreateCell(schedule.Amount.ToString(), "", 1, "right", "invoiceRecords"));
                        }
                        else
                        {
                            vaccinetable.AddCell(CreateCell("0", "", 1, "right", "invoiceRecords"));
                            vaccinetable.AddCell(CreateCell("0", "", 1, "right", "invoiceRecords"));
                        }
                    }
                }

                if (consultaionFee != 0)
                {
                    count++;
                    vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                    vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                    vaccinetable.AddCell(CreateCell("Consultation / Visit Charges", "left", 1, "left", "invoiceRecords"));
                    vaccinetable.AddCell(CreateCell("1", " ", 1, "right", "invoiceRecords"));
                    vaccinetable.AddCell(CreateCell("", " ", 1, "right", "invoiceRecords"));
                    vaccinetable.AddCell(CreateCell(consultaionFee.ToString("F2"), " ", 1, "right", "invoiceRecords"));
                }

                vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell("Total", "backgroudLightGray", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell((count).ToString(), "backgroudLightGray", 1, "right", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(" ", "backgroudLightGray", 1, "right", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(("PKR " + string.Format("{0:F2}", (amount + consultaionFee))), "backgroudLightGray", 1, "right", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell("Amount in words", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(ConvertWholeNumber((amount + consultaionFee).ToString()) + " Only", " ", 4,
                                                "left", "inwordamount"));
            }

            // loop end
            document.Add(vaccinetable);

            // First Table: Quick Links and Other Information
            PdfPTable bottomTable = new PdfPTable(2);
            float[] bottomTableWidths = new float[] { 235f, 235f };
            bottomTable.HorizontalAlignment = Element.ALIGN_LEFT;
            bottomTable.TotalWidth = 470f;
            bottomTable.LockedWidth = true;
            bottomTable.SetWidths(bottomTableWidths);

            // Ensure `currentDate` and `footerFont` are properly initialized
            var footerFont = new Font(Font.HELVETICA, 8, Font.NORMAL);
            var currentDate = DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-dd");

            // Adding cells to the bottom table
            PdfPCell CreateCellWithMargin(string text, string style, int colspan, string alignment, string description, float topMargin = 0)
            {
                var cell = CreateCell(text, style, colspan, alignment, description);
                cell.PaddingTop = topMargin;
                return cell;
            }

            bottomTable.AddCell(CreateCell(" ", "bold", 2, "left", "description"));
            bottomTable.AddCell(CreateCell("Quick links: ", "", 2, "left", "description"));

            bottomTable.AddCell(CreateCell("vaccinationcentre.com", "", 1, "left", "description"));
            bottomTable.AddCell(CreateCellWithMargin("Web: vaccinationcentre.com", "", 1, "right", "description", topMargin: -0)); // Adjust topMargin as needed
            bottomTable.AddCell(CreateCell("vaccinationcentre.com/vaccines", "", 1, "left", "description"));
            bottomTable.AddCell(CreateCellWithMargin("Phone/WhatsApp: +923335196658", "", 1, "right", "description", topMargin: -0)); // Adjust topMargin as needed
            bottomTable.AddCell(CreateCell("vaccinationcentre.com/schedule", "", 1, "left", "description"));
            bottomTable.AddCell(CreateCellWithMargin("Email: info@vaccinationcentre.com", "", 1, "right", "description", topMargin: -0)); // Adjust topMargin as needed
            bottomTable.WriteSelectedRows(0, -1, 65, 85, writer.DirectContent);

            PdfPTable footerTable = new PdfPTable(1);
            footerTable.TotalWidth = 470f;
            footerTable.LockedWidth = true;

            Font footerFont1 = FontFactory.GetFont(FontFactory.HELVETICA, 10);
            Phrase footerPhrase = new Phrase();
            var statementText = "This is an electronically generated invoice and doesn't require signature/stamp. Printed On : " + currentDate;
            Chunk statementChunk = new Chunk(statementText, footerFont1);
            footerPhrase.Add(statementChunk);
            PdfPCell footerCell = new PdfPCell(footerPhrase)
            {
                HorizontalAlignment = Element.ALIGN_LEFT,
                Border = Rectangle.NO_BORDER,
                PaddingTop = -70f,
                PaddingBottom = 300f,
            };

            // Add the footer cell to the table
            bottomTable.AddCell(footerCell);

            footerTable.AddCell(footerCell);
            footerTable.WriteSelectedRows(0, -1, 65, 60, writer.DirectContent);

            var baseUrl = "https://myapi.vaccinationcentre.com/api";
            var childData = _db.Childs.Include(x => x.Clinic)
                            .ThenInclude(x => x.Doctor)
                            .ThenInclude(y => y.User)
                            .FirstOrDefault(x => x.Id == Id);

            var childDTO = new ChildDTO
            {
                Id = childData.Id,
                Name = childData.Name ?? "Unknown",
                FatherName = childData.FatherName ?? "Unknown",
            };

            var qrCodeUrl = $"{baseUrl}/child/{Id}/{ScheduleDate:yyyy-MM-dd}/{InvoiceDate:yyyy-MM-dd}/{ConsultationFee}/Download-Invoice-PDF";
            try
            {

                using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeUrl, QRCodeGenerator.ECCLevel.Q))
                {
                    var qrCode = new BitmapByteQRCode(qrCodeData);
                    byte[] qrCodeImage = qrCode.GetGraphic(20);
                    using (MemoryStream ms = new MemoryStream(qrCodeImage))
                    {
                        var pdfQrCode = iTextSharpImage.GetInstance(ms.ToArray());
                        pdfQrCode.ScaleAbsolute(80f, 80f);

                        float pageWidth = document.PageSize.Width;
                        float qrCodeXPosition = (pageWidth - pdfQrCode.ScaledWidth) / 2;

                        float qrCodeYPosition = 23f;

                        pdfQrCode.SetAbsolutePosition(qrCodeXPosition, qrCodeYPosition);
                        writer.DirectContent.AddImage(pdfQrCode);
                    }
                }



            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating QR code: {ex.Message}");
            }
            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            return output;
        }

        [HttpGet("{Id}/{ScheduleDate}/{InvoiceDate}/{ConsultationFee}/Download-Invoice-PDF")]
        public IActionResult GenerateVerifyInvoicePdf(int Id, DateTime ScheduleDate, DateTime InvoiceDate, int ConsultationFee)
        {
            var fileUrl = $"https://myapi.vaccinationcentre.com/api/Child/{Id}/{ScheduleDate:yyyy-MM-dd}/{InvoiceDate:yyyy-MM-dd}/{ConsultationFee}/Verification-Invoice-PDF";

            string htmlContent = $@"
                                    <!DOCTYPE html>
                                    <html lang='en'>
                                    <head>
                                        <meta charset='UTF-8'>
                                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                                        <title>QR Code Viewer</title>
                                    </head>
                                    <body style='font-family: Arial, sans-serif; text-align: center; padding: 20px;'>
                                        <h1>Immunization Record</h1>
                                        <p><a href='{fileUrl}' target='_blank'>click here</a> to view the details.</p>
                                        <iframe src='{fileUrl}' width='600' height='700' style='border: 1px solid #ccc;'></iframe>
                                    </body>
                                    </html>";

            return new ContentResult
            {
                Content = htmlContent,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        // functions to convert amount to words
        private static String ones(String Number)
        {
            int _Number = Convert.ToInt32(Number);
            String name = "";
            switch (_Number)
            {
                case 1:
                    name = "One";
                    break;
                case 2:
                    name = "Two";
                    break;
                case 3:
                    name = "Three";
                    break;
                case 4:
                    name = "Four";
                    break;
                case 5:
                    name = "Five";
                    break;
                case 6:
                    name = "Six";
                    break;
                case 7:
                    name = "Seven";
                    break;
                case 8:
                    name = "Eight";
                    break;
                case 9:
                    name = "Nine";
                    break;
            }
            return name;
        }

        private static String tens(String Number)
        {
            int _Number = Convert.ToInt32(Number);
            String name = null;
            switch (_Number)
            {
                case 10:
                    name = "Ten";
                    break;
                case 11:
                    name = "Eleven";
                    break;
                case 12:
                    name = "Twelve";
                    break;
                case 13:
                    name = "Thirteen";
                    break;
                case 14:
                    name = "Fourteen";
                    break;
                case 15:
                    name = "Fifteen";
                    break;
                case 16:
                    name = "Sixteen";
                    break;
                case 17:
                    name = "Seventeen";
                    break;
                case 18:
                    name = "Eighteen";
                    break;
                case 19:
                    name = "Nineteen";
                    break;
                case 20:
                    name = "Twenty";
                    break;
                case 30:
                    name = "Thirty";
                    break;
                case 40:
                    name = "Fourty";
                    break;
                case 50:
                    name = "Fifty";
                    break;
                case 60:
                    name = "Sixty";
                    break;
                case 70:
                    name = "Seventy";
                    break;
                case 80:
                    name = "Eighty";
                    break;
                case 90:
                    name = "Ninety";
                    break;
                default:
                    if (_Number > 0)
                    {
                        name = tens(Number.Substring(0, 1) + "0") + " " + ones(Number.Substring(1));
                    }
                    break;
            }
            return name;
        }

        private static String ConvertWholeNumber(String Number)
        {
            string word = "";
            try
            {
                bool beginsZero = false;  // tests for 0XX
                bool isDone = false;      // test if already translated
                double dblAmt = (Convert.ToDouble(Number));

                // if ((dblAmt > 0) && number.StartsWith("0"))

                if (dblAmt > 0)
                {
                    // test for zero or digit zero in a nuemric

                    beginsZero = Number.StartsWith("0");

                    int numDigits = Number.Length;
                    int pos = 0;        // store digit grouping
                    String place = "";  // digit grouping name:hundres,thousand,etc...
                    switch (numDigits)
                    {
                        case 1:  // ones' range
                            word = ones(Number);
                            isDone = true;
                            break;
                        case 2:  // tens' range
                            word = tens(Number);
                            isDone = true;
                            break;
                        case 3:  // hundreds' range
                            pos = (numDigits % 3) + 1;
                            place = " Hundred ";
                            break;
                        case 4:  // thousands' range
                        case 5:
                        case 6:
                            pos = (numDigits % 4) + 1;
                            place = " Thousand ";
                            break;
                        case 7:  // millions' range
                        case 8:
                        case 9:
                            pos = (numDigits % 7) + 1;
                            place = " Million ";
                            break;
                        case 10:  // Billions's range
                        case 11:
                        case 12:
                            pos = (numDigits % 10) + 1;
                            place = " Billion ";
                            break;
                        // add extra case options for anything above Billion...

                        default:
                            isDone = true;
                            break;
                    }
                    if (!isDone)
                    {
                        // if transalation is not done, continue...(Recursion comes in now!!)

                        if (Number.Substring(0, pos) != "0" && Number.Substring(pos) != "0")
                        {
                            try
                            {
                                word = ConvertWholeNumber(Number.Substring(0, pos)) + place + ConvertWholeNumber(Number.Substring(pos));
                            }
                            catch
                            {
                            }
                        }
                        else
                        {
                            word = ConvertWholeNumber(Number.Substring(0, pos)) + ConvertWholeNumber(Number.Substring(pos));
                        }

                        // check for trailing zeros
                        // if (beginsZero) word = " and " + word.Trim();
                    }

                    // ignore digit grouping names

                    if (word.Trim().Equals(place.Trim())) word = "";
                }
            }
            catch
            {
            }
            return word.Trim();
        }

        [HttpPut]
        public Response<ChildDTO> Put([FromBody] ChildDTO childDTO)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            childDTO.Name = textInfo.ToTitleCase(childDTO.Name);
            childDTO.FatherName = textInfo.ToTitleCase(childDTO.FatherName);

            {
                var dbChild = _db.Childs.Include(x => x.User).FirstOrDefault(c => c.Id == childDTO.Id);
                if (dbChild == null) return new Response<ChildDTO>(false, "Child not found", null);
                dbChild.Name = childDTO.Name;
                dbChild.Email = childDTO.Email;
                dbChild.Guardian = childDTO.Guardian;
                dbChild.FatherName = childDTO.FatherName;
                dbChild.Gender = childDTO.Gender;
                dbChild.City = childDTO.City;
                dbChild.IsEPIDone = childDTO.IsEPIDone;
                dbChild.IsVerified = childDTO.IsVerified;
                dbChild.IsInactive = childDTO.IsInactive;
                dbChild.IsPAApprove = childDTO.IsPAApprove;
                dbChild.Nationality = childDTO.Nationality;
                dbChild.Agent = childDTO.Agent;
                dbChild.CNIC = childDTO.CNIC;
                var dbUser = dbChild.User;
                dbUser.CountryCode = childDTO.CountryCode;
                dbUser.MobileNumber = childDTO.MobileNumber;
                _db.SaveChanges();
                return new Response<ChildDTO>(true, null, childDTO);
            }
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id, [FromQuery] string userType = null, [FromQuery] long? paId = null)
        {
            if (!string.IsNullOrWhiteSpace(userType) && userType.Equals("PA", StringComparison.OrdinalIgnoreCase))
            {
                var child = _db.Childs.Find((long)Id);
                if (child == null)
                    return new Response<string>(false, "Child not found", null);

                if (child.AddedByPaId == null || child.AddedByPaId != paId)
                    return new Response<string>(false, "You can only delete patients you added yourself.", null);

                if (child.CreatedAt == null || child.CreatedAt.Value.Date != DateTime.UtcNow.Date)
                    return new Response<string>(false, "You can only delete a patient on the same day they were added.", null);
            }

            var dbChild = _db.Childs.Include(x => x.User)
                              .ThenInclude(x => x.Childs)
                              .Include(x => x.Schedules)
                              .Include(x => x.FollowUps)
                              .Where(c => c.Id == Id)
                              .FirstOrDefault();
            if (dbChild == null)
            {
                return new Response<string>(false, "Child not found", null);
            }

            _db.Schedules.RemoveRange(dbChild.Schedules);
            _db.FollowUps.RemoveRange(dbChild.FollowUps);
            if (dbChild.User.Childs.Count == 1) _db.Users.Remove(dbChild.User);
            _db.Childs.Remove(dbChild);
            _db.SaveChanges();
            return new Response<string>(true, "Child is deleted successfully", null);
        }

        // Date Function
        protected DateTime calculateDate(DateTime date, int GapInDays)
        {
            if (GapInDays == 28 || GapInDays == 30 || GapInDays == 31)
                return date.AddMonths(1);
            else if (GapInDays == 56)
                return date.AddMonths(2);
            else if (GapInDays == 84)
                return date.AddMonths(3);
            else if (GapInDays == 112)
                return date.AddMonths(4);
            else if (GapInDays == 140 || GapInDays == 150)
                return date.AddMonths(5);
            else if (GapInDays == 168)
                return date.AddMonths(6);
            else if (GapInDays == 3315)
                return date.AddYears(9).AddMonths(1);
            else if (GapInDays == 3833)
                return date.AddYears(10).AddMonths(6);
            else if (GapInDays == 365 || GapInDays == 730 || GapInDays == 1095 || GapInDays == 1460 || GapInDays == 1825 ||
                    GapInDays == 2190 || GapInDays == 2555 || GapInDays == 2920 || GapInDays == 3285 || GapInDays == 3650 ||
                    GapInDays == 4015 || GapInDays == 4380 || GapInDays == 4745 || GapInDays == 5110 || GapInDays == 5475 ||
                    GapInDays == 5840 || GapInDays == 6205 || GapInDays == 6570 || GapInDays == 6935 || GapInDays == 7300 ||
                    GapInDays == 7665 || GapInDays == 8030 || GapInDays == 8395 || GapInDays == 8760 || GapInDays == 9125)
                return date.AddYears((int)(GapInDays / 365));
            else if (GapInDays > 168 && GapInDays <= 334)
                return date.AddMonths((int)(GapInDays / 28));
            else if (GapInDays >= 395 && GapInDays <= 608)
                return date.AddMonths((int)(GapInDays / 29));
            else if (GapInDays >= 639 && GapInDays <= 1795)
                return date.AddMonths((int)(GapInDays / 30));
            else
                return date.AddDays(GapInDays);
        }


        public class PDFFooter : PdfPageEventHelper
        {
             private readonly Child child;
             BaseColor lightGreen = new BaseColor(159, 226, 191);
             private readonly Font footerFont = FontFactory.GetFont("Helvetica", 8f, BaseColor.Black);
             private readonly Font footerFontBold = FontFactory.GetFont("Helvetica-Bold", 8f, BaseColor.Black);

             public PDFFooter(Child postedChild)
             {
                 child = postedChild;
             }
         
             public override void OnEndPage(PdfWriter writer, Document document)
             {
             base.OnEndPage(writer, document);
             PdfContentByte cb = writer.DirectContent;
                if (child != null && child.Clinic != null)
             {
             var clinic = child.Clinic;
             var clinicName = clinic.Name ?? "";
             var regNo = clinic.RegNo ?? "";
             var address = clinic.Address ?? "";
             var phoneNumber = clinic.PhoneNumber ?? "";
             var email = "https://vaccinationcentre.com";
             float footerY = 85; 
             float footerHeight = 110;

             string footer =
                 "Vaccines may cause fever, localized redness, and pain. This schedule is valid for all airports, airlines, embassies, and schools of world. We " +
                 "always use the best available vaccine brand/manufacturer. With time and ongoing research, vaccine brands may differ for future doses." +
                 " Disclaimer: This schedule provides recommended dates for immunization based on the individual date of birth, past immunization, and disease history." +
                 "Your consultant may update the due dates or add/remove vaccines." + clinicName + ", its management, or staff hold no responsibility for any loss or" +
                 "damage due to any vaccines given or change/s in schedule. *OHF = vaccine given at other health facility (not by " + clinicName + ")." +
                 "\nPrinted on: " + DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-dd") + ".";
           
             footer = footer.Replace(Environment.NewLine, string.Empty).Replace("  ", string.Empty);

             Font georgia = FontFactory.GetFont("Georgia", 8f);
             Chunk beginning = new Chunk(footer, georgia);

             PdfPTable tabFot = new PdfPTable(1);
             tabFot.SetTotalWidth(new float[] { 510f });
             tabFot.DefaultCell.HorizontalAlignment = Element.ALIGN_JUSTIFIED;

             PdfPCell cell = new PdfPCell(new Phrase(beginning));
             cell.Border = 0;
             cell.PaddingLeft = -21f;
             cell.PaddingTop = 28f;
             cell.PaddingRight = 21f;
             // cell.PaddingBottom = -85f;
             tabFot.AddCell(cell);

             // Write the main footer
             tabFot.WriteSelectedRows(0, -85, 65, 135, cb);

             // Clinic details (if available)
     

             Phrase phrase = new Phrase();
                 // phrase.Add(new Chunk($"", footerFontBold));
                 // if (!string.IsNullOrEmpty(regNo))
                 //     phrase.Add(new Chunk($"({regNo})", footerFont));
                 // phrase.Add(new Chunk($" Printed on: " + DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-dd"), footerFont));
                 // ColumnText.ShowTextAligned(cb, Element.ALIGN_LEFT, phrase, document.LeftMargin + 5, footerHeight, 0);

                        // 3-column table for address, phone, email
             PdfPTable contactTable = new PdfPTable(2);
             contactTable.TotalWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
             contactTable.SetWidths(new float[] { 3,  2 });

            var boldFont = FontFactory.GetFont("Helvetica-Bold", 10f, BaseColor.Black);
            var normalFont = FontFactory.GetFont("Helvetica", 10f, BaseColor.Black);
            Phrase AddressPhrase = new Phrase();
            AddressPhrase.Add(new Chunk($"{clinicName},", boldFont));
            AddressPhrase.Add(new Chunk($"{address}", normalFont));
            PdfPCell addressCell = new PdfPCell(AddressPhrase)
            {
                Border = Rectangle.TOP_BORDER | Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER,
                HorizontalAlignment = Element.ALIGN_LEFT,
                PaddingTop = 8,
                BackgroundColor = new BaseColor(159, 226, 191)
            };
           
            Phrase emailPhrase = new Phrase();
            // emailPhrase.Add(new Chunk("Site: ", boldFont));
            emailPhrase.Add(new Chunk($"{email}", normalFont));
            // emailPhrase.Add(new Chunk("\nPhone: ", boldFont));
            emailPhrase.Add(new Chunk($"\n{phoneNumber}", normalFont));

            PdfPCell emailCell = new PdfPCell(emailPhrase)
            {
                Border = Rectangle.TOP_BORDER | Rectangle.RIGHT_BORDER | Rectangle.BOTTOM_BORDER,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                Padding = 5,
                BackgroundColor = new BaseColor(159, 226, 191)
            };

            contactTable.AddCell(addressCell);
            // contactTable.AddCell(phoneCell);
            contactTable.AddCell(emailCell);
            contactTable.WriteSelectedRows(0, -1, document.LeftMargin, footerY - 30, cb);
              }
              else
              {
                 float footerY = 85; 
                 ColumnText.ShowTextAligned(
                     cb,
                     Element.ALIGN_LEFT,
                     new Phrase("Clinic details not found", footerFont),
                     document.LeftMargin + 5,
                     footerY,
                     0
                 );
              }
           }
        }

        [HttpGet("PIDVerify/{id}")]
        public IActionResult GeneratePIDPdf(int id)
        {
            long childId = Convert.ToInt64(id);
            var dbChild = _db.Childs.Find(childId);
            if (dbChild == null)
            {
                return NotFound(new { message = "Child not found." });
            }

            MemoryStream output = CreatePID(childId);

            if (output == null)
            {
                return NotFound(new { message = "Child not found." });
            }


            var currentDate = DateTime.Now.ToString("dd-MMM-yyyy");
            var fileName = $"{dbChild.Name}_PID_{currentDate}.pdf";
            return File(output.ToArray(), "application/pdf", fileName);
        }

        [HttpGet("PID/{id}")]
        public IActionResult GenerateVerifyPID(int id)
        {
            var fileUrl = $"https://myapi.vaccinationcentre.com/api/Child/PIDPDF/{id}";

            string htmlContent = $@"
                                    <!DOCTYPE html>
                                    <html lang='en'>
                                    <head>
                                        <meta charset='UTF-8'>
                                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                                        <title>QR Code Viewer</title>
                                    </head>
                                    <body style='font-family: Arial, sans-serif; text-align: center; padding: 20px;'>
                                        <h1>Immunization Record</h1>
                                        <p><a href='{fileUrl}' target='_blank'>click here</a> to view the details.</p>
                                        <iframe src='{fileUrl}' width='600' height='400' style='border: 1px solid #ccc;'></iframe>
                                    </body>
                                    </html>";

            return new ContentResult
            {
                Content = htmlContent,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        [HttpGet("PIDPDF/{childId}")]
        public IActionResult ViewPdf(int childId)
        {
            long child = Convert.ToInt64(childId);
            var dbChild = _db.Childs.Find(child);

            if (dbChild == null)
            {
                return NotFound(new { message = "Child not found." });
            }

            MemoryStream output = CreatePID(child);

            if (output == null)
            {
                return NotFound(new { message = "Child not found." });
            }

            Response.Headers.Add("X-Frame-Options", "ALLOWALL");
            Response.Headers.Add("Content-Disposition", $"inline; filename=PID_{childId}.pdf");

            return File(output.ToArray(), "application/pdf");
        }

        private MemoryStream CreatePID(long childId)
        {
             var dbChild = _db.Childs
                 .Include(c => c.Clinic)
                 .FirstOrDefault(c => c.Id == childId);

             if (dbChild == null)
                 return null;

             if (dbChild.Clinic == null)
                 throw new Exception("Clinic information not found for this child.");

            float width = 120f * 2.83465f;
            float height = 80f * 2.83465f;
            float padding = 10f;
            var document = new Document(new Rectangle(width, height), padding, padding, padding, padding);
            var output = new MemoryStream();
            var writer = PdfWriter.GetInstance(document, output);
            writer.CloseStream = false;
            document.Open();

            PdfPTable headerTable = new PdfPTable(2);
            headerTable.WidthPercentage = 100;
            headerTable.SetWidths(new float[] { 3, 1 });

            PdfPCell textCell = new PdfPCell();
            textCell.Border = PdfPCell.NO_BORDER;
            textCell.PaddingTop = 20f;
            textCell.PaddingLeft = 20f;
            textCell.AddElement(new Paragraph("Vaccine.pk", FontFactory.GetFont(FontFactory.HELVETICA, 10)));
            textCell.AddElement(new Paragraph("IMMUNIZATION RECORD", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12))
            {
                SpacingAfter = 0f
            });
            headerTable.AddCell(textCell);

            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Images", "Vaccine.pdflogo.png");
            if (System.IO.File.Exists(logoPath))
            {
                var logo = iTextSharp.text.Image.GetInstance(logoPath);
                logo.ScaleToFit(55f, 55f);
                PdfPCell logoCell = new PdfPCell(logo)
                {
                    Border = PdfPCell.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_LEFT
                };
                logoCell.PaddingTop = 30f;
                logoCell.PaddingLeft = 0f;
                headerTable.AddCell(logoCell);
            }
            else
            {
                throw new FileNotFoundException("Logo file not found at: " + logoPath);
            }
            document.Add(headerTable);

            var detailsFonts = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
            var detailsFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
            PdfPTable detailsTable = new PdfPTable(1);
            detailsTable.WidthPercentage = 100;
            detailsTable.AddCell(new PdfPCell(new Paragraph($"{dbChild.Name}", detailsFonts))
            {
                Border = PdfPCell.NO_BORDER,
                PaddingLeft = 23f,
                PaddingTop = 0f
            });
            detailsTable.AddCell(new PdfPCell(new Paragraph($"{dbChild.FatherName}", detailsFont))
            {
                Border = PdfPCell.NO_BORDER,
                PaddingLeft = 23f
            });
            detailsTable.AddCell(new PdfPCell(new Paragraph($"DOB: {dbChild.DOB:dd-MMM-yyyy}", detailsFont))
            {
                Border = PdfPCell.NO_BORDER,
                PaddingLeft = 23f
            });
            detailsTable.AddCell(new PdfPCell(new Paragraph($"Passport/ID: {dbChild.CNIC}", detailsFont))
            {
                Border = PdfPCell.NO_BORDER,
                PaddingLeft = 23f
            });
            int currentYear = DateTime.Now.Year;
            string mrNumber = currentYear.ToString()[2..];
            detailsTable.AddCell(new PdfPCell(new Paragraph($"MR # {mrNumber}{dbChild.Id}", detailsFonts))
            {
                Border = PdfPCell.NO_BORDER,
                PaddingLeft = 45f,
                PaddingBottom = 6f,
                PaddingTop = 6f
            });

            document.Add(detailsTable);

            var baseUrl = "https://myapi.vaccinationcentre.com/api";
            var qrCodeUrl = $"{baseUrl}/Child/PID/{childId}";

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeUrl, QRCodeGenerator.ECCLevel.Q))
            {
                var qrCode = new BitmapByteQRCode(qrCodeData);
                byte[] qrCodeImage = qrCode.GetGraphic(18);

                using (MemoryStream ms = new MemoryStream(qrCodeImage))
                {
                    var pdfQrCode = iTextSharp.text.Image.GetInstance(ms.ToArray());
                    const float qrBaseSize = 60f;
                    const float qrScale = 1.10f;
                    const float qrRightPadding = 35f;
                    const float qrBottomPadding = 25f;
                    const float qrMoveUp = 30f;

                    pdfQrCode.ScaleAbsolute(qrBaseSize * qrScale, qrBaseSize * qrScale);
                    float qrCodeXPosition = document.PageSize.Width - pdfQrCode.ScaledWidth - qrRightPadding;
                    float qrCodeYPosition = qrBottomPadding + qrMoveUp;
                    pdfQrCode.SetAbsolutePosition(qrCodeXPosition, qrCodeYPosition);
                    writer.DirectContent.AddImage(pdfQrCode);
                }
            }

            Font footerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);

            Paragraph footerText1 = new Paragraph($"{dbChild.Clinic.Name} ({dbChild.Clinic.RegNo})", footerFont)
            {
                IndentationLeft = 20f
            };
            document.Add(footerText1);

            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            return output;
        }

        [HttpGet("Travel-PDF-Download-verify/{childId}")]
        public IActionResult GenerateTravelPdf(int childId)
        {
            var output = CreateTravelPdf(childId);
            if (output == null)
            {
                return null;
            }

            var childDetails = _db.Childs.Where(x => x.Id == childId).FirstOrDefault();
            var fileName = childDetails.Name.Replace(" ", "_") + "_Travel_Immunization_" +
                          DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(output.ToArray(), "application/pdf");
        }

        [HttpGet("Travel-PDF-Download-Verification/{childId}")]
        public IActionResult GenerateTravelVerificationPdf(int childId)
        {
            var output = CreateTravelPdf(childId);
            if (output == null)
            {
                return null;
            }

            var fileName = $"Immunization-Record.pdf";
            Response.Headers.Add("X-Frame-Options", "ALLOWALL");
            Response.Headers.Add("Content-Disposition", $"inline; filename={fileName}");

            return File(output.ToArray(), "application/pdf");
        }

        [HttpGet("not-approved/{clinicId}")]
        public Response<IEnumerable<ChildDTO>> GetNotApprovedChildrenByClinic(long clinicId)
        {
            try
            {

                var dbChildren = _db
                    .Childs.Include(c => c.User) .Include(c => c.Schedules) 
                    .Where(c =>c.ClinicId == clinicId&& (c.IsPAApprove == false
                            || c.Schedules.Any(s => s.IsPAApprove == false && s.IsDone == true)))
                    .ToList();

                var childDTOs = _mapper.Map<List<ChildDTO>>(dbChildren);
                foreach (var childDTO in childDTOs)
                {
                    var user = dbChildren.FirstOrDefault(c => c.Id == childDTO.Id)?.User;
                    if (user != null)
                    {
                        childDTO.CountryCode = user.CountryCode;
                        childDTO.MobileNumber = user.MobileNumber;
                    }
                }
                return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching not-approved children for clinic ID {clinicId}: {ex.Message}");
                return new Response<IEnumerable<ChildDTO>>(false,"An error occurred while fetching not-approved children.",null);
            }
        }

        [HttpPut("approve/{id}")]
        public IActionResult ApproveChild(long id)
        {
            try
            {
            var child = _db.Childs.FirstOrDefault(c => c.Id == id);
            if (child == null)
            {
                return NotFound(new { success = false, message = "Child not found for the provided ID." });
            }
            child.IsPAApprove = true;
            _db.Entry(child).State = EntityState.Modified;
            _db.SaveChanges();
            return Ok(new { success = true, message = "Child approved successfully." });
            }
            catch (Exception ex)
            {
            Console.WriteLine($"Error approving child: {ex.Message}");
            return StatusCode(500, new { success = false, message = "An error occurred while approving the child." });
            }
        }

        private string GetYearOrMonthFromDays(int days)
        {
            // Use same age map as GetYearOrMonthFromDaysSchedule for consistency
            var ageMap = new Dictionary<int, string>
            {
                { 0, "At Birth" },
                { 365, "1 Year" },
                { 730, "2 Years" },
                { 1095, "3 Years" },
                { 1460, "4 Years" },
                { 1825, "5 Years" },
                { 2190, "6 Years" },
                { 2555, "7 Years" },
                { 2920, "8 Years" },
                { 3285, "9 Years" },
                { 3650, "10 Years" },
                { 4015, "11 Years" },
                { 4380, "12 Years" },
                { 4745, "13 Years" },
                { 5110, "14 Years" },
                { 5475, "15 Years" },
                { 5840, "16 Years" },
                { 6205, "17 Years" },
                { 6570, "18 Years" },
                { 6935, "19 Years" },
                { 7300, "20 Years" },
                { 7665, "21 Years" },
                { 8030, "22 Years" },
                { 8395, "23 Years" },
                { 8760, "24 Years" },
                { 9125, "25 Years" },
                { 30000, "Life Time" }
            };
            
            // Check if exact match exists
            if (ageMap.ContainsKey(days))
                return ageMap[days];
            
            // Fallback to calculation for other values
            if (days % 365 == 0)
                return $"{days / 365} Years";
            else if (days % 30 == 0)
                return $"{days / 30} Months";
            else
                return $"{days} Days";
        }
        
        private MemoryStream CreateTravelPdf(int childId)
        {
            var childDetails = _db.Childs
                .Include(c => c.Clinic)
                .ThenInclude(clinic => clinic.Doctor)
                .FirstOrDefault(c => c.Id == childId);

            if (childDetails == null)
            {
                return null;
            }
            string patientName = childDetails.Name;
            string relation = childDetails.FatherName;
            DateTime dob = childDetails.DOB;
            string passport = childDetails.CNIC;
            string city = childDetails.City;
            string Nationality = childDetails.Nationality;
            string mrNumber = childDetails.City;
            string clinicName = childDetails.Clinic.Name;
            string doctorDetails = childDetails.Clinic.Doctor.DisplayName;
            string additionalInfo = childDetails.Clinic.Doctor.AdditionalInfo;
            string clinicAddress = childDetails.Clinic.Address;
            string clinicPhoneNumber = childDetails.Clinic.PhoneNumber;
            var output = new MemoryStream();
            using (var document = new Document(PageSize.A5.Rotate(), 22f, 22f, 20f, 20f))
            {
                PdfWriter writer = PdfWriter.GetInstance(document, output);
                writer.PageEvent = new FooterPageEvent(_db, childId);
                document.Open();
                var headerTable = new PdfPTable(2);
                headerTable.WidthPercentage = 100;
                headerTable.SetWidths(new float[] { 3, 1 });
                headerTable.SpacingAfter = 3f;

                PdfPCell headerCell = new PdfPCell();
                headerCell.Border = PdfPCell.NO_BORDER;
                headerCell.Padding = 0f;
                headerCell.AddElement(new Paragraph(doctorDetails, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)) { SpacingAfter = 1f });
                headerCell.AddElement(new Paragraph(additionalInfo, FontFactory.GetFont(FontFactory.HELVETICA, 6)) { SpacingAfter = 0f });
                headerTable.AddCell(headerCell);
                string logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Images", "logo-vaccinepk-new.png");
                if (System.IO.File.Exists(logoPath))
                {
                    var logo = Image.GetInstance(logoPath);
                    // Fit logo within the right column (1/4 of usable page width)
                    float usableWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
                    float rightColumnWidth = usableWidth / 4f;
                    float maxLogoWidth = Math.Max(40f, rightColumnWidth - 4f);
                    logo.ScaleToFit(maxLogoWidth, 60f);
                    PdfPCell logoCell = new PdfPCell(logo)
                    {
                        Border = PdfPCell.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        PaddingLeft = 0f,
                        PaddingRight = 0f,
                        PaddingTop = 6f,
                    };
                    headerTable.AddCell(logoCell);
                }
                else
                {
                    headerTable.AddCell(new PdfPCell(new Phrase("No Logo Available", FontFactory.GetFont(FontFactory.HELVETICA, 10))) { Border = PdfPCell.NO_BORDER });
                }
                document.Add(headerTable);

                var title = new Paragraph("IMMUNIZATION RECORD", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10));
                title.Alignment = Element.ALIGN_CENTER;
                title.SpacingBefore = 0f;
                title.SpacingAfter = 2f;
                document.Add(title);
                var patientTable = new PdfPTable(4) { WidthPercentage = 100 };
                var patientTableWidths = new float[] { 1.2f, 2.8f, 1.2f, 2.8f };
                patientTable.SetWidths(patientTableWidths);
                patientTable.SpacingAfter = 3f;
                patientTable.DefaultCell.BorderColor = BaseColor.LightGray;
                patientTable.DefaultCell.BorderWidth = 0.5f;
                var cellFontBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var cellFontNormal = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                patientTable.AddCell(CreateCell("Name:", cellFontBold, new BaseColor(235, 235, 235)));
                patientTable.AddCell(CreateCell(patientName, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell("S/D/W/o:", cellFontBold, new BaseColor(235, 235, 235)));
                patientTable.AddCell(CreateCell(relation, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell("Date of Birth:", cellFontBold, new BaseColor(235, 235, 235)));
                patientTable.AddCell(CreateCell(dob.ToString("dd/MM/yyyy"), cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell("Passport No:", cellFontBold, new BaseColor(235, 235, 235)));
                patientTable.AddCell(CreateCell(passport, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell("City:", cellFontBold, new BaseColor(235, 235, 235)));
                patientTable.AddCell(CreateCell(city, cellFontNormal, BaseColor.White));
                patientTable.AddCell(CreateCell("Nationality:", cellFontBold, new BaseColor(235, 235, 235)));
                patientTable.AddCell(CreateCell(Nationality, cellFontNormal, BaseColor.White));
                document.Add(new Paragraph(" ", FontFactory.GetFont(FontFactory.HELVETICA, 10)) { SpacingBefore = -10f });
                document.Add(patientTable);

                PdfPCell CreateCell(string text, Font font, BaseColor backgroundColor)
                {
                    var cell = new PdfPCell(new Phrase(text, font))
                    {
                        BackgroundColor = backgroundColor,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2f,
                        PaddingBottom = 2f
                    };
                    return cell;
                }

                var vaccineTable = new PdfPTable(7) { WidthPercentage = 100 };
                vaccineTable.SetWidths(new float[] { 1.2f, 1, 1.5f, 1, 1, 1, 1 });
                vaccineTable.DefaultCell.Border = PdfPCell.NO_BORDER;
                
                var child = _db.Childs
                    .Include(x => x.Schedules.Where(s => s.IsSkip != true)) // Exclude skipped schedules
                        .ThenInclude(s => s.Dose)
                    .Include(x => x.Schedules.Where(s => s.IsSkip != true))
                        .ThenInclude(s => s.Brand)
                    .FirstOrDefault(c => c.Id == childId);
                if (child == null)
                {
                    return null;
                }
                var dbSchedules = child.Schedules.ToList();
                var brandIds = dbSchedules
                    .Where(s => s.BrandId.HasValue)
                    .Select(s => s.BrandId.Value)
                    .Distinct()
                    .ToList();

                var latestStockByBrand = _db.Stocks
                    .Include(s => s.Bill)
                    .Where(s => brandIds.Contains(s.BrandId) && s.Bill.ClinicId == childDetails.ClinicId)
                    .OrderByDescending(s => !string.IsNullOrWhiteSpace(s.BatchLot) || s.Expiry != null)
                    .ThenByDescending(s => s.Bill.BillDate)
                    .ThenByDescending(s => s.Id)
                    .AsEnumerable()
                    .GroupBy(s => s.BrandId)
                    .ToDictionary(g => g.Key, g => g.First());

                var vaccineTable1 = new PdfPTable(7) { WidthPercentage = 100 };
                vaccineTable.SetWidths(new float[] { 1.2f, 1, 1.5f, 1, 1, 1, 1 });
                vaccineTable.DefaultCell.Border = PdfPCell.NO_BORDER;

                string[] headers = { "Vaccine", "Brand", "Manufacturer", "Batch/Lot", "Date Given", "Expiry", "Validity" };
               void AddVaccineTableHeader(PdfPTable table)
{
    foreach (string header in headers)
    {
        table.AddCell(new PdfPCell(new Phrase(header, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9)))
        {
            BorderColor = BaseColor.LightGray,
            BorderWidth = 0.5f,
            HorizontalAlignment = header == "Vaccine" ? PdfPCell.ALIGN_LEFT : PdfPCell.ALIGN_CENTER,
            PaddingTop = 3,
            PaddingBottom = 3,
            Border = Rectangle.TOP_BORDER | (header == "Vaccine" ? Rectangle.LEFT_BORDER : 0) | (header == "Validity" ? Rectangle.RIGHT_BORDER : 0)
        });
    }
}
AddVaccineTableHeader(vaccineTable);
int rowCount = 0;
                foreach (var schedule in dbSchedules)
                {
                    string vaccineName = schedule.Dose?.Name ?? "N/A";
                    string brand = schedule.Brand?.Name ?? "";
                    string manufacturer = schedule.Brand?.Manufacturer ?? "N/A";
                    latestStockByBrand.TryGetValue(schedule.BrandId ?? 0, out var latestStock);
                    string batchLot = latestStock?.BatchLot ?? "";
                    string dateGiven = (schedule.GivenDate.HasValue && schedule.GivenDate.Value != DateTime.MinValue) ? schedule.GivenDate.Value.ToString("dd/MM/yyyy") : "Due";
                    string expiry = latestStock?.Expiry?.ToString("dd/MM/yyyy") ?? "";
                    string validity = schedule.Validity != null ? GetYearOrMonthFromDays((int)schedule.Validity) : "N/A";

                    // Check if this is the last row
                    bool isLastRow = schedule == dbSchedules.Last();
                    // Define border style
                    int borderStyle = isLastRow ? Rectangle.BOTTOM_BORDER : Rectangle.NO_BORDER;
                    vaccineTable.AddCell(new PdfPCell(new Phrase(vaccineName, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = Rectangle.LEFT_BORDER | borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_LEFT,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });

                    vaccineTable.AddCell(new PdfPCell(new Phrase(brand, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_CENTER,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });

                    vaccineTable.AddCell(new PdfPCell(new Phrase(manufacturer, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_CENTER,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });

                    vaccineTable.AddCell(new PdfPCell(new Phrase(batchLot, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_CENTER,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });

                    vaccineTable.AddCell(new PdfPCell(new Phrase(dateGiven, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_CENTER,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });

                    vaccineTable.AddCell(new PdfPCell(new Phrase(expiry, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_CENTER,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });

                    vaccineTable.AddCell(new PdfPCell(new Phrase(validity, FontFactory.GetFont(FontFactory.HELVETICA, 9)))
                    {
                        Border = Rectangle.RIGHT_BORDER | borderStyle,
                        HorizontalAlignment = PdfPCell.ALIGN_CENTER,
                        BorderColor = BaseColor.LightGray,
                        BorderWidth = 0.5f,
                        PaddingTop = 2,
                        PaddingBottom = 3
                    });
                }
                document.Add(vaccineTable); 
                // document.Add(new Paragraph(" "));
                // document.Add(new Paragraph(" "));

                var baseUrl = "https://myapi.vaccinationcentre.com/api";
                var qrCodeUrl = $"{baseUrl}/Child/Travel-PDF-Download/{childId}";
                using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeUrl, QRCodeGenerator.ECCLevel.Q))
                {
                    var qrCode = new BitmapByteQRCode(qrCodeData);
                    byte[] qrCodeImage = qrCode.GetGraphic(18);

                    using (MemoryStream ms = new MemoryStream(qrCodeImage))
                    {
                        var pdfQrCode = iTextSharp.text.Image.GetInstance(ms.ToArray());
                        pdfQrCode.ScaleAbsolute(67.5f, 67.5f);
                        float tableWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
                        float widthsSum = patientTableWidths[0] + patientTableWidths[1] + patientTableWidths[2] + patientTableWidths[3];
                        float col3Start = document.LeftMargin + (tableWidth * (patientTableWidths[0] + patientTableWidths[1]) / widthsSum);
                        float col3Width = tableWidth * patientTableWidths[2] / widthsSum;
                        float qrCodeXPosition = col3Start + (col3Width - pdfQrCode.ScaledWidth) / 2f;
                        float qrCodeYPosition = document.PageSize.Height - document.TopMargin - pdfQrCode.ScaledHeight;
                        pdfQrCode.SetAbsolutePosition(qrCodeXPosition, qrCodeYPosition);
                        writer.DirectContent.AddImage(pdfQrCode);
                    }
                }
                document.Close();
            }
            output.Seek(0, SeekOrigin.Begin);
            return output;
        }

        private class FooterPageEvent : PdfPageEventHelper
        {
            private readonly Context _db;
            private readonly int _childId;

            public FooterPageEvent(Context db, int childId)
            {
                _db = db;
                _childId = childId;
            }

            public override void OnEndPage(PdfWriter writer, Document document)
{
    PdfContentByte cb = writer.DirectContent;
    int currentYear = DateTime.Now.Year;
    Font regularFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
    Font footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
    Font footerFont1 = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
    Font mrFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9);

    // Fetch clinic details dynamically
    var clinicDetails = _db
        .Childs.Include(c => c.Clinic)
        .Where(c => c.Id == _childId)
        .Select(c => new
        {
            c.Clinic.Name,
            c.Clinic.RegNo,
            c.Clinic.Address,
            c.Clinic.PhoneNumber,
        })
        .FirstOrDefault();

    if (clinicDetails != null)
    {
        var clinicName = clinicDetails.Name;
        var regNo = clinicDetails.RegNo;
        var address = clinicDetails.Address;
        var phoneNumber = clinicDetails.PhoneNumber;
        var email = "info@vaccine.pk";

        Phrase phrase = new Phrase();
        phrase.Add(new Chunk($"{clinicName} ", footerFont1));
        phrase.Add(new Chunk($"({regNo})", footerFont));

        // 3-column table for address, phone, email
        PdfPTable contactTable = new PdfPTable(3);
        contactTable.TotalWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
        contactTable.SetWidths(new float[] { 2, 1, 2 });

        PdfPCell addressCell = new PdfPCell(new Phrase(address, footerFont))
        {
            Border = Rectangle.NO_BORDER,
            HorizontalAlignment = Element.ALIGN_LEFT,
            Padding = 5,
            BackgroundColor = new BaseColor(235, 235, 235)
        };
        PdfPCell phoneCell = new PdfPCell(new Phrase($"Phone: {phoneNumber}", footerFont))
        {
            Border = Rectangle.NO_BORDER,
            HorizontalAlignment = Element.ALIGN_CENTER,
            Padding = 5,
            BackgroundColor = new BaseColor(235, 235, 235)
        };
        PdfPCell emailCell = new PdfPCell(new Phrase(email, footerFont))
        {
            Border = Rectangle.NO_BORDER,
            HorizontalAlignment = Element.ALIGN_RIGHT,
            Padding = 5,
            BackgroundColor = new BaseColor(235, 235, 235)
        };

        contactTable.AddCell(addressCell);
        contactTable.AddCell(phoneCell);
        contactTable.AddCell(emailCell);

        float contactTableTopY = document.BottomMargin + contactTable.TotalHeight;
        contactTable.WriteSelectedRows(0, -1, document.LeftMargin, contactTableTopY, cb);

        float footerTextY = contactTableTopY + 6f;
        float clinicLineY = footerTextY + 14f;

        ColumnText.ShowTextAligned(cb, Element.ALIGN_LEFT, phrase, document.LeftMargin + 5, clinicLineY, 0);

        ColumnText.ShowTextAligned(
            cb,
            Element.ALIGN_RIGHT,
            new Phrase($"MR No: {currentYear}-{_childId}", mrFont),
            document.PageSize.Width - document.RightMargin,
            clinicLineY,
            0
        );

        Phrase verificationNote = new Phrase(
            "This is a computer generated verifiable certificate. It does not require physical stamp/signatures. " +
            "For verification, scan the QR code or visit https://vaccinationcentre.com/verify and enter MR number.",
            regularFont
        );

        ColumnText noteColumn = new ColumnText(cb);
        noteColumn.SetSimpleColumn(
            verificationNote,
            document.LeftMargin + 5,
            document.BottomMargin + 10f,
            document.PageSize.Width - document.RightMargin,
            footerTextY + 16f,
            9f,
            Element.ALIGN_LEFT
        );
        noteColumn.Go();
    }
    else
    {
        ColumnText.ShowTextAligned(
            cb,
            Element.ALIGN_LEFT,
            new Phrase("Clinic details not found", footerFont),
            document.LeftMargin + 5,
            document.BottomMargin + 32f,
            0
        );

        Phrase fallbackNote = new Phrase(
            "This is a computer-generated verifiable certificate. It does not require physical stamp/signatures. " +
            "For verification, scan the QR code.",
            regularFont
        );

        ColumnText fallbackColumn = new ColumnText(cb);
        fallbackColumn.SetSimpleColumn(
            fallbackNote,
            document.LeftMargin + 5,
            document.BottomMargin + 10f,
            document.PageSize.Width - document.RightMargin,
            document.BottomMargin + 30f,
            9f,
            Element.ALIGN_LEFT
        );
        fallbackColumn.Go();
    }
}
        }

        [HttpGet("Travel-PDF-Download/{id}")]
        public IActionResult GenerateVerifyTravelPdf(int id)
        {
            var fileUrl = $"https://myapi.vaccinationcentre.com/api/Child/Travel-PDF-Download-Verification/{id}";

            var child = _db.Childs
                .Include(c => c.Clinic)
                .Include(c => c.Schedules)
                    .ThenInclude(s => s.Dose)
                .Include(c => c.Schedules)
                    .ThenInclude(s => s.Brand)
                .FirstOrDefault(c => c.Id == id);

            string HtmlEncode(string value)
            {
                return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
            }

            string FormatDate(DateTime? value)
            {
                return value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";
            }

            string FormatValidity(int? value)
            {
                if (!value.HasValue)
                {
                    return "-";
                }

                return GetYearOrMonthFromDays(value.Value);
            }

            var currentYear = DateTime.UtcNow.AddHours(5).Year;
            var mrNo = $"{currentYear}-{id}";
            var guardian = child?.FatherName ?? "-";
            var childName = child?.Name ?? "-";
            var city = child?.City ?? "-";
            var nationality = string.IsNullOrWhiteSpace(child?.Nationality) ? "-" : child?.Nationality;
            var passport = string.IsNullOrWhiteSpace(child?.CNIC) ? "-" : child?.CNIC;
            var dob = child?.DOB.ToString("dd/MM/yyyy") ?? "-";

            var completedSchedules = child?.Schedules
                .Where(s => s.IsDone || s.GivenDate.HasValue)
                .OrderBy(s => s.GivenDate ?? s.Date)
                .ToList() ?? new List<Schedule>();

            var statusText = completedSchedules.Count > 0 ? "Vaccinated" : "Not Vaccinated";
            var statusClass = completedSchedules.Count > 0 ? "status-ok" : "status-warn";

            var rows = new StringBuilder();
            if (completedSchedules.Count == 0)
            {
                rows.AppendLine("<tr><td colspan='7' class='empty'>No vaccination records found.</td></tr>");
            }
            else
            {
                foreach (var item in completedSchedules)
                {
                    rows.AppendLine($@"
                        <tr>
                            <td>{HtmlEncode(item.Dose?.Name ?? "-")}</td>
                            <td>{HtmlEncode(item.Brand?.Name ?? "-")}</td>
                            <td>{HtmlEncode(item.Manufacturer ?? "-")}</td>
                            <td>{HtmlEncode(item.Lot ?? "-")}</td>
                            <td>{HtmlEncode(FormatDate(item.GivenDate))}</td>
                            <td>{HtmlEncode(FormatDate(item.Expiry))}</td>
                            <td>{HtmlEncode(FormatValidity(item.Validity))}</td>
                        </tr>");
                }
            }

            string htmlContent = $@"
                                    <!DOCTYPE html>
                                    <html lang='en'>
                                    <head>
                                        <meta charset='UTF-8'>
                                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                                        <title>Immunization Record</title>
                                        <style>
                                            :root {{
                                                --ink: #1f2933;
                                                --muted: #6b7280;
                                                --line: #e5e7eb;
                                                --brand: #0b4f6c;
                                                --brand-2: #0f7aa5;
                                                --accent: #2f9e44;
                                                --warn: #e67700;
                                                --card: #ffffff;
                                                --soft: #f3f6f9;
                                            }}
                                            * {{ box-sizing: border-box; }}
                                            body {{
                                                margin: 0;
                                                font-family: 'Trebuchet MS', Arial, sans-serif;
                                                color: var(--ink);
                                                background: radial-gradient(circle at top right, #e9f4fb 0%, #f7fbff 45%, #ffffff 100%);
                                            }}
                                            header {{
                                                background: linear-gradient(120deg, var(--brand) 0%, var(--brand-2) 100%);
                                                color: #fff;
                                                padding: 18px 16px;
                                                border-bottom: 4px solid #0a3d52;
                                            }}
                                            .brand {{
                                                display: flex;
                                                align-items: center;
                                                gap: 12px;
                                                font-size: 20px;
                                                font-weight: 700;
                                                letter-spacing: 0.5px;
                                            }}
                                            .brand-mark {{
                                                width: 38px;
                                                height: 38px;
                                                border-radius: 50%;
                                                background: #0a3d52;
                                                display: grid;
                                                place-items: center;
                                                font-weight: 800;
                                                font-size: 18px;
                                            }}
                                            .container {{
                                                max-width: 980px;
                                                margin: 0 auto;
                                                padding: 18px 16px 32px;
                                            }}
                                            .title {{
                                                font-size: 28px;
                                                font-weight: 800;
                                                margin: 18px 0 4px;
                                            }}
                                            .subtitle {{
                                                color: var(--muted);
                                                margin: 0 0 18px;
                                            }}
                                            .card {{
                                                background: var(--card);
                                                border: 1px solid var(--line);
                                                border-radius: 14px;
                                                box-shadow: 0 10px 22px rgba(15, 23, 42, 0.08);
                                                padding: 16px;
                                                margin-bottom: 18px;
                                            }}
                                            .status-bar {{
                                                display: grid;
                                                grid-template-columns: 1.2fr 1fr;
                                                gap: 12px;
                                            }}
                                            .status {{
                                                display: inline-flex;
                                                align-items: center;
                                                gap: 8px;
                                                padding: 8px 12px;
                                                border-radius: 999px;
                                                font-weight: 700;
                                                font-size: 14px;
                                            }}
                                            .status-ok {{ background: #e6fcf5; color: #0c6b58; border: 1px solid #96f2d7; }}
                                            .status-warn {{ background: #fff4e6; color: #a64900; border: 1px solid #ffd8a8; }}
                                            .info-grid {{
                                                display: grid;
                                                grid-template-columns: repeat(2, minmax(0, 1fr));
                                                gap: 8px 14px;
                                                margin-top: 10px;
                                            }}
                                            .info-item {{
                                                padding: 8px 10px;
                                                border-radius: 10px;
                                                background: var(--soft);
                                                border: 1px solid #e6edf3;
                                            }}
                                            .info-item span {{
                                                display: block;
                                                font-size: 12px;
                                                color: var(--muted);
                                            }}
                                            .info-item strong {{
                                                font-size: 14px;
                                                display: block;
                                                margin-top: 4px;
                                            }}
                                            .actions {{
                                                display: flex;
                                                gap: 12px;
                                                align-items: center;
                                                flex-wrap: wrap;
                                            }}
                                            .btn {{
                                                background: var(--brand);
                                                color: #fff;
                                                padding: 10px 16px;
                                                border-radius: 10px;
                                                text-decoration: none;
                                                font-weight: 700;
                                                box-shadow: 0 6px 12px rgba(11, 79, 108, 0.2);
                                            }}
                                            .table {{
                                                width: 100%;
                                                border-collapse: collapse;
                                                font-size: 13px;
                                            }}
                                            .table th,
                                            .table td {{
                                                padding: 10px 8px;
                                                border-bottom: 1px solid var(--line);
                                                text-align: left;
                                            }}
                                            .table th {{
                                                background: #f0f6fb;
                                                color: #0b4f6c;
                                                font-size: 12px;
                                                text-transform: uppercase;
                                                letter-spacing: 0.6px;
                                            }}
                                            .table .empty {{
                                                text-align: center;
                                                color: var(--muted);
                                                padding: 16px 8px;
                                            }}
                                            @media (max-width: 768px) {{
                                                .status-bar {{ grid-template-columns: 1fr; }}
                                                .info-grid {{ grid-template-columns: 1fr; }}
                                                .title {{ font-size: 22px; }}
                                            }}
                                        </style>
                                    </head>
                                    <body>
                                        <header>
                                            <div class='brand'>
                                                <div class='brand-mark'>V</div>
                                                Vaccine.pk Verification
                                            </div>
                                        </header>
                                        <div class='container'>
                                            <div class='title'>Immunization Record</div>
                                            <p class='subtitle'>Verified vaccination summary for travel and official use.</p>

                                            <div class='card status-bar'>
                                                <div>
                                                    <div class='status {statusClass}'>Status: {HtmlEncode(statusText)}</div>
                                                    <div class='info-grid'>
                                                        <div class='info-item'>
                                                            <span>MR No.</span>
                                                            <strong>{HtmlEncode(mrNo)}</strong>
                                                        </div>
                                                        <div class='info-item'>
                                                            <span>Name</span>
                                                            <strong>{HtmlEncode(childName)}</strong>
                                                        </div>
                                                        <div class='info-item'>
                                                            <span>S/D/W/O</span>
                                                            <strong>{HtmlEncode(guardian ?? "-")}</strong>
                                                        </div>
                                                        <div class='info-item'>
                                                            <span>Passport / CNIC</span>
                                                            <strong>{HtmlEncode(passport)}</strong>
                                                        </div>
                                                        <div class='info-item'>
                                                            <span>Date of Birth</span>
                                                            <strong>{HtmlEncode(dob)}</strong>
                                                        </div>
                                                        <div class='info-item'>
                                                            <span>City / Nationality</span>
                                                            <strong>{HtmlEncode(city)} / {HtmlEncode(nationality)}</strong>
                                                        </div>
                                                    </div>
                                                </div>
                                                <div class='actions'>
                                                    <a class='btn' href='{fileUrl}' target='_blank'>Open PDF</a>
                                                    <div class='subtitle'>Use the PDF for printing or offline sharing.</div>
                                                </div>
                                            </div>

                                            <div class='card'>
                                                <h3>Vaccines</h3>
                                                <table class='table'>
                                                    <thead>
                                                        <tr>
                                                            <th>Vaccine</th>
                                                            <th>Brand</th>
                                                            <th>Manufacturer</th>
                                                            <th>Batch/Lot</th>
                                                            <th>Date Given</th>
                                                            <th>Expiry</th>
                                                            <th>Validity</th>
                                                        </tr>
                                                    </thead>
                                                    <tbody>
                                                        {rows}
                                                    </tbody>
                                                </table>
                                            </div>
                                        </div>
                                    </body>
                                    </html>";

            return new ContentResult
            {
                Content = htmlContent,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        [HttpGet("agents/{doctorId}")]
        public Response<IEnumerable<string>> GetAgentNamesByDoctorId(long doctorId)
        {
            try
            {
                // Fetch distinct agent names where Agent is not null/empty and matches the given DoctorId
                var agentNames = _db.Childs
                    .Where(c => !string.IsNullOrEmpty(c.Agent) && c.Clinic.DoctorId == doctorId)
                    .Select(c => c.Agent)
                    .Distinct()
                    .ToList();

                if (!agentNames.Any())
                {
                    return new Response<IEnumerable<string>>(false, "No agents found for the specified doctor", null);
                }

                return new Response<IEnumerable<string>>(true, "Agents retrieved successfully", agentNames);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving agents: {ex.Message}");
                return new Response<IEnumerable<string>>(false, "An error occurred while retrieving agents", null);
            }
        }

        // ── IMMUNIZATION CARD: FRONT SIDE (Page 1) ────────────────────────
        [HttpGet("{id}/immunization-card-front")]
        public IActionResult ImmunizationCardFront(int id)
        {
            var child = _db.Childs
                .Include(c => c.User)
                .Include(c => c.Clinic).ThenInclude(cl => cl.Doctor)
                .FirstOrDefault(c => c.Id == id);
            if (child == null) return NotFound("Child not found");

            var doctor = child.Clinic?.Doctor;
            var clinic = child.Clinic;

            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A5, 18, 18, 18, 18);
            var writer = PdfWriter.GetInstance(doc, ms);
            writer.CloseStream = false;
            doc.Open();

            // ── Fonts ────────────────────────────────────────────────────────
            var normXs  = FontFactory.GetFont(FontFactory.HELVETICA, 8f);
            var normSm  = FontFactory.GetFont(FontFactory.HELVETICA, 10f);
            var boldSm  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10f);
            var normMd  = FontFactory.GetFont(FontFactory.HELVETICA, 10f);
            var boldMd  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13f);
            var boldLg  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13f);
            var titleFt = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14f);
            var hdrWhite= FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11f, BaseColor.White);
            var hdrBg   = new BaseColor(21, 101, 192);
            var altBg   = new BaseColor(244, 246, 252);

            // ── TOP SECTION ──────────────────────────────────────────────────
            var topTable = new PdfPTable(2) { WidthPercentage = 100 };
            topTable.SetWidths(new float[] { 50f, 50f });

            // ── LEFT: IMMUNIZATION CARD title + patient info table ───────────
            var leftContent = new PdfPTable(2);
            leftContent.SetWidths(new float[] { 32f, 68f });
            leftContent.WidthPercentage = 100;
            // value column ≈ 68% of half-page minus cell padding on both sides
            float valColPt = ((doc.PageSize.Width - 36f) * 0.5f - 8f) * 0.68f - 10f;
            var infoValBf  = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);
            Font FitFont(string text) {
                float maxSize = 10f;
                if (string.IsNullOrEmpty(text)) return normSm;
                float w = infoValBf.GetWidthPoint(text, maxSize);
                if (w <= valColPt) return normSm;
                return FontFactory.GetFont(FontFactory.HELVETICA, Math.Max(maxSize * valColPt / w, 6f));
            }
            void InfoRow(string label, string val) {
                leftContent.AddCell(new PdfPCell(new Phrase(label, boldSm)) { Border = Rectangle.BOX, Padding = 5 });
                leftContent.AddCell(new PdfPCell(new Phrase(val ?? "", FitFont(val ?? ""))) { Border = Rectangle.BOX, Padding = 5 });
            }
            InfoRow("Name",     child.Name);
            InfoRow("S/D/W of", child.FatherName ?? "");
            InfoRow("DoB",      child.DOB.ToString("dd-MM-yyyy"));
            InfoRow("City",     child.City ?? "");
            var mobileRaw = child.User?.MobileNumber ?? "";
            var mobileDisplay = string.IsNullOrWhiteSpace(mobileRaw) ? "" : "+92 " + mobileRaw;
            InfoRow("Phone",    mobileDisplay);

            var leftCell = new PdfPCell { Border = 0, Padding = 4 };
            leftCell.AddElement(new Paragraph("IMMUNIZATION CARD", titleFt)
                { Alignment = Element.ALIGN_CENTER, SpacingAfter = 5f });
            leftCell.AddElement(leftContent);
            topTable.AddCell(leftCell);

            // ── RIGHT: Logo (GREEN) + Doctor box (RED) ───────────────────────
            var rightCell = new PdfPCell { Border = 0, Padding = 4 };

            // GREEN area: clinic logo + clinic name (matching schedule top-right)
            var logoPath = clinic?.MonogramImage != null
                ? Path.Combine(_host.ContentRootPath, clinic.MonogramImage) : null;

            if (logoPath != null && System.IO.File.Exists(logoPath))
            {
                try
                {
                    var logo = iTextSharpImage.GetInstance(logoPath);
                    logo.ScaleAbsolute(120f, 60f);
                    logo.Alignment = Element.ALIGN_CENTER;
                    rightCell.AddElement(logo);
                }
                catch { /* skip logo if load fails */ }
            }

            // Doctor name (no box)
            rightCell.AddElement(new Paragraph(doctor?.DisplayName ?? doctor?.FirstName ?? "", boldMd)
                { Alignment = Element.ALIGN_CENTER, SpacingBefore = 4f });

            // Additional info from doctor profile
            var additionalInfo = doctor?.AdditionalInfo ?? "";
            if (!string.IsNullOrWhiteSpace(additionalInfo))
                rightCell.AddElement(new Paragraph(additionalInfo, normXs)
                    { Alignment = Element.ALIGN_CENTER, SpacingBefore = 2f });

            topTable.AddCell(rightCell);
            topTable.SpacingAfter = 5f;
            doc.Add(topTable);

            // ── SCHEDULE TABLE ────────────────────────────────────────────────
            doc.Add(new Paragraph("LATEST IMMUNIZATION SCHEDULE", boldLg)
                { Alignment = Element.ALIGN_CENTER });
            doc.Add(new Paragraph("FOR KIDS LIVING IN PAKISTAN", normMd)
                { Alignment = Element.ALIGN_CENTER, SpacingAfter = 4f });

            var schedTable = new PdfPTable(2) { WidthPercentage = 100 };
            schedTable.SetWidths(new float[] { 28f, 72f });

            PdfPCell SHdr(string t) => new PdfPCell(new Phrase(t, hdrWhite))
                { BackgroundColor = hdrBg, Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER };
            void SRow(string age, string vax, bool shade = false) {
                var bg = shade ? altBg : BaseColor.White;
                schedTable.AddCell(new PdfPCell(new Phrase(age, normSm)) { Padding = 5, BackgroundColor = bg });
                schedTable.AddCell(new PdfPCell(new Phrase(vax, normSm)) { Padding = 5, BackgroundColor = bg });
            }
            schedTable.AddCell(SHdr("Age")); schedTable.AddCell(SHdr("Vaccines"));
            SRow("Birth",         "BCG, OPV, Hepatitis B");
            SRow("6-8 Weeks",     "OPV/IPV, DPT, HBV, Hib, PCV, Rotavirus GE",  true);
            SRow("10-16 Weeks",   "OPV/IPV, DPT, HBV, Hib, PCV, Rotavirus GE");
            SRow("14-24 Weeks",   "OPV/IPV, DPT, HBV, Hib, PCV",               true);
            SRow("6 & 7 Months",  "Influenza");
            SRow("9 Months",      "MR, TCV, IPV, MenACWY",                      true);
            SRow("12-15 Months",  "Chickenpox, Hepatitis A, MenACWY, MMR, PCV");
            SRow("18-21 Months",  "Hepatitis A, IPV, DPT, HBV, Hib",            true);
            SRow("3-4 Years",     "MMR, Chickenpox, Typhoid");
            SRow("5 Years",       "DTaP, PPSV, Covid19",                         true);
            SRow("9 Years",       "HPV");
            schedTable.SpacingAfter = 0f;
            doc.Add(schedTable);

            // ── FOOTER + DISCLAIMER pinned to absolute bottom of page ─────────
            var footerLine = new StringBuilder();
            footerLine.Append(clinic?.Name ?? "");
            if (!string.IsNullOrEmpty(clinic?.Address))  footerLine.Append("  |  " + clinic.Address);
            if (!string.IsNullOrEmpty(clinic?.PhoneNumber)) footerLine.Append("  |  " + clinic.PhoneNumber);

            var cb = writer.DirectContent;
            float pageW  = doc.PageSize.Width;
            float lm = 18f, bm = 18f;
            float contentW = pageW - lm - lm;

            // Footer bar (blue) at very bottom
            float footerH = 22f;
            cb.SaveState();
            cb.SetColorFill(new BaseColor(21, 101, 192));
            cb.Rectangle(lm, bm, contentW, footerH);
            cb.Fill();
            cb.RestoreState();
            ColumnText.ShowTextAligned(cb, Element.ALIGN_CENTER,
                new Phrase(footerLine.ToString(),
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8f, BaseColor.White)),
                pageW / 2f, bm + 7f, 0f);

            // Disclaimer text just above footer
            float disclaimerBottom = bm + footerH + 4f;
            float disclaimerTop    = disclaimerBottom + 60f;
            var disclaimerCt = new ColumnText(cb);
            disclaimerCt.SetSimpleColumn(lm, disclaimerBottom, pageW - lm, disclaimerTop);
            disclaimerCt.AddText(new Phrase(9.5f,
                "Vaccines can cause fever, redness, rashes and pain. Rotarix vaccine can have loose " +
                "motions and intestinal complications. Pertussis vaccine may cause excessive crying " +
                "episodes and fits also rarely. This immunization card is valid to produce on demand at " +
                "all embassies, airports and schools of the world.",
                normXs));
            disclaimerCt.Go();

            doc.Close();
            var pdfBytes = ms.ToArray();
            var fileName = $"{child.Name.Replace(" ", "")}_ImmunizationCard_{DateTime.Now:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        // ── IMMUNIZATION CARD: VACCINE WISE (Page 2) ──────────────────────
        [HttpGet("{id}/immunization-card-vaccine")]
        public IActionResult ImmunizationCardVaccineWise(int id)
        {
            var dbChild = _db.Childs
                .Include(c => c.User)
                .Include(c => c.Clinic).ThenInclude(cl => cl.Doctor)
                .FirstOrDefault(c => c.Id == id);
            if (dbChild == null) return NotFound("Child not found");

            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A5, 15, 15, 15, 15);
            PdfWriter.GetInstance(doc, ms).CloseStream = false;
            doc.Open();

            var boldSm = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8f);
            var normSm = FontFactory.GetFont(FontFactory.HELVETICA, 7.5f);
            var boldMd = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11f);
            var normTy = FontFactory.GetFont(FontFactory.HELVETICA, 7f);
            var normNm = FontFactory.GetFont(FontFactory.HELVETICA, 8f);
            var hBg    = new BaseColor(21, 101, 192);
            var altBg  = new BaseColor(244, 246, 252);

            // ── Header: two centred lines ────────────────────────────────────
            doc.Add(new Paragraph("IMMUNIZATION RECORD", boldMd)
                { Alignment = Element.ALIGN_CENTER, SpacingAfter = 2f });

            var nameLine = new Paragraph { Alignment = Element.ALIGN_CENTER, SpacingAfter = 8f };
            nameLine.Add(new Chunk(dbChild.Name, boldSm));
            nameLine.Add(new Chunk("   S/D/W of   " + (dbChild.FatherName ?? ""), normNm));
            doc.Add(nameLine);

            // ── Hardcoded vaccine-wise template with rowspan ─────────────────
            // (vaccineName, doseNumber 0=blank, ageLabel)
            var template = new List<(string Vaccine, int DoseNum, string Age)>
            {
                ("BCG, OPV",                    0, "At Birth"),
                ("Hepatitis B",                 0, "At Birth"),
                ("IPV,DPT,HBV,Hib",             1, "6-8 Weeks"),
                ("IPV,DPT,HBV,Hib",             2, "10-16 Weeks"),
                ("IPV,DPT,HBV,Hib",             3, "14-24 Weeks"),
                ("IPV,DPT,HBV,Hib",             4, "21-24 Months"),
                ("Pneumococcal",                1, "6-8 Weeks"),
                ("Pneumococcal",                2, "10-16 Weeks"),
                ("Pneumococcal",                3, "14-24 Weeks"),
                ("Pneumococcal",                4, "12-15 Months"),
                ("Rotavirus GE",                1, "6-8 Weeks"),
                ("Rotavirus GE",                2, "10-16 Weeks"),
                ("Influenza (Yearly)",          1, "6, 7 Months"),
                ("Meningococcal\n(Men ACWY)",   1, "9 Months"),
                ("Meningococcal\n(Men ACWY)",   2, "12 Months"),
                ("Typhoid/TCV",                 1, "9 Months"),
                ("MR (Measles, Rubella)",       1, "9 Months"),
                ("MMR (Measles,\nMumps, Rubella)", 1, "15 Months"),
                ("MMR (Measles,\nMumps, Rubella)", 2, "30 Months"),
                ("Chickenpox",                  1, "12-15 Months"),
                ("Chickenpox",                  2, "2 Years"),
                ("Hepatitis A",                 1, "12-15 Months"),
                ("Hepatitis A",                 2, "18-21 Months"),
                ("PPSV/PCV",                    1, "4-6 Years"),
                ("DTaP",                        2, "4-6 Years"),
            };

            // Count how many rows each vaccine name spans
            var spanCount = new Dictionary<string, int>();
            foreach (var row in template)
                spanCount[row.Vaccine] = spanCount.ContainsKey(row.Vaccine)
                    ? spanCount[row.Vaccine] + 1 : 1;

            var tbl = new PdfPTable(8) { WidthPercentage = 100 };
            tbl.SetWidths(new float[] { 22f, 5f, 14f, 14f, 10f, 10f, 14f, 10f });

            PdfPCell Hdr(string t) => new PdfPCell(
                new Phrase(t, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7f, BaseColor.White)))
                { BackgroundColor = hBg, Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER };
            tbl.AddCell(Hdr("VACCINES")); tbl.AddCell(Hdr("#")); tbl.AddCell(Hdr("AGE"));
            tbl.AddCell(Hdr("GIVEN")); tbl.AddCell(Hdr("Wt(kg)")); tbl.AddCell(Hdr("OFC"));
            tbl.AddCell(Hdr("BRAND")); tbl.AddCell(Hdr("Sign."));

            var emittedVaccine = new HashSet<string>();
            int rowIdx2 = 0;
            foreach (var row in template)
            {
                var bg = rowIdx2 % 2 == 0 ? BaseColor.White : altBg;
                PdfPCell C(string v, int rs = 1) => new PdfPCell(new Phrase(v, normSm))
                    { Padding = 4, BackgroundColor = bg, Rowspan = rs };

                if (!emittedVaccine.Contains(row.Vaccine))
                {
                    var span = spanCount[row.Vaccine];
                    var nameCell = new PdfPCell(new Phrase(row.Vaccine, boldSm))
                        { Padding = 4, BackgroundColor = bg, Rowspan = span,
                          VerticalAlignment = Element.ALIGN_MIDDLE };
                    tbl.AddCell(nameCell);
                    emittedVaccine.Add(row.Vaccine);
                }

                string numStr = row.DoseNum > 0 ? row.DoseNum.ToString() : "";
                tbl.AddCell(C(numStr)); tbl.AddCell(C(row.Age));
                tbl.AddCell(C("")); tbl.AddCell(C("")); tbl.AddCell(C(""));
                tbl.AddCell(C("")); tbl.AddCell(C(""));
                rowIdx2++;
            }
            tbl.SpacingAfter = 8f;
            doc.Add(tbl);

            AddRecurringVaccinesFooter(doc, normTy, boldSm);
            doc.Close();

            var pdfBytes = ms.ToArray();
            return File(pdfBytes, "application/pdf",
                $"{dbChild.Name.Replace(" ", "")}_VaccineWise_{DateTime.Now:yyyyMMdd}.pdf");
        }

        // ── IMMUNIZATION CARD: AGE WISE (Page 3) ──────────────────────────
        [HttpGet("{id}/immunization-card-age")]
        public IActionResult ImmunizationCardAgeWise(int id)
        {
            var dbChild = _db.Childs
                .Include(c => c.User)
                .Include(c => c.Clinic).ThenInclude(cl => cl.Doctor)
                .FirstOrDefault(c => c.Id == id);
            if (dbChild == null) return NotFound("Child not found");

            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A5, 15, 15, 15, 15);
            PdfWriter.GetInstance(doc, ms).CloseStream = false;
            doc.Open();

            var boldSm = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8f);
            var normSm = FontFactory.GetFont(FontFactory.HELVETICA, 7.5f);
            var boldMd = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11f);
            var normTy = FontFactory.GetFont(FontFactory.HELVETICA, 7f);
            var hBg    = new BaseColor(21, 101, 192);
            var altBg  = new BaseColor(244, 246, 252);

            // ── Header: two centred lines ────────────────────────────────────
            doc.Add(new Paragraph("IMMUNIZATION RECORD", boldMd)
                { Alignment = Element.ALIGN_CENTER, SpacingAfter = 2f });

            var nameLine = new Paragraph { Alignment = Element.ALIGN_CENTER, SpacingAfter = 8f };
            nameLine.Add(new Chunk(dbChild.Name, boldSm));
            nameLine.Add(new Chunk("   S/D/W of   " + (dbChild.FatherName ?? ""), normSm));
            doc.Add(nameLine);

            // ── Hardcoded age-wise template with rowspan ─────────────────────
            // (ageLabel, vaccineText)
            var template = new List<(string Age, string Vaccine)>
            {
                ("At Birth",    "BCG, OPV"),
                ("At Birth",    "Hepatitis B"),
                ("6 Weeks",     "IPV,DPT,HBV,Hib 1"),
                ("6 Weeks",     "Rotavirus GE 1"),
                ("10 Weeks",    "Pneumococcal 1"),
                ("10 Weeks",    "Rotavirus GE 2"),
                ("14 Weeks",    "IPV,DPT,HBV,Hib 2"),
                ("14 Weeks",    "Pneumococcal 2"),
                ("18 Weeks",    "IPV,DPT,HBV,Hib 3"),
                ("18 Weeks",    "Pneumococcal 3"),
                ("6, 7 Months", "Flu (Yearly)"),
                ("9 Months",    "MenACWY 1"),
                ("9 Months",    "Typhoid/TCV"),
                ("9 Months",    "MR 1"),
                ("1 Year",      "Chickenpox 1"),
                ("1 Year",      "MenACWY 2"),
                ("13 Months",   "Pneumococcal 4"),
                ("13 Months",   "Hepatitis A 1"),
                ("15 Months",   "MMR 1"),
                ("18 Months",   "IPV,DPT,HBV,Hib 4"),
                ("19 Months",   "Hepatitis A 2"),
                ("2 Years",     "Chickenpox 2"),
                ("30 Months",   "MMR 2"),
                ("4-6 Years",   "PPSV/PCV"),
                ("4-6 Years",   "DtaP"),
            };

            // Count rowspan per age label (preserving order — track by first occurrence)
            var ageOrder = new List<string>();
            var ageSpan  = new Dictionary<string, int>();
            foreach (var row in template)
            {
                if (!ageSpan.ContainsKey(row.Age)) { ageSpan[row.Age] = 0; ageOrder.Add(row.Age); }
                ageSpan[row.Age]++;
            }

            var tbl = new PdfPTable(7) { WidthPercentage = 100 };
            tbl.SetWidths(new float[] { 16f, 28f, 14f, 10f, 10f, 12f, 10f });

            PdfPCell Hdr(string t) => new PdfPCell(
                new Phrase(t, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7f, BaseColor.White)))
                { BackgroundColor = hBg, Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER };
            tbl.AddCell(Hdr("AGE")); tbl.AddCell(Hdr("VACCINES")); tbl.AddCell(Hdr("GIVEN"));
            tbl.AddCell(Hdr("Wt(kg)")); tbl.AddCell(Hdr("OFC")); tbl.AddCell(Hdr("BRAND")); tbl.AddCell(Hdr("Sign."));

            var emittedAge = new HashSet<string>();
            int rowIdx3 = 0;
            foreach (var row in template)
            {
                var bg = rowIdx3 % 2 == 0 ? BaseColor.White : altBg;
                PdfPCell C(string v) => new PdfPCell(new Phrase(v, normSm))
                    { Padding = 4, BackgroundColor = bg };

                if (!emittedAge.Contains(row.Age))
                {
                    var ageCell = new PdfPCell(new Phrase(row.Age, boldSm))
                        { Padding = 4, BackgroundColor = bg, Rowspan = ageSpan[row.Age],
                          VerticalAlignment = Element.ALIGN_MIDDLE };
                    tbl.AddCell(ageCell);
                    emittedAge.Add(row.Age);
                }

                tbl.AddCell(C(row.Vaccine));
                tbl.AddCell(C("")); tbl.AddCell(C("")); tbl.AddCell(C(""));
                tbl.AddCell(C("")); tbl.AddCell(C(""));
                rowIdx3++;
            }
            tbl.SpacingAfter = 8f;
            doc.Add(tbl);

            AddRecurringVaccinesFooter(doc, normTy, boldSm);
            doc.Close();

            var pdfBytes = ms.ToArray();
            return File(pdfBytes, "application/pdf",
                $"{dbChild.Name.Replace(" ", "")}_AgeWise_{DateTime.Now:yyyyMMdd}.pdf");
        }

        private void AddRecurringVaccinesFooter(Document doc, iTextSharpFont normFont, iTextSharpFont boldFont)
        {
            var footTbl = new PdfPTable(6) { WidthPercentage = 100 };
            var hBg = new BaseColor(21, 101, 192);
            PdfPCell FHdr(string t) => new PdfPCell(new Phrase(t, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 6, BaseColor.White)))
                { BackgroundColor = hBg, Padding = 2, HorizontalAlignment = Element.ALIGN_CENTER };
            PdfPCell FCell(string t = "") => new PdfPCell(new Phrase(t, normFont))
                { Padding = 2, MinimumHeight = 14f, HorizontalAlignment = Element.ALIGN_CENTER };

            footTbl.AddCell(FHdr("Flu")); footTbl.AddCell(FHdr("Typhoid"));
            footTbl.AddCell(FHdr("Covid-19")); footTbl.AddCell(FHdr("HPV"));
            footTbl.AddCell(FHdr("Tdap")); footTbl.AddCell(FHdr("MMR 3"));

            footTbl.AddCell(FCell("Yearly")); footTbl.AddCell(FCell("Every 3rd Year"));
            footTbl.AddCell(FCell("5 Years")); footTbl.AddCell(FCell("9 Years"));
            footTbl.AddCell(FCell("12 Years")); footTbl.AddCell(FCell("Teenage"));

            // 3 blank rows for filling in
            for (int i = 0; i < 3; i++)
            {
                footTbl.AddCell(FCell()); footTbl.AddCell(FCell());
                footTbl.AddCell(FCell()); footTbl.AddCell(FCell());
                footTbl.AddCell(FCell()); footTbl.AddCell(FCell());
            }
            doc.Add(footTbl);
        }

        // GET: api/Child/agent-search?query=2025-123  OR  query=1234567890123
        [HttpGet("agent-search")]
        public ActionResult<object> AgentSearch([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { IsSuccess = false, Message = "Search query is required." });

            query = query.Trim();
            Child child = null;

            // Try MR number format: YYYY-ID
            var parts = query.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int childId))
            {
                child = _db.Childs
                    .Where(c => c.Type == "Travel" && c.Id == childId)
                    .FirstOrDefault();
            }

            // Fallback: search by CNIC / passport
            if (child == null)
            {
                child = _db.Childs
                    .Where(c => c.Type == "Travel" && c.CNIC == query)
                    .FirstOrDefault();
            }

            if (child == null)
                return NotFound(new { IsSuccess = false, Message = "No travel patient found with the given MR number or CNIC/Passport." });

            var year = DateTime.UtcNow.AddHours(5).Year;
            return Ok(new
            {
                IsSuccess = true,
                ResponseData = new
                {
                    child.Id,
                    child.Name,
                    child.FatherName,
                    child.CNIC,
                    MrNo = $"{year}-{child.Id}",
                    VerificationUrl = $"https://myapi.vaccinationcentre.com/api/Child/Travel-PDF-Download/{child.Id}"
                }
            });
        }

        // GET: api/Child/{id}/agent-travel-pdf  — same travel PDF with diagonal VERIFICATION COPY watermark
        [HttpGet("{id}/agent-travel-pdf")]
        public IActionResult AgentTravelPdf(int id)
        {
            var child = _db.Childs.Where(c => c.Id == id).FirstOrDefault();
            if (child == null)
                return NotFound("Patient not found.");

            var sourceStream = CreateTravelPdf(id);
            if (sourceStream == null)
                return NotFound("Could not generate certificate.");

            var sourceBytes = sourceStream.ToArray();

            using (var reader = new PdfReader(sourceBytes))
            using (var outputMs = new MemoryStream())
            {
                var stamper = new PdfStamper(reader, outputMs);
                var bf = BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);

                for (int p = 1; p <= reader.NumberOfPages; p++)
                {
                    var pageSize = reader.GetPageSizeWithRotation(p);
                    float cx = pageSize.Width / 2f;
                    float cy = pageSize.Height / 2f;

                    // Font size to span ~2/3 of page diagonally
                    float fontSize = pageSize.Width * 0.09f;

                    var over = stamper.GetOverContent(p);
                    over.SaveState();

                    var gs = new PdfGState();
                    gs.FillOpacity = 0.18f;
                    gs.StrokeOpacity = 0.18f;
                    over.SetGState(gs);

                    over.BeginText();
                    over.SetFontAndSize(bf, fontSize);
                    over.SetColorFill(new BaseColor(180, 0, 0));
                    over.ShowTextAligned(Element.ALIGN_CENTER, "VERIFICATION COPY", cx, cy, 45);
                    over.EndText();

                    over.RestoreState();
                }

                stamper.Close();

                var fileName = child.Name.Replace(" ", "_") + "_Verification_Copy_" +
                               DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
                return File(outputMs.ToArray(), "application/pdf", fileName);
            }
        }
    }
}