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
        private readonly IWebHostEnvironment _host;

        public FollowUpController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _db = context;
            _mapper = mapper;
            _host = host;
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

                foreach (var f in followUps)
                    if (f.Disease == null) f.Disease = "";

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
                // Get child with clinic and user information
                var child = _db.Childs
                    .Include(c => c.Clinic)
                        .ThenInclude(c => c.Doctor)
                    .Include(c => c.User)
                    .FirstOrDefault(c => c.Id == ChildId);

                if (child == null || string.IsNullOrEmpty(child.Email))
                {
                    return new Response<object>(false, "Child not found or email is missing.", null);
                }

                var specificDate = DateTime.Today;
                var nextSchedule = _db.FollowUps
                    .Where(s => s.ChildId == ChildId && s.NextVisitDate >= specificDate)
                    .OrderBy(s => s.NextVisitDate)
                    .FirstOrDefault();

                if (nextSchedule == null)
                {
                    return new Response<object>(false, "No FOLLOW UP found.", null);
                }

                // Prepare user info
                string mobile = child.User?.MobileNumber ?? "N/A";
                string password = child.User?.Password ?? "N/A";
                string siteUrl = "https://client.vaccinationcentre.com";

                // Create a more professional HTML email template
                string emailBody = $@"Dear Parent/Guardian of {child.Name},

                Your follow-up visit is scheduled as follows:

                Appointment Date: {nextSchedule.NextVisitDate:dd-MM-yyyy}
                Reason: {nextSchedule.Disease}
                Clinic: {child.Clinic.Name}
                Doctor: {child.Clinic.Doctor.DisplayName}

                Please confirm your appointment by contacting us at {child.Clinic.PhoneNumber}.

                You can view your complete vaccination record at: {siteUrl}
                Mobile Number: {mobile}
                Password: {password}

                Thanks,
                {child.Clinic.Name}

                Note: This is an automated reminder. Please do not reply to this email.";

                UserEmail.SendEmail(child.Email, emailBody, "Follow-up Reminder");

                return new Response<object>(true, "Email sent successfully.", new
                {
                    ChildId = child.Id,
                    Email = child.Email,
                    MobileNumber = mobile,
                    Password = password,
                    ScheduleDate = nextSchedule.NextVisitDate,
                    Disease = nextSchedule.Disease,
                    ClinicName = child.Clinic.Name,
                    SiteUrl = siteUrl
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

                // Map follow-ups to ChildDTO and include mobile/password
                var childIds = followUps.Where(f => f.Child != null).Select(f => f.Child.Id).Distinct().ToList();
                var children = _db.Childs
                    .Include(c => c.User)
                    .Where(c => childIds.Contains(c.Id))
                    .ToList();

                IEnumerable<ChildDTO> childInfoDTOs = children.Select(c => new ChildDTO
                {
                    Id = c.Id,
                    Name = c.Name,
                    Email = c.Email,
                    ClinicId = c.ClinicId,
                    MobileNumber = c.User?.MobileNumber ?? "",
                    Password = c.User?.Password ?? ""
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

                    string mobile = child.MobileNumber ?? "N/A";
                    string password = child.Password ?? "N/A";
                    string siteUrl = "https://vaccinationcentre.com";

                    string body;
                    if (dbFollowUps.Any())
                    {
                        body =
                                $@"Dear Parent/Guardian of {child.Name},

                                Your follow-up visit is scheduled as follows:

                                Appointment Date: {dbFollowUps.First().NextVisitDate:dd-MM-yyyy}
                                Reason: {dbFollowUps.First().Disease}
                                Clinic: {clinic.Name}
                                Doctor: {clinic.Doctor.DisplayName}

                                Please confirm your appointment by contacting us at {clinic.PhoneNumber}.

                                You can view your complete vaccination record at: {siteUrl}
                                Mobile Number: {mobile}
                                Password: {password}

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
                if (dbChildFollowup == null)
                {
                    return new Response<FollowUpDTO>(false, "No follow-up found for child.", null);
                }
                UserSMS u = new UserSMS(_db);
                u.ParentFollowUpSMSAlert(dbChildFollowup);
                FollowUpDTO followupDTO = _mapper.Map<FollowUpDTO>(dbChildFollowup);
                return new Response<FollowUpDTO>(true, null, followupDTO);
            }
        }

        [HttpPost]
        public Response<FollowUpDTO> Post(FollowUpDTO FollowUpDto)
        {
            // Auto-vaccine follow-up rows (Disease == "Vaccination", created by autoCreateFollowUp /
            // autoCreateFollowUpForBulk in VacDoc) are upserted: at most one row per (ChildId, CurrentVisitDate.Date)
            // is kept for "Vaccination" visits, so repeated fills/after-fills on the same day update the same row
            // instead of creating duplicates.
            //
            // NOTE: this match relies on Disease == "Vaccination" being exclusive to auto-generated rows.
            // The manual "Add Follow-up" flow (addfollowup.page.ts) can theoretically also send
            // Disease == "Vaccination" via its dormant loadVaccineDefaults() (?auto=1) path, but that
            // path is currently unreachable (no UI passes ?auto=1). If that ever changes, a dedicated
            // IsAutoGenerated flag should be added to disambiguate.
            bool isAutoVaccine = string.Equals(FollowUpDto.Disease, "Vaccination", StringComparison.Ordinal);

            if (isAutoVaccine)
            {
                var visitDate = FollowUpDto.CurrentVisitDate.Date;
                FollowUp? existing = _db.FollowUps
                    .Where(f => f.ChildId == FollowUpDto.ChildId
                             && f.CurrentVisitDate.HasValue
                             && f.CurrentVisitDate.Value.Date == visitDate
                             && f.Disease == "Vaccination")
                    .OrderByDescending(f => f.Id)
                    .FirstOrDefault();

                if (existing != null)
                {
                    // NextVisitDate is always null for auto-vaccine rows: vaccine reminders are
                    // driven by the Schedule table, not by FollowUp.NextVisitDate.
                    existing.NextVisitDate = null;

                    // Only overwrite vitals when the incoming payload actually provided a value,
                    // so a later partial call doesn't blank out previously-recorded vitals.
                    if (FollowUpDto.Weight.HasValue) existing.Weight = FollowUpDto.Weight;
                    if (FollowUpDto.Height.HasValue) existing.Height = FollowUpDto.Height;
                    if (FollowUpDto.OFC.HasValue) existing.OFC = FollowUpDto.OFC;

                    existing.DoctorId = FollowUpDto.DoctorId;

                    _db.SaveChanges();

                    FollowUpDTO updatedDto = _mapper.Map<FollowUpDTO>(existing);
                    return new Response<FollowUpDTO>(true, "updated", updatedDto);
                }

                FollowUpDto.NextVisitDate = null;
            }

            FollowUp dbFollowUp = _mapper.Map<FollowUp>(FollowUpDto);
            _db.FollowUps.Add(dbFollowUp);
            _db.SaveChanges();
            return new Response<FollowUpDTO>(true, null, FollowUpDto);
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
            string logoPath = childDetails.Clinic?.MonogramImage != null
                ? Path.Combine(_host.ContentRootPath, childDetails.Clinic.MonogramImage)
                : null;
            if (logoPath != null && System.IO.File.Exists(logoPath))
            {
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleAbsolute(150f, 150f);
                childDetailsCell.AddElement(logo);
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
            followUpHeaderTable.AddCell(CreateCell("Ht Velocity (cm/yr)", "backgroudLightGray", 1, "center", "scheduleRecords"));

            // Loop through follow-up details and add them to the table
            int srNo = 1; // Initialize serial number
            float? lastValidHeight = null; // Last visit's height where height was actually documented
            DateTime? lastValidHeightDate = null; // Visit date corresponding to lastValidHeight

            foreach (var followUpDetails in followUpDetailsList)
            {
                DateTime currentVisitDate = followUpDetails.CurrentVisitDate ?? DateTime.MinValue;
                string disease = followUpDetails.Disease ?? "";
                string weightStr = (followUpDetails.Weight.HasValue && followUpDetails.Weight.Value > 0) ? $"{followUpDetails.Weight.Value} kg" : "-";
                string heightStr = (followUpDetails.Height.HasValue && followUpDetails.Height.Value > 0) ? $"{followUpDetails.Height.Value} cm" : "-";
                string ofcStr    = (followUpDetails.OFC.HasValue    && followUpDetails.OFC.Value    > 0) ? $"{followUpDetails.OFC.Value} cm"    : "-";
                bool hasHeight = followUpDetails.Height.HasValue && followUpDetails.Height.Value > 0;

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
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(weightStr, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(heightStr, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(ofcStr, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });

                // Growth Velocity = (Height difference) / (Time difference in years), comparing
                // against the most recent prior visit where height was actually documented.
                string velocityStr = "-";
                if (hasHeight && lastValidHeight.HasValue && lastValidHeightDate.HasValue)
                {
                    double years = (currentVisitDate - lastValidHeightDate.Value).TotalDays / 365.25;
                    if (years >= (1.0 / 365.25))
                    {
                        float velocity = (followUpDetails.Height.Value - lastValidHeight.Value) / (float)years;
                        velocityStr = $"{velocity:F2} cm/yr";
                    }
                }

                followUpHeaderTable.AddCell(new PdfPCell(new Phrase(velocityStr, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    BackgroundColor = BaseColor.White,
                    BorderColor = BaseColor.LightGray,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });

                // Only update the "last valid" reference when this visit actually documented height
                if (hasHeight)
                {
                    lastValidHeight = followUpDetails.Height.Value;
                    lastValidHeightDate = currentVisitDate;
                }

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
