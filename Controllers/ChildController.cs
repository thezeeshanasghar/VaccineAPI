using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutoMapper;
using CsvHelper;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
//using WebApi.OutputCache.V2;

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
            var dbChilds = _db.Childs.Include(x => x.User).Where(x => x.ClinicId == id).OrderByDescending(x => x.Id).Skip(10 * page).Take(10).ToList();
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
            ChildDTO childDTO = _mapper.Map<ChildDTO>(dbChild);
            childDTO.CountryCode = dbChild.User.CountryCode;
            childDTO.MobileNumber = dbChild.User.MobileNumber;
            return new Response<ChildDTO>(true, null, childDTO);
        }

        [HttpGet("{id}/schedule")]
        public Response<IEnumerable<ScheduleDTO>> GetChildSchedule(int id)
        {
            {
                var child = _db.Childs.Include(x => x.Schedules).Include(x => x.User).FirstOrDefault(c => c.Id == id);
                if (child == null)
                    return new Response<IEnumerable<ScheduleDTO>>(false, "Child not found", null);
                else
                {
                    var dbSchedules = child.Schedules.OrderBy(x => x.Date).ToList();
                    for (int i = 0; i < dbSchedules.Count; i++)
                    {
                        var dbSchedule = dbSchedules.ElementAt(i);
                        dbSchedule.Dose = _db.Schedules.Include("Dose").Where<Schedule>(x => x.Id == dbSchedule.Id).FirstOrDefault().Dose;
                        dbSchedule.Brand = _db.Brands.Where<Brand>(x => x.Id == dbSchedule.BrandId).FirstOrDefault();

                    }

                    var schedulesDTO = _mapper.Map<List<ScheduleDTO>>(dbSchedules);
                    //foreach (var scheduleDTO in schedulesDTO)
                    //    scheduleDTO.Dose = Mapper.Map<DoseDTO>(entities.Schedules.Include("Dose").Where<Schedule>(x => x.ID == scheduleDTO.ID).FirstOrDefault<Schedule>().Dose);
                    return new Response<IEnumerable<ScheduleDTO>>(true, null, schedulesDTO);
                }
            }
        }

        // csv file start
        [HttpGet("{id}/downloadcsv")]
        public IActionResult MyExportAction(int id)
        {
            var schedule = _db.Schedules.Where(x => x.ChildId == id).Include(x => x.Dose).ThenInclude(x => x.Vaccine).ToList();
            DateTime nextvisitDate = getNextDate(schedule);
            var progresses = _db.Childs.Include(x => x.User).Where(x => x.Id == id).ToList()
                .Select(progress =>
                   new ChildCsvDTO()
                   {
                       Name = progress.Name,
                       FatherName = progress.FatherName,
                       DOB = progress.DOB.ToShortDateString(),
                       City = progress.City,
                       Next_Due_Date = nextvisitDate.ToString("yyyy/MM/dd"),
                       Next_Due_Vaccines = getNextVaccine(schedule, nextvisitDate),
                       Phone = progress.User.MobileNumber,
                       Email = progress.Email
                   }
                );

            // List<ChildCsvDTO> reportCSVModels = childModel.ToList();

            var stream = new MemoryStream();
            using (var writeFile = new StreamWriter(stream, Encoding.UTF8, 512, true))
            {
                var csv = new CsvWriter(writeFile, CultureInfo.InvariantCulture);
                //csv.Configuration.RegisterClassMap<GroupReportCSVMap>();            
                csv.WriteRecords(progresses);
                csv.WriteRecords(progresses);
            }
            stream.Position = 0; //reset stream
            return File(stream, "application/octet-stream", "Reports.csv");
        }
        // csv file end

        // csv file start
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
                    var schedule = _db.Schedules.Where(x => x.ChildId == id).Include(x => x.Dose).ThenInclude(x => x.Vaccine).ToList();
                    DateTime nextvisitDate = getNextDate(schedule);
                    var progresses = _db.Childs.Include(x => x.User).Where(x => x.Id == id).ToList()
                        .Select(progress =>
                           new ChildCsvDTO()
                           {
                               Name = progress.Name,
                               FatherName = progress.FatherName,
                               DOB = progress.DOB.ToShortDateString(),
                               City = progress.City,
                               Next_Due_Date = nextvisitDate.ToString("yyyy/MM/dd"),
                               Next_Due_Vaccines = getNextVaccine(schedule, nextvisitDate),
                               Phone = progress.User.MobileNumber,
                               Email = progress.Email
                           }
                        );
                    csv.WriteRecords(progresses);

                }

            }
            stream.Position = 0; //reset stream
            return File(stream, "application/octet-stream", "Reports.csv");
        }
        // csv file end

        private DateTime getNextDate(List<Schedule> schedul)
        {
            DateTime Now = DateTime.Now;

            foreach (var sch in schedul)
            {
                // Console.WriteLine (Now);
                //sch.Date = sch.Date.ToString ("yyyy/MM/dd");
                // Console.WriteLine (sch.Date);
                if (sch.Date > Now)
                    return sch.Date;
            }
            return Now;
        }

        private string getNextVaccine(List<Schedule> schedu, DateTime nextDate)
        {
            string nextVaccines = "";
            foreach (var sch in schedu)
            {
                if (sch.Date == nextDate)
                    nextVaccines += (sch.Dose.Name + ",");
            }
            return nextVaccines;
        }

        [HttpGet("{id}/GetChildAgainstMobile")]
        public Response<IEnumerable<ChildDTO>> GetChildAgainstMobile(string id)
        {

            User user = _db.Users.Where(x => x.MobileNumber == id).FirstOrDefault();
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

        //    [HttpGet("{id}/Download-Schedule-PDF")]
        //     public HttpResponseMessage DownloadSchedulePDF(int id)
        //     {
        //         Child dbScheduleChild;
        //         {
        //             dbScheduleChild = _db.Childs.Where(x => x.Id == id).FirstOrDefault();
        //         }
        //         var stream = CreateSchedulePdf(id);

        //         return new HttpResponseMessage
        //         {
        //             Content = new StreamContent(stream)
        //             {
        //                 Headers = {
        //                             ContentType = new MediaTypeHeaderValue("application/pdf"),
        //                             ContentDisposition = new ContentDispositionHeaderValue("attachment") {
        //                                 FileName =dbScheduleChild.Name.Replace(" ","")+"_Schedule_" +DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy")+ ".pdf"
        //                             }
        //                         }
        //             },
        //             StatusCode = HttpStatusCode.OK
        //         };
        //     }

        [HttpGet("{id}/Download-Schedule-PDF")]
        public IActionResult DownloadSchedulePDF(int id)
        {
            Child dbScheduleChild;
            {
                dbScheduleChild = _db.Childs.Where(x => x.Id == id).FirstOrDefault();
            }
            var stream = CreateSchedulePdf(id);
            var FileName = dbScheduleChild.Name.Replace(" ", "") + "_Schedule_" + DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy") + ".pdf";
            return File(stream, "application/pdf", FileName);
        }

        private Stream CreateSchedulePdf(int childId)
        {
            //Access db data
            var dbChild = _db.Childs.Include(x => x.User).Include(x => x.Clinic).ThenInclude(x => x.Doctor).ThenInclude(x => x.User).Where(x => x.Id == childId).FirstOrDefault();
            var dbDoctor = dbChild.Clinic.Doctor;
            var child = _db.Childs.Include(x => x.Schedules).ThenInclude(x => x.Dose).Include(x => x.Schedules).ThenInclude(x => x.Brand).FirstOrDefault(c => c.Id == childId);
            var dbSchedules = child.Schedules.OrderBy(x => x.Date).ToList();
            var scheduleDoses = from schedule in dbSchedules
                                group schedule.Dose by schedule.Date into scheduleDose
                                select new { Date = scheduleDose.Key, Doses = scheduleDose.ToList() };

            int count = 0;
            //
            var document = new Document(PageSize.A4, 60, 60, 30, 30);
            { //new Document (PageSize.A4, 50, 50, 25, 105); {
                var output = new MemoryStream();

                var writer = PdfWriter.GetInstance(document, output);
                writer.CloseStream = false;
                // calling PDFFooter class to Include in document
                writer.PageEvent = new PDFFooter(child);
                document.Open();
                // GetPDFHeading (document, "Immunization Record");

                //Table 1 for description above Schedule table
                PdfPTable upperTable = new PdfPTable(3);
                float[] upperTableWidths = new float[] { 230f, 10f, 230f };
                upperTable.HorizontalAlignment = 0;
                upperTable.TotalWidth = 470f;
                upperTable.LockedWidth = true;
                upperTable.SetWidths(upperTableWidths);
                upperTable.AddCell(CreateCell("DR SALMAN AHMAD BAJWA", "bold", 2, "left", "description"));

                //image code start
                var imgPath = Path.Combine(_host.ContentRootPath, "Resources/Images/cliniclogo.png");

                // if (dbChild.Clinic.MonogramImage != null) {
                // imgPath = Path.Combine (_host.ContentRootPath, dbChild.Clinic.MonogramImage);
                Image img = Image.GetInstance(imgPath);
                img.ScaleAbsolute(160f, 50f);
                //img.ScaleToFit(40f, 40f);

                PdfPCell imageCell = new PdfPCell(img, false);
                // imageCell.PaddingTop = 5;
                imageCell.Colspan = 1; // either 1 if you need to insert one cell
                imageCell.Rowspan = 2;
                imageCell.Border = 0;
                imageCell.FixedHeight = 1f;
                imageCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                upperTable.AddCell(imageCell);
                // } else {
                //     PdfPCell imageCell = new PdfPCell ();
                //     // imageCell.PaddingTop = 5;
                //     imageCell.Colspan = 1; // either 1 if you need to insert one cell
                //     imageCell.Rowspan = 4;
                //     imageCell.Border = 0;
                //     imageCell.FixedHeight = 1f;
                //     imageCell.HorizontalAlignment = Element.ALIGN_CENTER;
                //    // upperTable.AddCell (imageCell);
                // }
                //image code end

                upperTable.AddCell(CreateCell("MBBS, RMP, FCPS (Peads) \nConsultant Paediatrician & Neonatologist\nVaccinology and Immunization Expert", "unbold", 2, "left", "description"));

                upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "bold", 2, "left", "description"));

                // upperTable.AddCell (CreateCell (dbChild.Clinic.Name, "", 1, "left", "description"));
                if (dbChild.Gender == "Girl")
                {
                    upperTable.AddCell(CreateCell(dbChild.Name + "  D/O", "bold", 1, "right", "description"));
                }
                else
                {
                    upperTable.AddCell(CreateCell(dbChild.Name + "  S/O", "bold", 1, "right", "description"));
                }

                upperTable.AddCell(CreateCell(dbChild.Clinic.Address, "unbold", 2, "left", "description"));
                upperTable.AddCell(CreateCell(dbChild.FatherName, "", 1, "right", "description"));

                upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "", 2, "left", "description"));
                upperTable.AddCell(CreateCell("+" + dbChild.User.CountryCode + "-" + dbChild.User.MobileNumber, "", 1, "right", "description"));
                upperTable.AddCell(CreateCell("", "", 2, "left", "description"));
                upperTable.AddCell(CreateCell("DOB: " + dbChild.DOB.ToString("dd MMMM, yyyy"), "", 1, "right", "description"));

                // upperTable.AddCell (CreateCell ("Address: " + dbChild.Clinic.Address, "", 1, "left", "description"));
                //  upperTable.AddCell (CreateCell ("", "", 1, "right", "description"));
                document.Add(upperTable);
                Paragraph title = new Paragraph("IMMUNIZATION RECORD");
                title.Font = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
                title.Alignment = Element.ALIGN_CENTER;

                document.Add(title);
                //  document.Add(new Paragraph(""));
                // document.Add (new Chunk (""));
                //Schedule Table
                float[] widths = new float[] { 20f, 160f, 50f, 70, 70f, 45f, 45f, 35f };

                PdfPTable table = new PdfPTable(8);
                table.HorizontalAlignment = 0;
                table.TotalWidth = 470f;
                table.LockedWidth = true;
                table.SpacingBefore = 5;
                table.SetWidths(widths);
                table.AddCell(CreateCell("Sr", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Vaccine", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Status", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Date", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Brand", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Weight", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Height", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("OFC", "backgroudLightGray", 1, "center", "scheduleRecords"));
                //table.AddCell(CreateCell("Injected", "backgroudLightGray", 1, "center", "scheduleRecords"));

                // for typhoid and flu
                var flu1Date = " ";
                var flu2Date = " ";
                var flu3Date = " ";
                var flu1Brand = " ";
                var flu2Brand = " ";
                var flu3Brand = " ";
                var flu1GivenDate = " ";
                var flu2GivenDate = " ";
                var flu3GivenDate = " ";
                var flustop = false;

                var type1Date = "";
                var type2Date = "";
                var type3Date = "";
                // var type4Date = "";
                var type1Brand = "";
                var type2Brand = "";
                var type3Brand = "";
                var type1GivenDate = "";
                var type2GivenDate = "";
                var type3GivenDate = "";
                var type4GivenDate = "";
                var typestop = false;

                foreach (var dbSchedule in dbSchedules)
                {
                    if (dbSchedule.IsSkip != true && !dbSchedule.Dose.Name.StartsWith("Flu") && !dbSchedule.Dose.Name.StartsWith("Typhoid"))
                    {
                        int doseCount = 0;
                        Paragraph p = new Paragraph();
                        count++;
                        doseCount++;
                        Font font = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                        Font boldfont = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
                        Font italicfont = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.ITALIC);

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
                                PdfPCell statusCell = new PdfPCell(new Phrase("Given", boldfont));
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
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && !checkForMissed(dbSchedule.Date))
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Due", font));
                                statusCell.HorizontalAlignment = Element.ALIGN_LEFT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true && checkForMissed(dbSchedule.Date))
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase(" Missed", italicfont));
                                statusCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                                statusCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(statusCell);
                            }
                            else
                            {
                                PdfPCell statusCell = new PdfPCell(new Phrase("Diseased" + dbSchedule.DiseaseYear, font));
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
                            else
                            {
                                PdfPCell dateCell = new PdfPCell(new Phrase(dbSchedule.Date.Date.ToString("dd/MM/yyyy"), font));
                                dateCell.HorizontalAlignment = Element.ALIGN_CENTER;
                                dateCell.BorderColor = GrayColor.LightGray;
                                table.AddCell(dateCell);
                            }

                            PdfPCell brandCell = new PdfPCell(new Phrase(dbSchedule.Brand != null ? dbSchedule.Brand.Name.ToString() : "", font));
                            brandCell.HorizontalAlignment = Element.ALIGN_LEFT;
                            brandCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(brandCell);

                            PdfPCell weightCell = new PdfPCell(new Phrase(dbSchedule.Weight > 0 ? dbSchedule.Weight.ToString() : "", font));
                            weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            weightCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(weightCell);

                            PdfPCell heightCell = new PdfPCell(new Phrase(dbSchedule.Height > 0 ? dbSchedule.Height.ToString() : "", font));
                            heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            heightCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(heightCell);

                            PdfPCell circleCell = new PdfPCell(new Phrase(dbSchedule.Circle > 0 ? dbSchedule.Circle.ToString() : "", font));
                            circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            circleCell.BorderColor = GrayColor.LightGray;
                            table.AddCell(circleCell);
                        }

                    }

                    // for typhoid and flu

                    if (dbSchedule.Dose.Name.StartsWith("Flu") && dbSchedule.Dose.DoseOrder == 1)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            flu1GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            flu1Brand = dbSchedule.Brand?.Name.ToString();
                        }
                        else
                        {
                            flustop = true;
                            flu1Date = dbSchedule.Date.ToString("dd/MM/yyyy");
                        }

                    }
                    if (dbSchedule.Dose.Name.StartsWith("Flu") && dbSchedule.Dose.DoseOrder == 2 && flustop == false)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            flu2GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            flu2Brand = dbSchedule.Brand?.Name.ToString();
                        }
                        else
                        {
                            flustop = true;
                            flu1Date = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                        }

                    }
                    if (dbSchedule.Dose.Name.StartsWith("Flu") && dbSchedule.Dose.DoseOrder == 3 && flustop == false)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            flu3GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            flu3Brand = dbSchedule.Brand?.Name.ToString();
                        }
                        else
                        {
                            flustop = true;
                            flu2Date = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                        }

                    }

                    //   //typhoid 
                    if (dbSchedule.Dose.Name.StartsWith("Typhoid") && dbSchedule.Dose.DoseOrder == 1)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            type1GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            type1Brand = dbSchedule.Brand?.Name.ToString();
                        }
                        else
                        {
                            typestop = true;
                            type1Date = dbSchedule.Date.ToString("dd/MM/yyyy");
                        }

                    }
                    if (dbSchedule.Dose.Name.StartsWith("Typhoid") && dbSchedule.Dose.DoseOrder == 2 && typestop == false)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            type2GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            type2Brand = dbSchedule.Brand?.Name.ToString();
                        }
                        else
                        {
                            typestop = true;
                            type1Date = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                        }

                    }
                    if (dbSchedule.Dose.Name.StartsWith("Typhoid") && dbSchedule.Dose.DoseOrder == 3 && typestop == false)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            type3GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                            type3Brand = dbSchedule.Brand?.Name.ToString();
                        }
                        else
                        {
                            typestop = true;
                            type2Date = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                        }

                    }

                    if (dbSchedule.Dose.Name.StartsWith("Typhoid") && dbSchedule.Dose.DoseOrder == 4 && typestop == false)
                    {
                        if (dbSchedule.IsDone == true)
                        {
                            type4GivenDate = dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy");
                        }
                        else
                        {
                            typestop = true;
                            type3Date = dbSchedule.Date.Date.ToString("dd/MM/yyyy");
                        }

                    }

                }

                document.Add(table);
                // special vaccines table start
                float[] lowerwidths = new float[] { 250f, 250f };
                PdfPTable lowertable = new PdfPTable(2);
                lowertable.HorizontalAlignment = 0;
                lowertable.TotalWidth = 470f;
                lowertable.LockedWidth = true;
                lowertable.SpacingBefore = 10;
                lowertable.SetWidths(lowerwidths);
                lowertable.AddCell(CreateCell("Typhoid (Every 2-3 years)", "bold", 1, "center", "scheduleRecords"));
                lowertable.AddCell(CreateCell("Flu (Yearly)", "bold", 1, "center", "scheduleRecords"));
                document.Add(lowertable);

                float[] lowerwidths2 = new float[] { 62f, 62f, 62f, 62f, 62f, 62f };
                PdfPTable lowertable2 = new PdfPTable(6);
                lowertable2.HorizontalAlignment = 0;
                lowertable2.TotalWidth = 470f;
                lowertable2.LockedWidth = true;
                lowertable2.SetWidths(lowerwidths2);
                //header
                lowertable2.AddCell(CreateCell("Brand", "backgroudLightGray", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Given On ", "backgroudLightGray", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Next Due", "backgroudLightGray", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("Signature", "backgroudLightGray", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Brand", "backgroudLightGray", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Given On ", "backgroudLightGray", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell("Next Due", "backgroudLightGray", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("Signature", "backgroudLightGray", 1, "center", "scheduleRecords"));

                //boxes
                lowertable2.AddCell(CreateCell(type1Brand, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type1GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type1Date, "", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("", "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu1Brand, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu1GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu1Date, "", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("", "", 1, "center", "scheduleRecords"));

                lowertable2.AddCell(CreateCell(type2Brand, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type2GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type2Date, "", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("", "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu2Brand, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu2GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu2Date, "", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("", "", 1, "center", "scheduleRecords"));

                lowertable2.AddCell(CreateCell(type3Brand, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type3GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(type3Date, "", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("", "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu3Brand, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu3GivenDate, "", 1, "center", "scheduleRecords"));
                lowertable2.AddCell(CreateCell(flu3Date, "", 1, "center", "scheduleRecords"));
                // lowertable2.AddCell (CreateCell ("", "", 1, "center", "scheduleRecords"));

                document.Add(lowertable2);
                //special vaccines table end
                document.Close();

                output.Seek(0, SeekOrigin.Begin);

                return output;
            }
        }

        private bool checkForMissed(DateTime DueDate)
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

                dbChildrenResults = _db.Childs.Where(c => c.Name.ToLower().Contains(keyword.ToLower()) ||
                   c.FatherName.ToLower().Contains(keyword.ToLower())).ToList();
                childDTOs.AddRange(_mapper.Map<List<ChildDTO>>(dbChildrenResults));

                foreach (var item in childDTOs)
                {
                    item.MobileNumber = dbChildrenResults.Where(x => x.Id == item.Id).FirstOrDefault().User.MobileNumber;
                }

                return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
            }
        }

        // for admin app
        [HttpGet("search")]
        public Response<IEnumerable<ChildDTO>> SearchChildrenByCity([FromQuery] string name = "", [FromQuery] string city = "", [FromQuery] string fromdob = "", [FromQuery] string todob = "", [FromQuery] string gender = "", [FromQuery] string vaccineid = "", [FromQuery] string doctorid = "")

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
                dbChildrenResults = dbChildrenResults.Where(c =>
                   c.Name.ToLower().Contains(name.Trim().ToLower()) ||
                   c.FatherName.ToLower().Contains(name.Trim().ToLower())).ToList();

            if (!String.IsNullOrEmpty(city))
                dbChildrenResults = dbChildrenResults.Where(c => c.City != null && c.City.ToLower().Contains(city.Trim().ToLower())).ToList();

            if (!String.IsNullOrEmpty(fromdob) && !String.IsNullOrEmpty(todob))
                dbChildrenResults = dbChildrenResults.Where(c => c.DOB >= Convert.ToDateTime(fromdob).Date && c.DOB <= Convert.ToDateTime(todob).Date).ToList();

            if (!String.IsNullOrEmpty(gender))
                dbChildrenResults = dbChildrenResults.Where(c => c.Gender == gender).ToList();

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
                // check for existing parent 
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
                    // childDB.ChildVaccines.Clear();
                    // foreach(VaccineDTO vaccineDTO in childDTO.ChildVaccines) {
                    //     childDB.ChildVaccines.Add(_db.Vaccines.Where(x=>x.Id==vaccineDTO.Id).FirstOrDefault());
                    // }
                    _db.Childs.Add(childDB);
                    _db.SaveChanges();
                }
                else
                {
                    Child existingChild = _db.Childs.FirstOrDefault(x => x.Name.Equals(childDTO.Name) && x.UserId == user.Id);
                    if (existingChild != null)
                        return new Response<ChildDTO>(false, "Children with same name & number already exists. Parent should login and start change doctor process.", null);
                    childDB.UserId = user.Id;
                    _db.Childs.Add(childDB);
                    _db.SaveChanges();
                }
                childDTO.Id = childDB.Id;
                if (childDTO.Type == "regular")
                {
                    // get doctor schedule and apply it to child and save in Schedule table
                    Clinic clinic = _db.Clinics.Where(x => x.Id == childDTO.ClinicId).Include(x => x.Doctor).FirstOrDefault();
                    Doctor doctor = clinic.Doctor;

                    List<DoctorSchedule> dss = _db.DoctorSchedules.Where(x => x.DoctorId == doctor.Id).ToList();
                    //IEnumerable<DoctorSchedule> dss = doctor.DoctorSchedules;

                    foreach (DoctorSchedule ds in dss)
                    {
                        var dbDose = _db.Doses.Where(x => x.Id == ds.DoseId).Include(x => x.Vaccine).FirstOrDefault();
                        //  if (childDTO.ChildVaccines.Any(x => x.Id == dbDose.Vaccine.Id))
                        {
                            Schedule cvd = new Schedule();
                            cvd.ChildId = childDTO.Id;
                            cvd.DoseId = ds.DoseId;
                            if (childDTO.Gender == "Boy" && ds.Dose.Name.StartsWith("HPV"))
                                continue;

                            if (childDTO.IsSkip == true && ds.IsActive != true) // skip unactive doses
                                continue;

                            if (childDTO.IsEPIDone)
                            {
                                if (ds.Dose.Name.StartsWith("BCG") ||
                                    ds.Dose.Name.StartsWith("HBV") ||
                                    ds.Dose.Name.Equals("OPV # 1"))
                                {
                                    cvd.IsDone = true;
                                    cvd.Due2EPI = true;
                                    cvd.GivenDate = childDB.DOB;
                                }
                                else if (
                                  ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 1", StringComparison.OrdinalIgnoreCase) ||
                                  ds.Dose.Name.Equals("Pneumococcal # 1", StringComparison.OrdinalIgnoreCase) ||
                                  ds.Dose.Name.Equals("Rota Virus GE # 1", StringComparison.OrdinalIgnoreCase)
                              //ds.Dose.Name.Equals ("DTaP 1", StringComparison.OrdinalIgnoreCase)
                              )
                                {
                                    cvd.IsDone = true;
                                    cvd.Due2EPI = true;
                                    DateTime d = childDB.DOB;
                                    cvd.GivenDate = d.AddDays(42);
                                }
                                else if (
                                  ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 2", StringComparison.OrdinalIgnoreCase) ||
                                  ds.Dose.Name.Equals("Pneumococcal # 2", StringComparison.OrdinalIgnoreCase) ||
                                  ds.Dose.Name.Equals("Rota Virus GE # 2", StringComparison.OrdinalIgnoreCase)
                              //ds.Dose.Name.Equals ("DTaP 2", StringComparison.OrdinalIgnoreCase)
                              )
                                {
                                    cvd.IsDone = true;
                                    cvd.Due2EPI = true;
                                    DateTime d = childDB.DOB;
                                    cvd.GivenDate = d.AddDays(70);
                                }
                                else if (
                                  ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 3", StringComparison.OrdinalIgnoreCase) ||
                                  ds.Dose.Name.Equals("Pneumococcal # 3", StringComparison.OrdinalIgnoreCase)
                              // ds.Dose.Name.Equals ("DTaP 3", StringComparison.OrdinalIgnoreCase)
                              )
                                {
                                    cvd.IsDone = true;
                                    cvd.Due2EPI = true;
                                    DateTime d = childDB.DOB;
                                    cvd.GivenDate = d.AddDays(98);
                                }
                                else if (
                                  ds.Dose.Name.Equals("Measles # 1", StringComparison.OrdinalIgnoreCase)
                              )
                                {
                                    cvd.IsDone = true;
                                    cvd.Due2EPI = true;
                                    DateTime d = childDB.DOB;
                                    cvd.GivenDate = d.AddMonths(9);
                                }
                            }

                            if (ds.Dose.Name.StartsWith("HPV") && ds.Dose.DoseOrder == 3)
                                cvd.IsSkip = true;

                            cvd.Date = calculateDate(childDTO.DOB, ds.GapInDays);
                            _db.Schedules.Add(cvd);
                            _db.SaveChanges();
                        }
                    }
                }
                //  Child c = _db.Childs.Include("User").Include("Clinic").Where(x => x.Id == childDTO.Id).FirstOrDefault();
                Child c = _db.Childs.Where(x => x.Id == childDTO.Id).Include(x => x.User).Include(x => x.Clinic).Include(x => x.Clinic.Doctor.User).FirstOrDefault();
                if (c.Email != "")
                    UserEmail.ParentEmail(c);

                // generate SMS and save it to the db
                //  UserSMS u = new UserSMS(_db);
                //  u.ParentSMS(c);

                return new Response<ChildDTO>(true, null, childDTO);
            }
            // var cache = Configuration.CacheOutputConfiguration().GetCacheOutputProvider(Request);
            // cache.RemoveStartsWith(Configuration.CacheOutputConfiguration().MakeBaseCachekey((DoctorController t) => t.GetAllChildsOfaDoctor(0, 0, 20, "")));

        }

        [HttpPost("followup")]
        public Response<List<FollowUpDTO>> GetFollowUp(FollowUpDTO followUpDto)
        {
            {
                //when followup call from parent side
                if (followUpDto.DoctorId < 1)
                {
                    var dbChild = _db.Childs.Include("Clinic").FirstOrDefault();
                    followUpDto.DoctorId = dbChild.Clinic.DoctorId;
                }
                // when followup call from doctor side
                var dbFollowUps = _db.FollowUps.Include(x => x.Child)
                    .Where(f => f.DoctorId == followUpDto.DoctorId && f.ChildId == followUpDto.ChildId)
                    .OrderByDescending(x => x.CurrentVisitDate).ToList();
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

        // old invoice pdf
        [HttpGet("{Id}/{IsBrand}/{IsConsultationFee}/{InvoiceDate}/{DoctorId}/Download-Invoice-PDF")]
        public IActionResult DownloadInvoicePDF(int Id, bool IsBrand, bool IsConsultationFee, DateTime InvoiceDate, int DoctorId)
        {

            Stream stream;
            int amount = 0;
            int count = 1;
            int col = 3;
            int consultaionFee = 0;
            string childName = "";
            var document = new Document(PageSize.A4, 50, 50, 25, 25);

            var output = new MemoryStream();

            var writer = PdfWriter.GetInstance(document, output);
            writer.CloseStream = false;

            document.Open();
            //Page Heading
            GetPDFHeading(document, "INVOICE");

            //Access db data

            var dbDoctor = _db.Doctors.Where(x => x.Id == DoctorId).Include(x => x.User).FirstOrDefault();
            dbDoctor.InvoiceNumber = (dbDoctor.InvoiceNumber > 0) ? dbDoctor.InvoiceNumber + 1 : 1;
            var dbChild = _db.Childs.Include("Clinic").Where(x => x.Id == Id).FirstOrDefault();
            var dbSchedules = _db.Schedules.Include(x => x.Dose).ThenInclude(x => x.Vaccine).Include("Brand").Where(x => x.ChildId == Id).ToList();
            childName = dbChild.Name;
            //
            //Table 1 for description above amounts table
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

            //upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "noColor", 1, "left", "description"));

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

            //2nd Table
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
            //Rows
            table.AddCell(CreateCell(count.ToString(), "", 1, "center", "invoiceRecords"));
            //col = (col > 3) ? col - 3 : col-2;
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
                    //date is static due to date conversion issue
                    //  && schedule.Date.Date == DateTime.Now.Date
                    //when we add bulk injection we don't add brandId in schedule
                    if (schedule.IsDone && schedule.BrandId > 0)
                    {
                        count++;
                        table.AddCell(CreateCell(count.ToString(), "", 1, "center", "invoiceRecords"));
                        table.AddCell(CreateCell(schedule.Dose.Vaccine.Name, "", 1, "center", "invoiceRecords"));
                        if (IsBrand)
                        {
                            table.AddCell(CreateCell(schedule.Brand.Name, "", 1, "center", "invoiceRecords"));
                        }
                        var brandAmount = _db.BrandAmounts.Where(x => x.BrandId == schedule.BrandId && x.DoctorId == DoctorId).FirstOrDefault();
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

            //table.AddCell(CreateCell("Total(PKR)", "", col - 1, "right", "invoiceRecords"));

            //add consultancy fee
            if (IsConsultationFee)
            {
                amount = amount + (int)dbChild.Clinic.ConsultationFee;
            }

            _db.SaveChanges();
            document.Add(table);

            document.Add(new Paragraph(""));
            document.Add(new Chunk("\n"));
            //Table 3 for description above amounts table
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

            //var imgPath = System.Web.Hosting.HostingEnvironment.MapPath("~/Content/UserImages");
            //  var httpPostedSignatureImage = HttpContext.Request.Form.Files["SignatureImage"];
            var imgPath = Path.Combine(_host.ContentRootPath, "Content/UserImages");
            // using (var fileStream = new FileStream(imgPath, FileMode.Create)) 
            // httpPostedSignatureImage.CopyToAsync(fileStream);
            // dbDoctor.SignatureImage = httpPostedSignatureImage.FileName;

            var signatureImage = dbDoctor.SignatureImage;
            if (signatureImage == null)
            {
                signatureImage = "avatar.png";
            }
            Image img = Image.GetInstance(imgPath + "//" + signatureImage);

            img.ScaleAbsolute(2f, 2f);
            PdfPCell imageCell = new PdfPCell(img, true);
            imageCell.PaddingTop = 5;
            imageCell.Colspan = 1; // either 1 if you need to insert one cell
            imageCell.Border = 0;
            imageCell.FixedHeight = 40f;
            imageCell.HorizontalAlignment = Element.ALIGN_RIGHT;
            bottomTable.AddCell(imageCell);

            document.Add(bottomTable);
            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            stream = output;

            //}
            var FileName = childName.Replace(" ", "") + "_Invoice" + "_" + DateTime.UtcNow.AddHours(5).Date.ToString("MMMM-dd-yyyy") + ".pdf";
            return File(stream, "application/pdf", FileName);
        }
        // updated invoice pdf
        [HttpGet("{Id}/{InvoiceDate}/{ConsultationFee}/Download-Invoice-PDF")]
        public IActionResult DownloadInvoicePDFUpdated(int Id, DateTime InvoiceDate, int ConsultationFee)
        {
            // var IsConsultationFee = true;
            // var IsBrand = true;
            Stream stream;
            int amount = 0;
            int count = 0;
            // int col = 3;
            int consultaionFee = ConsultationFee;
            string childName = "";
            var document = new Document(PageSize.A4, 60, 60, 30, 30);
            var output = new MemoryStream();
            var writer = PdfWriter.GetInstance(document, output);
            writer.CloseStream = false;

            document.Open();
            //Page Heading
            //GetPDFHeading (document, "INVOICE");

            //Access db data
            var dbChild = _db.Childs.Include(x => x.Clinic).ThenInclude(x => x.Doctor).ThenInclude(y => y.User).Where(x => x.Id == Id).FirstOrDefault();
            var dbDoctor = dbChild.Clinic.Doctor;
            var DoctorId = dbDoctor.Id;
            dbDoctor.InvoiceNumber = (dbDoctor.InvoiceNumber > 0) ? dbDoctor.InvoiceNumber + 1 : 1;

            var dbSchedules = _db.Schedules.Include(x => x.Dose).ThenInclude(x => x.Vaccine).Include(x => x.Brand)
                .Where(x => x.ChildId == Id && x.Date.Date == InvoiceDate.Date && x.IsSkip != true && x.IsDone == true && x.IsDisease != true).ToList();
            childName = dbChild.Name;
            //Table 1 for description above amounts table
            PdfPTable upperTable = new PdfPTable(2);
            float[] upperTableWidths = new float[] { 250f, 250f };
            upperTable.HorizontalAlignment = 0;
            upperTable.TotalWidth = 470f;
            upperTable.LockedWidth = true;
            // upperTable.DefaultCell.PaddingLeft = 4;
            upperTable.SetWidths(upperTableWidths);

            upperTable.AddCell(CreateCell("DR SALMAN AHMAD BAJWA", "bold", 1, "left", "description"));
            //image code start
            var imgPath = Path.Combine(_host.ContentRootPath, "Resources/Images/cliniclogo.png");
            Image img = Image.GetInstance(imgPath);
            img.ScaleAbsolute(160f, 50f);
            PdfPCell imageCell = new PdfPCell(img, false);
            imageCell.Colspan = 1; // either 1 if you need to insert one cell
            imageCell.Rowspan = 2;
            imageCell.Border = 0;
            imageCell.FixedHeight = 1f;
            imageCell.HorizontalAlignment = Element.ALIGN_RIGHT;
            upperTable.AddCell(imageCell);
            //image code end

            upperTable.AddCell(CreateCell("MBBS, RMP, FCPS (Peads) \nConsultant Paediatrician & Neonatologist\nVaccinology and Immunization Expert", "unbold", 1, "left", "description"));
            upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "bold", 1, "left", "description"));
            upperTable.AddCell(CreateCell("info@vaccine.pk", "", 1, "right", "description"));
            upperTable.AddCell(CreateCell(dbChild.Clinic.Address, "unbold", 1, "left", "description"));
            upperTable.AddCell(CreateCell(InvoiceDate.ToString("dd-MM-yyyy"), "", 1, "right", "description"));
            upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "unbold", 1, "left", "description"));
            upperTable.AddCell(CreateCell("#StayHome #GetVaccinated", "", 1, "right", "description"));


            // upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "bold", 2, "left", "description"));
            // upperTable.AddCell(CreateCell(dbChild.Clinic.Address, "unbold", 2, "left", "description"));
            // upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "unbold", 2, "left", "description"));

            //upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "noColor", 1, "left", "description"));

            // upperTable.AddCell (CreateCell ("", "", 1, "right", "description"));

            // // if (IsConsultationFee) {
            // //     consultaionFee = (int) dbChild.Clinic.ConsultationFee;
            // // }

            // upperTable.AddCell (CreateCell ("", "", 1, "left", "description"));
            // upperTable.AddCell (CreateCell ("", "", 1, "right", "description"));
            // upperTable.AddCell (CreateCell ("P: " + dbDoctor.PhoneNo, "", 1, "left", "description"));
            // upperTable.AddCell (CreateCell ("", "", 1, "right", "description"));
            // upperTable.AddCell (CreateCell ("M: " + dbDoctor.User.MobileNumber, "", 1, "left", "description"));
            // upperTable.AddCell (CreateCell ("", "", 1, "right", "description"));

            document.Add(upperTable);
            Paragraph title = new Paragraph("INVOICE");
            title.Font = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
            title.Alignment = Element.ALIGN_CENTER;
            document.Add(title);

            //2nd Table
            float[] widths = new float[] { 170f, 300f };
            PdfPTable childtable = new PdfPTable(2);
            childtable.HorizontalAlignment = 0;
            childtable.TotalWidth = 470f;
            childtable.LockedWidth = true;
            childtable.SetWidths(widths);
            childtable.SpacingBefore = 10;
            childtable.SpacingAfter = 10;

            childtable.AddCell(CreateCell("Name of Kid/Patient", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.Name, " ", 1, "left", "invoiceRecords"));

            childtable.AddCell(CreateCell("Father/Mother Name:", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.FatherName, "", 1, "left", "invoiceRecords"));

            childtable.AddCell(CreateCell("Date of Birth:", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.DOB.ToString("dd/MM/yyyy"), "", 1, "left", "invoiceRecords"));

            childtable.AddCell(CreateCell("City", "backgroudLightGray", 1, "left", "invoiceRecords"));
            childtable.AddCell(CreateCell(dbChild.City, " ", 1, "left", "invoiceRecords"));

            _db.SaveChanges();
            document.Add(childtable);

            Paragraph vaccinetitle = new Paragraph("VACCINATION DETAILS");
            vaccinetitle.Font = FontFactory.GetFont(FontFactory.HELVETICA, 10, Font.BOLD);
            vaccinetitle.Alignment = Element.ALIGN_CENTER;
            document.Add(vaccinetitle);

            // table 3 for vaccination details
            float[] vaccinationwidths = new float[] { 10f, 70, 50, 30f, 30f, 30f };
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
                        if (schedule.BrandId > 0)
                        {
                            vaccinetable.AddCell(CreateCell(schedule.Brand.Name, "", 1, "left", "invoiceRecords"));
                        }
                        else
                        {
                            vaccinetable.AddCell(CreateCell(" ", "", 1, "center", "invoiceRecords"));
                        }

                        vaccinetable.AddCell(CreateCell("1", " ", 1, "right", "invoiceRecords"));

                        var brandAmount = _db.BrandAmounts.Where(x => x.BrandId == schedule.BrandId && x.DoctorId == DoctorId).FirstOrDefault();
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
                    vaccinetable.AddCell(CreateCell(consultaionFee.ToString(), " ", 1, "right", "invoiceRecords"));
                }

                vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell("Total", "backgroudLightGray", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell((count).ToString(), "backgroudLightGray", 1, "right", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(" ", "backgroudLightGray", 1, "right", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell((amount + consultaionFee).ToString(), "backgroudLightGray", 1, "right", "invoiceRecords"));

                vaccinetable.AddCell(CreateCell(" ", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell("Amount in words", " ", 1, "left", "invoiceRecords"));
                vaccinetable.AddCell(CreateCell(ConvertWholeNumber((amount + consultaionFee).ToString()) + " Only", " ", 4, "left", "inwordamount"));

            }

            // loop end

            document.Add(vaccinetable);

            //    Paragraph signatureheading = new Paragraph("AUTHORIZED SIGNATURES");
            //     signatureheading.Font = FontFactory.GetFont(FontFactory.HELVETICA, 12);
            //     signatureheading.Alignment = Element.ALIGN_LEFT;
            //     document.Add (signatureheading);

            //Table 4 for description above amounts table
            PdfPTable bottomTable = new PdfPTable(2);
            float[] bottomTableWidths = new float[] { 235f, 235f };
            bottomTable.HorizontalAlignment = 0;
            bottomTable.TotalWidth = 470f;
            bottomTable.LockedWidth = true;
            bottomTable.SetWidths(bottomTableWidths);

            bottomTable.AddCell(CreateCell(" ", "bold", 2, "left", "description"));
            bottomTable.AddCell(CreateCell("Quick links: ", "", 2, "left", "description"));

            bottomTable.AddCell(CreateCell("Vaccine.pk", "", 1, "left", "description"));
            bottomTable.AddCell(CreateCell("Web: SalmanBajwa.com", "", 1, "right", "description"));
            bottomTable.AddCell(CreateCell("Vaccine.pk/booking", "", 1, "left", "description"));
            bottomTable.AddCell(CreateCell("Phone/WhatsApp: +923335196658", "", 1, "right", "description"));
            bottomTable.AddCell(CreateCell("Vaccine.pk/pricing", "", 1, "left", "description"));
            bottomTable.AddCell(CreateCell("Email: dr@salmanbajwa.com", "", 1, "right", "description"));
            bottomTable.WriteSelectedRows(0, -1, 65, 100, writer.DirectContent);

            // var imgcellLeft = CreateCell ("", "", 1, "left", "description");
            // imgcellLeft.PaddingTop = 5;
            // bottomTable.AddCell (imgcellLeft);
            // document.Add (bottomTable);
            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            stream = output;

            //}
            var FileName = childName.Replace(" ", "") + "_Invoice" + "_" + DateTime.UtcNow.AddHours(5).Date.ToString("MMMM-dd-yyyy") + ".pdf";
            return File(stream, "application/pdf", FileName);
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
                bool beginsZero = false; //tests for 0XX    
                bool isDone = false; //test if already translated    
                double dblAmt = (Convert.ToDouble(Number));
                //if ((dblAmt > 0) && number.StartsWith("0"))    
                if (dblAmt > 0)
                { //test for zero or digit zero in a nuemric    
                    beginsZero = Number.StartsWith("0");

                    int numDigits = Number.Length;
                    int pos = 0; //store digit grouping    
                    String place = ""; //digit grouping name:hundres,thousand,etc...    
                    switch (numDigits)
                    {
                        case 1: //ones' range    

                            word = ones(Number);
                            isDone = true;
                            break;
                        case 2: //tens' range    
                            word = tens(Number);
                            isDone = true;
                            break;
                        case 3: //hundreds' range    
                            pos = (numDigits % 3) + 1;
                            place = " Hundred ";
                            break;
                        case 4: //thousands' range    
                        case 5:
                        case 6:
                            pos = (numDigits % 4) + 1;
                            place = " Thousand ";
                            break;
                        case 7: //millions' range    
                        case 8:
                        case 9:
                            pos = (numDigits % 7) + 1;
                            place = " Million ";
                            break;
                        case 10: //Billions's range    
                        case 11:
                        case 12:

                            pos = (numDigits % 10) + 1;
                            place = " Billion ";
                            break;
                        //add extra case options for anything above Billion...    
                        default:
                            isDone = true;
                            break;
                    }
                    if (!isDone)
                    { //if transalation is not done, continue...(Recursion comes in now!!)    
                        if (Number.Substring(0, pos) != "0" && Number.Substring(pos) != "0")
                        {
                            try
                            {
                                word = ConvertWholeNumber(Number.Substring(0, pos)) + place + ConvertWholeNumber(Number.Substring(pos));
                            }
                            catch { }
                        }
                        else
                        {
                            word = ConvertWholeNumber(Number.Substring(0, pos)) + ConvertWholeNumber(Number.Substring(pos));
                        }

                        //check for trailing zeros    
                        //if (beginsZero) word = " and " + word.Trim();    
                    }
                    //ignore digit grouping names    
                    if (word.Trim().Equals(place.Trim())) word = "";
                }
            }
            catch { }
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
                dbChild.FatherName = childDTO.FatherName;
                dbChild.PreferredDayOfWeek = childDTO.PreferredDayOfWeek;
                dbChild.Gender = childDTO.Gender;
                dbChild.City = childDTO.City;
                dbChild.PreferredDayOfReminder = childDTO.PreferredDayOfReminder;
                dbChild.PreferredSchedule = childDTO.PreferredSchedule;
                dbChild.IsEPIDone = childDTO.IsEPIDone;
                dbChild.IsVerified = childDTO.IsVerified;
                var dbUser = dbChild.User;
                dbUser.MobileNumber = childDTO.MobileNumber;
                _db.SaveChanges();
                return new Response<ChildDTO>(true, null, childDTO);
            }
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id)

        {

            var dbChild = _db.Childs.Include(x => x.User).ThenInclude(x => x.Childs).Include(x => x.Schedules).Include(x => x.FollowUps).Where(c => c.Id == Id).FirstOrDefault();
            _db.Schedules.RemoveRange(dbChild.Schedules);
            _db.FollowUps.RemoveRange(dbChild.FollowUps);
            if (dbChild.User.Childs.Count == 1)
                _db.Users.Remove(dbChild.User);
            _db.Childs.Remove(dbChild);
            _db.SaveChanges();
            return new Response<string>(true, "Child is deleted successfully", null);

        }

        //Date Function
        protected DateTime calculateDate(DateTime date, int GapInDays)
        {
            // For 3 months
            if (GapInDays == 84)
                return date.AddMonths(3);
            // For 9 Year 1 month
            else if (GapInDays == 3315)
                return date.AddYears(9).AddMonths(1);
            // For 10 Year 6 month
            else if (GapInDays == 3833)
                return date.AddYears(10).AddMonths(6);
            // For 1 to 15 years
            else if (GapInDays == 365 || GapInDays == 730 || GapInDays == 1095 ||
                GapInDays == 1460 || GapInDays == 1825 || GapInDays == 2190 || GapInDays == 2555 ||
                GapInDays == 2920 || GapInDays == 3285 || GapInDays == 3650 || GapInDays == 4015 ||
                GapInDays == 4380 || GapInDays == 4745 || GapInDays == 5110 || GapInDays == 5475)
                return date.AddYears((int)(GapInDays / 365));
            // From 6 months to 11 months
            else if (GapInDays >= 168 && GapInDays <= 334)
                return date.AddMonths((int)(GapInDays / 28));
            // From 13 months to 20 months
            else if (GapInDays >= 395 && GapInDays <= 608)
                return date.AddMonths((int)(GapInDays / 29));
            // From 21 months to 11 months
            else if (GapInDays >= 639 && GapInDays <= 1795)
                return date.AddMonths((int)(GapInDays / 30));
            else
                return date.AddDays(GapInDays);
        }
    }

    public class PDFFooter : PdfPageEventHelper
    {
        Child child = new Child();
        public PDFFooter(Child postedChild)
        {
            child = postedChild;
        }
        // write on top of document
        //public override void OnOpenDocument(PdfWriter writer, Document document)
        //{
        //    base.OnOpenDocument(writer, document);
        //    PdfPTable tabFot = new PdfPTable(new float[] { 1F });
        //    tabFot.SpacingAfter = 10F;
        //    PdfPCell cell;
        //    tabFot.TotalWidth = 300F;
        //    cell = new PdfPCell(new Phrase("Header"));
        //    tabFot.AddCell(cell);
        //    tabFot.WriteSelectedRows(0, -1, 150, document.Top, writer.DirectContent);
        //}

        // write on start of each page
        public override void OnStartPage(PdfWriter writer, Document document)
        {
            base.OnStartPage(writer, document);
        }

        // write on end of each page
        public override void OnEndPage(PdfWriter writer, Document document)
        {
            base.OnEndPage(writer, document);
            string footer = @"NOTE: 1. Vaccines can cause fever, localised redness and pain. 2. This schedule is valid to produce on demand at all airports, embassies and schools of the world. 3. We alway use best available vaccine brand/manufacturer. With time and continuous research 

            vaccine brand can be different for future doses. Disclaimer: This schedule provides recommended dates for immunisations for individual based date of birth, past history of immunisation and disease. Your consultant may update the due dates or add/remove vaccines. Vaccine.pk, 
            
            its management or staﬀ holds no responsibility for any loss or damage due to any vaccine given. " + "  Printed On" + " " + DateTime.UtcNow.AddHours(5).ToString("MMMM dd, yyyy");

            footer = footer.Replace(Environment.NewLine, String.Empty).Replace("  ", String.Empty);
            Font georgia = FontFactory.GetFont("georgia", 9f);

            Chunk beginning = new Chunk(footer, georgia);

            PdfPTable tabFot = new PdfPTable(1);
            PdfPCell cell;
            tabFot.SetTotalWidth(new float[] { 500f });
            tabFot.DefaultCell.HorizontalAlignment = Element.ALIGN_JUSTIFIED;
            cell = new PdfPCell(new Phrase(beginning));

            cell.Border = 0;
            tabFot.AddCell(cell);
            tabFot.WriteSelectedRows(0, -1, 50, 90, writer.DirectContent); //tabFot.WriteSelectedRows (0, -1, 10, 50, writer.DirectContent);
        }

        //write on close of document
        public override void OnCloseDocument(PdfWriter writer, Document document)
        {
            base.OnCloseDocument(writer, document);
        }
    }
}