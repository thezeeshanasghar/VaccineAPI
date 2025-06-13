using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using System.Text;
using AutoMapper;
using VaccineAPI.ModelDTO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Globalization;
using CsvHelper;
using static VaccineAPI.Controllers.ChildController;


namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FollowUpController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public FollowUpController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<FollowUpDTO>>> GetAll()
        {
            var list = await _db.FollowUps.OrderBy(x => x.Id).ToListAsync();
            List<FollowUpDTO> listDTO = _mapper.Map<List<FollowUpDTO>>(list);

            return new Response<List<FollowUpDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<FollowUp>> GetSingle(long id)
        {
            var single = await _db.FollowUps.FindAsync(id);
            if (single == null)
                return new Response<FollowUp>(false, "Not Found", null);
            return new Response<FollowUp>(true, null, single);
        }

        [HttpGet("doctor/{doctorId}")]
        public Response<IEnumerable<FollowUpDTO>> GetFollowUpsByDoctor(long doctorId, DateTime inputDate)
        {
            try{
                var followUps = _db
                    .FollowUps.Include(f => f.Child) 
                    .ThenInclude(c => c.User) 
                    .Where(f => f.DoctorId == doctorId)
                    .Where(f =>f.NextVisitDate.HasValue && f.NextVisitDate.Value.Date == inputDate.Date) // Handle nullable DateTime
                    .Where(f => f.Child.IsInactive == false).ToList();

                var groupedFollowUps = followUps
                    .GroupBy(f => f.Child.Id) 
                    .Select(g => g.First()) 
                    .OrderBy(f => f.Child.Id) .ToList();

                IEnumerable<FollowUpDTO> followUpDTOs = _mapper.Map<IEnumerable<FollowUpDTO>>(groupedFollowUps);

                return new Response<IEnumerable<FollowUpDTO>>(true, null, followUpDTOs);
            }
            catch (Exception ex)
            {
                return new Response<IEnumerable<FollowUpDTO>>(false,$"An error occurred: {ex.Message}",null);
            }
        }

        [HttpGet("alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<FollowUpDTO>> GetAlert(DateTime inputDate, int GapDays, long OnlineClinicId)
        {
            {
                var doctor = _db.Clinics.Where(x => x.Id == OnlineClinicId).Include(x => x.Doctor).First<Clinic>().Doctor;
                long[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<long>();

                //  int[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<int>();

                IEnumerable<FollowUp> followups = new List<FollowUp>();
                DateTime AddedDateTime = DateTime.UtcNow.AddHours(5).AddDays(GapDays);
                DateTime pakistanTime = DateTime.UtcNow.AddHours(5);
                if (GapDays == 0)
                    followups = _db.FollowUps.Include(x => x.Child).ThenInclude(x => x.User)
                        .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                       //     .Where(c => System.Data.Entity.DbFunctions.TruncateTime(c.NextVisitDate) == System.Data.Entity.DbFunctions.TruncateTime(pakistanTime))
                       .Where(c => c.NextVisitDate == inputDate.Date)
                        .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate).ToList<FollowUp>();
                else if (GapDays > 0)
                {
                    AddedDateTime = AddedDateTime.AddDays(1);
                    followups = _db.FollowUps.Include(x => x.Child).ThenInclude(x => x.User)

                        .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                        .Where(c => c.NextVisitDate > pakistanTime && c.NextVisitDate <= AddedDateTime)
                        .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate)
                        .ToList<FollowUp>();

                }
                else if (GapDays < 0)
                {
                    followups = _db.FollowUps.Include(x => x.Child).ThenInclude(x => x.User)
                        //    .Where(c => ClinicIDs.Contains(c.Child.ClinicID))
                        .Where(c => c.NextVisitDate < pakistanTime.Date && c.NextVisitDate >= AddedDateTime)
                        .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate)
                        .ToList<FollowUp>();
                }

                IEnumerable<FollowUpDTO> followUpDTO = _mapper.Map<IEnumerable<FollowUpDTO>>(followups);
                return new Response<IEnumerable<FollowUpDTO>>(true, null, followUpDTO);
            }
        }

        [HttpGet("followupmail/{ChildId}")]
        public Response<object> SendAlertEmail(long ChildId)
        {
            try
            {
                // Get child with clinic information
                var child = _db.Childs
                    .Include(c => c.Clinic)
                        .ThenInclude(c => c.Doctor)
                    .FirstOrDefault(c => c.Id == ChildId);

                if (child == null || string.IsNullOrEmpty(child.Email))
                {
                    return new Response<object>(false, "Child not found or email is missing.", null);
                }

                // Get today's schedules - removed Include(s => s.Disease) since Disease is not a navigation property
                var specificDate = DateTime.Today;
                var nextSchedule = _db.FollowUps
                    .Where(s => s.ChildId == ChildId && s.NextVisitDate >= specificDate)
                    .OrderBy(s => s.NextVisitDate)
                    .FirstOrDefault();

                if (nextSchedule == null)
                {
                    return new Response<object>(false, "No FOLLOW UP found.", null);
                }

                // Create a more professional HTML email template
                string emailBody = $@"Dear Parent/Guardian of {child.Name},

                 Your follow-up visit is scheduled as follows:

                 Appointment Date: {nextSchedule.NextVisitDate:dd-MM-yyyy}
                 Reason: {nextSchedule.Disease}
                 Clinic: {child.Clinic.Name}
                 Doctor: {child.Clinic.Doctor.DisplayName}

                 Please confirm your appointment by contacting us at {child.Clinic.PhoneNumber}.

                 You can view your complete vaccination record at: https://vaccinationcentre.com

                 Thanks,
                 {child.Clinic.Name}

                 Note: This is an automated reminder. Please do not reply to this email.";


                UserEmail.SendEmail(child.Email, emailBody, "Follow-up Reminder");

                return new Response<object>(true, "Email sent successfully.", new
                {
                    ChildId = child.Id,
                    Email = child.Email,
                    ScheduleDate = nextSchedule.NextVisitDate,
                    Disease = nextSchedule.Disease,
                    ClinicName = child.Clinic.Name
                });
            }
            catch (Exception ex)
            {
                // Log the error details here
                return new Response<object>(false, $"Error sending email: {ex.Message}", null);
            }
        }

        [HttpGet("followup-alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ChildDTO>> GetFollowUpAlert(int GapDays, long OnlineClinicId)
        {
            try
            {
                // Fetch follow-up schedules based on GapDays and OnlineClinicId
                List<FollowUp> followUps = GetFollowUpData(GapDays, OnlineClinicId, _db);

                if (followUps == null || !followUps.Any())
                {
                    return new Response<IEnumerable<ChildDTO>>(false, "No follow-ups found.", null);
                }

                // Map follow-ups to ChildDTO
                IEnumerable<ChildDTO> childInfoDTOs = followUps
                    .Where(f => f.Child != null) // Ensure Child is not null
                    .Select(f => new ChildDTO
                    {
                        Id = f.Child.Id,
                        Name = f.Child.Name,
                        Email = f.Child.Email,
                        ClinicId = f.Child.ClinicId,
                    });

                foreach (var child in childInfoDTOs)
                {
                    if (string.IsNullOrEmpty(child.Email))
                    {
                        continue; // Skip if no email is available
                    }

                    var ChildId = child.Id;
                    var ClinicId = child.ClinicId;

                    // Fetch clinic and doctor details
                    var clinic = _db
                        .Clinics.Include(x => x.Doctor)
                        .FirstOrDefault(x => x.Id == ClinicId);

                    if (clinic == null || clinic.Doctor == null)
                    {
                        Console.WriteLine($"Clinic or Doctor not found for ClinicId: {ClinicId}");
                        continue;
                    }

                    // Fetch follow-ups for the child
                    var dbFollowUps = followUps
                        .Where(f => f.ChildId == ChildId)
                        .OrderBy(f => f.NextVisitDate)
                        .ToList();

                    string body;
                    if (dbFollowUps.Any())
                    {
                        // Generate email content for follow-up alerts
                        var followUpDetails = dbFollowUps
                            .Select(f => $" {f.Disease} on {f.NextVisitDate:dd-MM-yyyy}")
                            .ToList();

                        body =
                            $@"Dear Parent/Guardian of {child.Name},

Your follow-up visit is scheduled as follows:

Appointment Date: {dbFollowUps.First().NextVisitDate:dd-MM-yyyy}
Reason: {dbFollowUps.First().Disease}
Clinic: {clinic.Name}
Doctor: {clinic.Doctor.DisplayName}

Please confirm your appointment by contacting us at {clinic.PhoneNumber}.

You can view your complete vaccination record at: https://vaccinationcentre.com

Thanks,
{clinic.Name}

Note: This is an automated reminder. Please do not reply to this email.";
                    }
                    else
                    {
                        body = "No upcoming follow-up schedules found.";
                    }

                    // Send email
                    try
                    {
                        UserEmail.SendEmail(child.Email, body);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error sending email: " + ex.Message);
                    }
                }

                return new Response<IEnumerable<ChildDTO>>(true, null, childInfoDTOs);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in GetFollowUpAlert: " + ex.Message);
                return new Response<IEnumerable<ChildDTO>>(
                    false,
                    "An error occurred while processing the request.",
                    null
                );
            }
        }

        private static List<FollowUp> GetFollowUpData(int GapDays, long OnlineClinicId, Context db)
        {
            try
            {
                var doctor = db
                    .Clinics.Where(x => x.Id == OnlineClinicId)
                    .Include(x => x.Doctor)
                    .FirstOrDefault()
                    ?.Doctor;

                if (doctor == null)
                {
                    Console.WriteLine($"Doctor not found for OnlineClinicId: {OnlineClinicId}");
                    return new List<FollowUp>();
                }

                var clinics = db.Clinics.Where(x => x.DoctorId == doctor.Id).ToList();

                if (clinics == null || !clinics.Any())
                {
                    Console.WriteLine($"No clinics found for DoctorId: {doctor.Id}");
                    return new List<FollowUp>();
                }

                long[] ClinicIDs = clinics.Select(x => x.Id).ToArray();

                DateTime CurrentPakDateTime = DateTime.UtcNow.AddHours(5);
                DateTime AddedDateTime = CurrentPakDateTime.AddDays(GapDays);

                List<FollowUp> followUps = new List<FollowUp>();

                if (GapDays == 0)
                {
                    followUps = db
                        .FollowUps.Include(f => f.Child)
                        .ThenInclude(c => c.Clinic)
                        .Where(f => ClinicIDs.Contains(f.Child.ClinicId))
                        .Where(f =>
                            f.NextVisitDate.HasValue
                            && f.NextVisitDate.Value.Date == CurrentPakDateTime.Date
                        )
                        .OrderBy(f => f.Child.Id)
                        .ThenBy(f => f.NextVisitDate)
                        .ToList();
                }
                else
                {
                    followUps = db
                        .FollowUps.Include(f => f.Child)
                        .ThenInclude(c => c.Clinic)
                        .Where(f => ClinicIDs.Contains(f.Child.ClinicId))
                        .Where(f =>
                            f.NextVisitDate.HasValue
                            && f.NextVisitDate.Value.Date > CurrentPakDateTime.Date
                            && f.NextVisitDate.Value.Date <= AddedDateTime.Date
                        )
                        .OrderBy(f => f.Child.Id)
                        .ThenBy(f => f.NextVisitDate)
                        .ToList();
                }

                return followUps;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in GetFollowUpData: " + ex.Message);
                return new List<FollowUp>();
            }
        }

        [HttpGet("export-followups-csv")]
        public IActionResult ExportFollowUpsToCsv([FromQuery(Name = "arr[]")] long[] arr)
        {
            try
            {
                if (arr == null || !arr.Any())
                {
                    return BadRequest("No follow-up IDs provided.");
                }
                List<FollowUp> allFollowUps = new List<FollowUp>();
                foreach (long id in arr)
                {
                    var followUps = _db.FollowUps.Where(f => f.ChildId == id)
                        .Include(f => f.Child)
                        .ThenInclude(c => c.User)
                        .Include(f => f.Child)
                        .ThenInclude(c => c.Clinic)
                        .ToList();
                    allFollowUps.AddRange(followUps);
                }
                if (!allFollowUps.Any())
                {
                    return NotFound("No follow-ups found for the provided IDs.");
                }
                var stream = new MemoryStream();
                using (var writeFile = new StreamWriter(stream, Encoding.UTF8, 512, true))
                {
                    var csv = new CsvWriter(writeFile, CultureInfo.InvariantCulture);
                    var followUpCsvData = allFollowUps.Select(f => new FollowUpCsvDTO
                    {
                        ChildName = f.Child?.Name ?? "N/A",
                        FatherName = f.Child?.FatherName ?? "N/A",
                        DOB = f.Child?.DOB.ToString("yyyy-MM-dd") ?? "N/A",
                        ClinicName = f.Child?.Clinic?.Name ?? "N/A",
                        CurrentVisitDate = f.CurrentVisitDate?.ToString("yyyy-MM-dd") ?? "N/A",
                        NextVisitDate = f.NextVisitDate?.ToString("yyyy-MM-dd") ?? "N/A",
                        Disease = f.Disease ?? "N/A",
                        Weight = f.Weight?.ToString("F2") ?? "N/A",
                        Height = f.Height?.ToString("F2") ?? "N/A",
                        OFC = f.OFC?.ToString("F2") ?? "N/A",
                        Phone = f.Child?.User?.MobileNumber ?? "N/A",
                        Email = f.Child?.Email ?? "N/A",
                    });
                    csv.WriteRecords(followUpCsvData);
                }
                stream.Position = 0; 
                return File(stream, "application/octet-stream", "FollowUps.csv");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error exporting follow-ups to CSV: " + ex.Message);
                return StatusCode(500, "An error occurred while generating the CSV file.");
            }
        }

        [HttpGet("sms-alert/{childId}")]
        public Response<FollowUpDTO> SendSMSAlertToOneChild(int childId)
        {
            {
                var dbChildFollowup = _db.FollowUps.Where(x => x.ChildId == childId).OrderByDescending(x => x.Id).FirstOrDefault();
                UserSMS u = new UserSMS(_db);
                u.ParentFollowUpSMSAlert(dbChildFollowup);
                FollowUpDTO followupDTO = _mapper.Map<FollowUpDTO>(dbChildFollowup);
                return new Response<FollowUpDTO>(true, null, followupDTO);
            }
        }

        [HttpPost]
        public Response<FollowUpDTO> Post(FollowUpDTO FollowUpDto)
        {
            {
                FollowUp dbFollowUp = _mapper.Map<FollowUp>(FollowUpDto);
                _db.FollowUps.Add(dbFollowUp);
                _db.SaveChanges();
                return new Response<FollowUpDTO>(true, null, FollowUpDto);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, FollowUp FollowUp)
        {
            if (id != FollowUp.Id)
                return BadRequest();
            _db.Entry(FollowUp).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.FollowUps.FindAsync(id);
            if (obj == null)
                return NotFound();
            _db.FollowUps.Remove(obj);
            await _db.SaveChangesAsync();
            return Ok(new { Message = "Follow-up deleted successfully." });
        }

        [HttpGet("Follow-Up-PDF")]
        public IActionResult GenerateFollowUpPdf(int childId)
        {
            var followUpDetailsList = _db.FollowUps
                .Where(f => f.ChildId == childId)
                .OrderBy(f => f.CurrentVisitDate)
                .ToList();

            if (followUpDetailsList.Count == 0)
            {
                return NotFound("Follow-up details not found for the child");
            }

            var childDetails = _db.Childs
                .Include(c => c.Clinic)
                .ThenInclude(clinic => clinic.Doctor)
                .FirstOrDefault(c => c.Id == childId);

            if (childDetails == null)
            {
                return NotFound("Child not found");
            }
            string clinicName = childDetails.Clinic.Name;
            string doctorDisplayName = $"{childDetails.Clinic.Doctor.DisplayName}";
            string doctorQualification = childDetails.Clinic.Doctor.AdditionalInfo;
            string clinicPhone = $"Phone : {childDetails.Clinic.PhoneNumber}";
            string clinicAddress = childDetails.Clinic.Address;
            string childName = childDetails.Name;
            string fatherName = $"S/D/W/o : {childDetails.FatherName}";
            string DOB = $"DOB : {childDetails.DOB.ToString("dd-MMM-yyyy")}";

            var output = new MemoryStream();
            var document = new Document();
            PdfWriter writer = PdfWriter.GetInstance(document, output);
            // Add the footer event
            PDFFooter footer = new PDFFooter(childDetails);
            writer.PageEvent = footer;

            document.Open();
            var headerTable = new PdfPTable(2);
            headerTable.WidthPercentage = 100;
            headerTable.SetWidths(new float[] { 3, 1 });

            PdfPCell headerCell = new PdfPCell();
            headerCell.Border = PdfPCell.NO_BORDER;
            headerCell.AddElement(new Paragraph(doctorDisplayName, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12)));
            headerCell.AddElement(new Paragraph(doctorQualification, FontFactory.GetFont(FontFactory.HELVETICA, 10)) { MultipliedLeading = 1.0f, SpacingBefore = 2 });
            headerCell.AddElement(new Paragraph(clinicName, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12)));
            headerCell.AddElement(new Paragraph(clinicAddress, FontFactory.GetFont(FontFactory.HELVETICA, 10)));
            headerCell.AddElement(new Paragraph(clinicPhone, FontFactory.GetFont(FontFactory.HELVETICA, 10)));
            headerTable.AddCell(headerCell);

            PdfPCell childDetailsCell = new PdfPCell();
            string logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Images", "ClinicTeeka.png");
            if (System.IO.File.Exists(logoPath))
            {
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleAbsolute(150f, 150f);
                childDetailsCell.AddElement(logo);
            }
            else
            {
                childDetailsCell.AddElement(new Paragraph("Logo not found", FontFactory.GetFont(FontFactory.HELVETICA, 10)));
            }
            childDetailsCell.Border = PdfPCell.NO_BORDER;
            childDetailsCell.AddElement(new Paragraph($"   {childName}", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)));
            childDetailsCell.AddElement(new Paragraph($"   {fatherName}", FontFactory.GetFont(FontFactory.HELVETICA, 10)) { MultipliedLeading = 1.0f, SpacingBefore = 2 });
            childDetailsCell.AddElement(new Paragraph($"   {DOB}", FontFactory.GetFont(FontFactory.HELVETICA, 10)));

            headerTable.AddCell(childDetailsCell);
            document.Add(headerTable);

            var heading = new Paragraph("VISIT HISTORY", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14))
            {
                Alignment = Element.ALIGN_CENTER
            };
            document.Add(heading);

            var followUpHeaderTable = new PdfPTable(7);
            followUpHeaderTable.HorizontalAlignment = 0;
            followUpHeaderTable.TotalWidth = 510f;
            followUpHeaderTable.LockedWidth = true;
            followUpHeaderTable.SpacingBefore = 15;
            float[] widths = new float[] { 0.5f, 1.5f, 1.5f, 1f, 1f, 1f, 1f };
            followUpHeaderTable.SetWidths(widths);

            followUpHeaderTable.AddCell(CreateCell("Sr No", "backgroudLightGray", 1, "center", "scheduleRecords"));
            followUpHeaderTable.AddCell(CreateCell("VISIT DATE", "backgroudLightGray", 1, "center", "scheduleRecords"));
            followUpHeaderTable.AddCell(CreateCell("Disease", "backgroudLightGray", 1, "center", "scheduleRecords"));
            followUpHeaderTable.AddCell(CreateCell("Weight", "backgroudLightGray", 1, "center", "scheduleRecords"));
            followUpHeaderTable.AddCell(CreateCell("Height", "backgroudLightGray", 1, "center", "scheduleRecords"));
            followUpHeaderTable.AddCell(CreateCell("OFC", "backgroudLightGray", 1, "center", "scheduleRecords"));
            followUpHeaderTable.AddCell(CreateCell("Growth Velocity", "backgroudLightGray", 1, "center", "scheduleRecords"));

            // Loop through follow-up details and add them to the table
            int srNo = 1; // Initialize serial number
            float? previousHeight = null; // To store the height of the previous record
            DateTime? previousVisitDate = null; // To store the visit date of the previous record

            foreach (var followUpDetails in followUpDetailsList)
            {
                DateTime currentVisitDate = followUpDetails.CurrentVisitDate ?? DateTime.MinValue;
                string disease = followUpDetails.Disease;
                float weight = followUpDetails.Weight ?? 0;
                float height = followUpDetails.Height ?? 0;
                float ofc = followUpDetails.OFC ?? 0;

                // Add current record to the table
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(srNo.ToString(), FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(currentVisitDate.ToString("dd-MM-yyyy"), FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(disease, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{weight} kg", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{height} cm", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{ofc} cm", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });

                // Growth Velocity = Height Difference / Time Difference in Years
                if (previousHeight.HasValue && previousVisitDate.HasValue)
                {
                    float heightDifference = height - previousHeight.Value; // Calculate height difference

                    // Add Growth Velocity to the table
                    followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{heightDifference:F2} cm/year", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                    {
                        BackgroundColor = BaseColor.White,
                        BorderColor = BaseColor.LightGray,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }
                else
                {
                    // If it's the first record, just add N/A for growth velocity
                    followUpHeaderTable.AddCell(new PdfPCell(new Phrase("N/A", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                    {
                        BackgroundColor = BaseColor.White,
                        BorderColor = BaseColor.LightGray,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                // Update previous record values for the next iteration
                previousHeight = height;
                previousVisitDate = currentVisitDate;

                srNo++; // Increment serial number
            }

            document.Add(followUpHeaderTable);

            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            return File(output, "application/pdf", "Follow-Up.pdf");
        }

        protected PdfPCell CreateCell(string value, string color, int colpan, string alignment, string table)
        {
            Font font = FontFactory.GetFont(FontFactory.HELVETICA, 11);
            if (color == "bold" || color == "backgroudLightGray")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11);
            }

            if (table == "inwordamount")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA, 11, Font.ITALIC);
            }

            PdfPCell cell = new PdfPCell(new Phrase(value, font));
            cell.BorderColor = GrayColor.LightGray;

            if (color == "backgroudLightGray")
            {
                cell.BackgroundColor = new BaseColor(224, 218, 218);
                cell.FixedHeight = 20f;
            }

            switch (alignment)
            {
                case "right":
                    cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                    break;
                case "left":
                    cell.HorizontalAlignment = Element.ALIGN_LEFT;
                    break;
                case "center":
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    break;
            }

            cell.Colspan = colpan;

            if (table == "description")
            {
                cell.Border = 0;
                cell.Padding = 2f;
            }
            else if (table == "scheduleRecords")
            {
                cell.FixedHeight = 15f;
            }
            else if (table == "invoiceRecords" || table == "inwordamount")
            {
                cell.FixedHeight = 18f;
            }

            return cell;
        }
    }
}
