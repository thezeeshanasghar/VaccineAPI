using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using iTextSharp.text.pdf;
using iTextSharp.text;
using Microsoft.AspNetCore.Hosting;
//using WebApi.OutputCache.V2;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChildController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
         private readonly IHostingEnvironment _host;

        public ChildController(Context context, IMapper mapper,IHostingEnvironment host)
        {
            _db = context;
            _mapper = mapper;
            _host = host;
        }

        [HttpGet]
        public Response<IEnumerable<ChildDTO>> Get()
        {
            var dbChilds = _db.Childs.Include(x=>x.User).ToList();
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

        [HttpGet("clinic/{id}")]
        public Response<IEnumerable<ChildDTO>> GetChildByClinic(long id)
        {
            var dbChilds = _db.Childs.Include(x=>x.User).Where(x=>x.ClinicId == id).OrderByDescending(x=>x.Id).ToList();
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
                var child = _db.Childs.Include(x => x.Schedules).FirstOrDefault(c => c.Id == id);
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
            var FileName =dbScheduleChild.Name.Replace(" ","")+"_Schedule_" +DateTime.UtcNow.AddHours(5).ToString("MMMM-dd-yyyy")+ ".pdf";
           return File(stream, "application/pdf", FileName);
        }
       
        // private Stream CreateSchedulePdf(int childId)
        // {
        //     //Access db data
        //     var dbChild = _db.Childs.Include(x => x.User).Include(x => x.Clinic).ThenInclude(x=>x.Doctor).ThenInclude(x => x.User).Where(x => x.Id == childId).FirstOrDefault();
        //     var dbDoctor = dbChild.Clinic.Doctor;
        //     var child = _db.Childs.Include(x=>x.Schedules).ThenInclude(x => x.Dose).FirstOrDefault(c => c.Id == childId);
        //     var dbSchedules = child.Schedules.OrderBy(x => x.Date).ToList();
        //     var scheduleDoses = from schedule in dbSchedules
        //                         group schedule.Dose by schedule.Date into scheduleDose
        //                         select new { Date = scheduleDose.Key, Doses = scheduleDose.ToList() };

        //     int count = 0;
        //     //
        //      var document = new Document(PageSize.A4, 50, 50, 25, 105);
        //     {
        //         var output = new MemoryStream();

        //         var writer = PdfWriter.GetInstance(document, output);
        //         writer.CloseStream = false;
        //         // calling PDFFooter class to Include in document
        //         writer.PageEvent = new PDFFooter(child);
        //         document.Open();
        //         GetPDFHeading(document, "Childhood Vaccination Record");

        //         //Table 1 for description above Schedule table
        //         PdfPTable upperTable = new PdfPTable(2);
        //         float[] upperTableWidths = new float[] { 250f, 250f };
        //         upperTable.HorizontalAlignment = 0;
        //         upperTable.TotalWidth = 500f;
        //         upperTable.LockedWidth = true;
        //         upperTable.SetWidths(upperTableWidths);

        //         upperTable.AddCell(CreateCell(dbDoctor.DisplayName, "bold", 1, "left", "description"));
        //         upperTable.AddCell(CreateCell(dbChild.Name, "bold", 1, "right", "description"));

        //         upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "", 1, "left", "description"));
        //         if (dbChild.Gender == "Girl")
        //         {
        //             upperTable.AddCell(CreateCell("D/O " + dbChild.FatherName, "", 1, "right", "description"));
        //         }
        //         else
        //         {
        //             upperTable.AddCell(CreateCell("S/O " + dbChild.FatherName, "", 1, "right", "description"));
        //         }

        //         upperTable.AddCell(CreateCell(dbChild.Clinic.Address, "", 1, "left", "description"));
        //         upperTable.AddCell(CreateCell("+" + dbChild.User.CountryCode + "-" + dbChild.User.MobileNumber, "", 1, "right", "description"));

        //         upperTable.AddCell(CreateCell("Clinic Ph#: " + dbChild.Clinic.PhoneNumber, "", 1, "left", "description"));
        //         upperTable.AddCell(CreateCell(dbChild.DOB.ToString("MM/dd/yyyy"), "", 1, "right", "description"));

        //         if (dbDoctor.ShowPhone)
        //         {
        //             upperTable.AddCell(CreateCell("Doctor Ph#: +" + dbDoctor.User.CountryCode + "-" + dbDoctor.PhoneNo, "", 1, "left", "description"));
        //         }
        //         else
        //         {
        //             upperTable.AddCell(CreateCell("", "", 1, "left", "description"));

        //         }
        //         upperTable.AddCell(CreateCell("", "", 1, "right", "description"));
        //         document.Add(upperTable);

        //         document.Add(new Paragraph(""));
        //         document.Add(new Chunk("\n"));
        //         //Schedule Table
        //         float[] widths = new float[] { 25f, 145f, 70f, 70f, 45f, 45f, 45f };

        //         PdfPTable table = new PdfPTable(7);
        //         table.HorizontalAlignment = 0;
        //         table.TotalWidth = 500f;
        //         table.LockedWidth = true;
        //         table.SetWidths(widths);

        //         table.AddCell(CreateCell("Age", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         table.AddCell(CreateCell("Vaccine", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         table.AddCell(CreateCell("Due Date", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         table.AddCell(CreateCell("Given Date", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         table.AddCell(CreateCell("Weight", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         table.AddCell(CreateCell("Height", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         table.AddCell(CreateCell("OFC", "backgroudLightGray", 1, "center", "scheduleRecords"));
        //         //table.AddCell(CreateCell("Injected", "backgroudLightGray", 1, "center", "scheduleRecords"));

        //       //  var imgPath = System.Web.Hosting.HostingEnvironment.MapPath("~/Content/img");
        //        var imgPath = Path.Combine(_host.ContentRootPath, "Content/img");
        //         foreach (var schedule in scheduleDoses)
        //         {
        //             int doseCount = 0;
        //             Paragraph p = new Paragraph();
        //             foreach (var dose in schedule.Doses)
        //             {
        //                 count++;
        //                 doseCount++;
        //                 Font font = FontFactory.GetFont(FontFactory.HELVETICA, 10);
        //                 // select only injected dose schedule
        //                 var dbSchedule = dose.Schedules.Where(x => x.DoseId == dose.Id).FirstOrDefault();

        //                 // table.AddCell(CreateCell(count.ToString(), "", 1, "center", "scheduleRecords"));
        //                 //table.AddCell(CreateCell(dose.Name, "", 1, "", "scheduleRecords"));


        //                 if (doseCount == 1)
        //                 {
        //                     PdfPCell ageCell = new PdfPCell(new Phrase(count.ToString(), font));
        //                     ageCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                     ageCell.Rowspan = schedule.Doses.Count();
        //                     table.AddCell(ageCell);
        //                     foreach (var d in schedule.Doses)
        //                     {
        //                         p.Add(d.Name + "\n");
        //                     }
        //                     PdfPCell dosenameCell = new PdfPCell(p);
        //                     dosenameCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                     dosenameCell.Rowspan = schedule.Doses.Count();
        //                     table.AddCell(dosenameCell);

        //                     PdfPCell sameDueDateCell = new PdfPCell(new Phrase(schedule.Date.Date.ToString("dd/MM/yyyy"), font));
        //                     sameDueDateCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                     sameDueDateCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        //                     sameDueDateCell.PaddingBottom = schedule.Doses.Count();
        //                     sameDueDateCell.Rowspan = schedule.Doses.Count();
        //                     table.AddCell(sameDueDateCell);
        //                 }

        //                 PdfPCell dateCell = new PdfPCell(new Phrase(String.Format("{0:dd/MM/yyyy}", dbSchedule.GivenDate), font));
        //                 dateCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                 table.AddCell(dateCell);

        //                 PdfPCell weightCell = new PdfPCell(new Phrase(dbSchedule.Weight > 0 ? dbSchedule.Weight.ToString() : "", font));
        //                 weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                 table.AddCell(weightCell);

        //                 PdfPCell heightCell = new PdfPCell(new Phrase(dbSchedule.Height > 0 ? dbSchedule.Height.ToString() : "", font));
        //                 heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                 table.AddCell(heightCell);

        //                 PdfPCell circleCell = new PdfPCell(new Phrase(dbSchedule.Circle > 0 ? dbSchedule.Circle.ToString() : "", font));
        //                 circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                 table.AddCell(circleCell);
        //                 //table.AddCell(CreateCell(String.Format("{0:dd/MM/yyyy}", dbSchedule.GivenDate), "", 1, "", "scheduleRecords"));
        //                 //table.AddCell(CreateCell(dbSchedule.Weight.ToString(), "", 1, "", "scheduleRecords"));
        //                 //table.AddCell(CreateCell(dbSchedule.Height.ToString(), "", 1, "", "scheduleRecords"));
        //                 //table.AddCell(CreateCell(dbSchedule.Circle.ToString(), "", 1, "", "scheduleRecords"));


        //                 ////  add a image
        //                 //var isDone = dbSchedule.Where(x => x.IsDone).FirstOrDefault();
        //                 //string injectionPath = "";
        //                 //if (dbSchedule.IsDone)
        //                 //{
        //                 //    injectionPath = "\\injectionFilled.png";
        //                 //}
        //                 //else
        //                 //{
        //                 //    injectionPath = "\\injectionEmpty.png";
        //                 //}
        //                 //Image img = Image.GetInstance(imgPath + injectionPath);
        //                 //img.ScaleAbsolute(2f, 2f);
        //                 //PdfPCell imageCell = new PdfPCell(img, true);
        //                 //imageCell.PaddingBottom = 2;
        //                 //imageCell.Colspan = 1; // either 1 if you need to insert one cell
        //                 ////imageCell.Border = 0;
        //                 //imageCell.FixedHeight = 15f;
        //                 //imageCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //                 //table.AddCell(imageCell);
        //             }


        //             //  imageCell.setHorizontalAlignment(Element.ALIGN_CENTER);

        //         }

        //         document.Add(table);
        //         document.Close();

        //         output.Seek(0, SeekOrigin.Begin);

        //         return output;
        //     }
        // }

         private Stream CreateSchedulePdf(int childId)
        {
            //Access db data
            var dbChild = _db.Childs.Include(x => x.User).Include(x => x.Clinic).ThenInclude(x=>x.Doctor).ThenInclude(x => x.User).Where(x => x.Id == childId).FirstOrDefault();
            var dbDoctor = dbChild.Clinic.Doctor;
            var child = _db.Childs.Include(x=>x.Schedules).ThenInclude(x => x.Dose).Include(x=>x.Schedules).ThenInclude(x => x.Brand).FirstOrDefault(c => c.Id == childId);
            var dbSchedules = child.Schedules.OrderBy(x => x.Date).ToList();
            var scheduleDoses = from schedule in dbSchedules
                                group schedule.Dose by schedule.Date into scheduleDose
                                select new { Date = scheduleDose.Key, Doses = scheduleDose.ToList() };

            int count = 0;
            //
             var document = new Document(PageSize.A4, 50, 50, 25, 105);
            {
                var output = new MemoryStream();

                var writer = PdfWriter.GetInstance(document, output);
                writer.CloseStream = false;
                // calling PDFFooter class to Include in document
                writer.PageEvent = new PDFFooter(child);
                document.Open();
                GetPDFHeading(document, "Immunization Record");

                //Table 1 for description above Schedule table
                PdfPTable upperTable = new PdfPTable(2);
                float[] upperTableWidths = new float[] { 250f, 250f };
                upperTable.HorizontalAlignment = 0;
                upperTable.TotalWidth = 500f;
                upperTable.LockedWidth = true;
                upperTable.SetWidths(upperTableWidths);

                upperTable.AddCell(CreateCell(dbDoctor.DisplayName, "bold", 1, "left", "description"));
                upperTable.AddCell(CreateCell(dbChild.Name, "bold", 1, "right", "description"));

                upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "", 1, "left", "description"));
                if (dbChild.Gender == "Girl")
                {
                    upperTable.AddCell(CreateCell("D/O " + dbChild.FatherName, "", 1, "right", "description"));
                }
                else
                {
                    upperTable.AddCell(CreateCell("S/O " + dbChild.FatherName, "", 1, "right", "description"));
                }

                upperTable.AddCell(CreateCell(dbChild.Clinic.Address, "", 1, "left", "description"));
                upperTable.AddCell(CreateCell("+" + dbChild.User.CountryCode + "-" + dbChild.User.MobileNumber, "", 1, "right", "description"));

                upperTable.AddCell(CreateCell("Clinic Ph#: " + dbChild.Clinic.PhoneNumber, "", 1, "left", "description"));
                upperTable.AddCell(CreateCell(dbChild.DOB.ToString("dd MMMM, yyyy"), "", 1, "right", "description"));

                // if (dbDoctor.ShowPhone)
                // {
                    upperTable.AddCell(CreateCell("Address: " + dbChild.Clinic.Address , "", 1, "left", "description"));
                // }
                // else
                // {
                //     upperTable.AddCell(CreateCell("", "", 1, "left", "description"));

                // }
                upperTable.AddCell(CreateCell("", "", 1, "right", "description"));
                document.Add(upperTable);

                document.Add(new Paragraph(""));
                document.Add(new Chunk("\n"));
                //Schedule Table
                float[] widths = new float[] { 25f, 145f, 70f, 70f, 45f, 45f, 45f };

                PdfPTable table = new PdfPTable(7);
                table.HorizontalAlignment = 0;
                table.TotalWidth = 500f;
                table.LockedWidth = true;
                table.SetWidths(widths);

                table.AddCell(CreateCell("Sr", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Vaccine", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Status", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Brand", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Weight", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("Height", "backgroudLightGray", 1, "center", "scheduleRecords"));
                table.AddCell(CreateCell("OFC", "backgroudLightGray", 1, "center", "scheduleRecords"));
                //table.AddCell(CreateCell("Injected", "backgroudLightGray", 1, "center", "scheduleRecords"));

              //  var imgPath = System.Web.Hosting.HostingEnvironment.MapPath("~/Content/img");
               var imgPath = Path.Combine(_host.ContentRootPath, "Content/img");
                foreach (var dbSchedule in dbSchedules)
                {
                    if (dbSchedule.IsSkip ==false ||dbSchedule.IsSkip == null )
                    {
                    int doseCount = 0;
                    Paragraph p = new Paragraph();
                        count++;
                        doseCount++;
                        Font font = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                      
                        {
                            PdfPCell ageCell = new PdfPCell(new Phrase(count.ToString(), font));
                            ageCell.HorizontalAlignment = Element.ALIGN_CENTER;
                          
                            table.AddCell(ageCell);
                           
                          
                            PdfPCell dosenameCell = new PdfPCell(new Phrase(dbSchedule.Dose.Name , font));
                            dosenameCell.HorizontalAlignment = Element.ALIGN_CENTER;
                           
                            table.AddCell(dosenameCell);
                            if(dbSchedule.IsDone == true && dbSchedule.IsDisease != true )
                            {
                            PdfPCell statusCell = new PdfPCell(new Phrase("Given on "+dbSchedule.GivenDate?.Date.ToString("dd/MM/yyyy"), font));
                            statusCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            table.AddCell(statusCell);
                            }
                            else if (dbSchedule.IsDone == false && dbSchedule.IsDisease != true )
                            {
                            PdfPCell statusCell = new PdfPCell(new Phrase("Due on "+dbSchedule.Date.Date.ToString("dd/MM/yyyy"), font));
                            statusCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            table.AddCell(statusCell);
                            }

                             else 
                            {
                            PdfPCell statusCell = new PdfPCell(new Phrase("Diseased in "+dbSchedule.DiseaseYear, font));
                            statusCell.HorizontalAlignment = Element.ALIGN_CENTER;
                            table.AddCell(statusCell);
                            }

                            PdfPCell brandCell = new PdfPCell(new Phrase(dbSchedule.Brand != null ? dbSchedule.Brand.Name.ToString() : "N/A", font));
                            brandCell.HorizontalAlignment = Element.ALIGN_CENTER;
                          
                            table.AddCell(brandCell);

                             PdfPCell weightCell = new PdfPCell(new Phrase(dbSchedule.Weight > 0 ? dbSchedule.Weight.ToString() : "N/A", font));
                        weightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                        table.AddCell(weightCell);

                        PdfPCell heightCell = new PdfPCell(new Phrase(dbSchedule.Height > 0 ? dbSchedule.Height.ToString() : "N/A", font));
                        heightCell.HorizontalAlignment = Element.ALIGN_CENTER;
                        table.AddCell(heightCell);

                        PdfPCell circleCell = new PdfPCell(new Phrase(dbSchedule.Circle > 0 ? dbSchedule.Circle.ToString() : "N/A", font));
                        circleCell.HorizontalAlignment = Element.ALIGN_CENTER;
                        table.AddCell(circleCell);
                        }

                        }


                }

                document.Add(table);
                document.Close();

                output.Seek(0, SeekOrigin.Begin);

                return output;
            }
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


      [HttpGet("search")]
        public Response<IEnumerable<ChildDTO>> SearchChildrenByCity([FromQuery] string name = "", [FromQuery] string city = "", [FromQuery] string dob = "", [FromQuery] string gender = "" )
        
                {

                    List<Child> dbChildrenResults = _db.Childs.Include(x=>x.User).ToList();
                    List<ChildDTO> childDTOs = new List<ChildDTO>();
                    if (!String.IsNullOrEmpty(name))
                        dbChildrenResults = dbChildrenResults.Where(c =>
                               c.Name.ToLower().Contains(name.Trim().ToLower()) ||
                               c.FatherName.ToLower().Contains(name.Trim().ToLower())).ToList();

                    if (!String.IsNullOrEmpty(city))
                        dbChildrenResults = dbChildrenResults.Where(c => c.City != null && c.City.ToLower().Contains(city.Trim().ToLower())).ToList();

                    if(!String.IsNullOrEmpty(dob))
                        dbChildrenResults = dbChildrenResults.Where(c => c.DOB == Convert.ToDateTime(dob).Date).ToList();
                        
                    if(!String.IsNullOrEmpty(gender))
                        dbChildrenResults = dbChildrenResults.Where(c => c.Gender == gender).ToList();

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

                // get doctor schedule and apply it to child and save in Schedule table
                Clinic clinic = _db.Clinics.Where(x => x.Id == childDTO.ClinicId).Include(x=>x.Doctor).FirstOrDefault();
                Doctor doctor = clinic.Doctor;
                
                List<DoctorSchedule> dss = _db.DoctorSchedules.Where(x=>x.DoctorId == doctor.Id).ToList();
                //IEnumerable<DoctorSchedule> dss = doctor.DoctorSchedules;
                
                foreach (DoctorSchedule ds in dss)
                {
                    var dbDose = _db.Doses.Where(x => x.Id == ds.DoseId).Include(x=>x.Vaccine).FirstOrDefault();
                    if (childDTO.ChildVaccines.Any(x => x.Id == dbDose.Vaccine.Id))
                    {
                        Schedule cvd = new Schedule();
                        cvd.ChildId = childDTO.Id;
                        cvd.DoseId = ds.DoseId;
                        if (childDTO.IsEPIDone)
                        {
                            if (ds.Dose.Name.StartsWith("BCG")
                                || ds.Dose.Name.StartsWith("HBV")
                                || ds.Dose.Name.Equals("OPV # 1"))
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                cvd.GivenDate = childDB.DOB;
                            }
                            else if (
                              ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 1", StringComparison.OrdinalIgnoreCase)
                              || ds.Dose.Name.Equals("Pneumococcal # 1", StringComparison.OrdinalIgnoreCase)
                              || ds.Dose.Name.Equals("Rota Virus GE # 1", StringComparison.OrdinalIgnoreCase)
                              )
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                    DateTime d = childDB.DOB;
                                    cvd.GivenDate = d.AddDays(42);
                            }
                            else if (
                            ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 2", StringComparison.OrdinalIgnoreCase)
                            || ds.Dose.Name.Equals("Pneumococcal # 2", StringComparison.OrdinalIgnoreCase)
                            || ds.Dose.Name.Equals("Rota Virus GE # 2", StringComparison.OrdinalIgnoreCase)
                              )
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                      DateTime d = childDB.DOB;
                                     cvd.GivenDate = d.AddDays(70);
                            }
                            else if (
                            ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 3", StringComparison.OrdinalIgnoreCase)
                            || ds.Dose.Name.Equals("Pneumococcal # 3", StringComparison.OrdinalIgnoreCase)
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

                         cvd.Date = calculateDate(childDTO.DOB, ds.GapInDays);

                        _db.Schedules.Add(cvd);
                        _db.SaveChanges();
                    }
                }
              //  Child c = _db.Childs.Include("User").Include("Clinic").Where(x => x.Id == childDTO.Id).FirstOrDefault();
              Child c = _db.Childs.Where(x => x.Id == childDTO.Id).Include(x=>x.User).Include(x=>x.Clinic).Include(x=>x.Clinic.Doctor.User).FirstOrDefault();
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
                var dbFollowUps = _db.FollowUps.Include(x=>x.Child)
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

            Font font = FontFactory.GetFont(FontFactory.HELVETICA, 10);
            if (color == "bold" || color == "backgroudLightGray")
            {
                font = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
            }
            if (table != "description")
            {
                font.Size = 7;
            }
            PdfPCell cell = new PdfPCell(new Phrase(value, font));
            if (color == "backgroudLightGray")
            {
                cell.BackgroundColor = GrayColor.LightGray;
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
            return cell;

        }

       // [HttpPost("Download-Invoice-PDF")]
       [HttpGet("{Id}/{IsBrand}/{IsConsultationFee}/{InvoiceDate}/{DoctorId}/Download-Invoice-PDF")]
  //      public IActionResult DownloadInvoicePDF(ChildDTO childDTO)
        public IActionResult DownloadInvoicePDF(int Id , bool IsBrand , bool IsConsultationFee , DateTime InvoiceDate , int DoctorId )
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
            //  upperTable.AddCell(CreateCell("Consultation Fee: " + consultaionFee, "noColor", 1, "left", "description"));
            //upperTable.AddCell(CreateCell("", "", 1, "left", "description"));
            //upperTable.AddCell(CreateCell("", "", 1, "right", "description"));

            upperTable.AddCell(CreateCell("", "", 1, "left", "description"));
            upperTable.AddCell(CreateCell("", "", 1, "right", "description"));
            //upperTable.AddCell(CreateCell("", "", 1, "left", "description"));
            //upperTable.AddCell(CreateCell("Father: " + dbChild.FatherName, "", 1, "right", "description"));
            //upperTable.AddCell(CreateCell("", "", 1, "left", "description"));
            //upperTable.AddCell(CreateCell("Child: " + dbChild.Name, "", 1, "right", "description"));
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
            //table.AddCell(CreateCell(amount.ToString(), "", 1, "right", "invoiceRecords"));

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
           var FileName = childName.Replace(" ","") +"_Invoice"+"_"+DateTime.UtcNow.AddHours(5).Date.ToString("MMMM-dd-yyyy")+".pdf" ;
           return File(stream, "application/pdf", FileName);
        }


        [HttpPut]
        public Response<ChildDTO> Put([FromBody] ChildDTO childDTO)
        {


            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            childDTO.Name = textInfo.ToTitleCase(childDTO.Name);
            childDTO.FatherName = textInfo.ToTitleCase(childDTO.FatherName);

            {
                var dbChild = _db.Childs.Include(x=>x.User).FirstOrDefault(c => c.Id == childDTO.Id);
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
            {
                var dbChild = _db.Childs.Where(c => c.Id == Id).FirstOrDefault();
                //entities.Schedules.RemoveRange(dbChild.Schedules);
                //entities.FollowUps.RemoveRange(dbChild.FollowUps);
                if (dbChild.User.Childs.Count == 1)
                    _db.Users.Remove(dbChild.User);
                _db.Childs.Remove(dbChild);
                _db.SaveChanges();
                return new Response<string>(true, "Child is deleted successfully", null);
            }
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
            string footer = @"This schedule is automatically generated for " + child.Clinic.Name + @" by Vaccs.io Visit https://www.vaccs.io/ for more details
             ____________________________________________________________________________________________________________________________________________
             Disclaimer: This schedule provides recommended dates for immunizations for your child based on date of birth. Your pediatrician
             may update due dates or add/remove vaccines from this schedule.Vaccs.io or its management or staff holds no responsibility on any loss or damage due to any vaccine given to child at any given timeOfSending.";
            footer = footer.Replace(Environment.NewLine, String.Empty).Replace("  ", String.Empty);
            Font georgia = FontFactory.GetFont("georgia", 7f);

            Chunk beginning = new Chunk(footer, georgia);

            PdfPTable tabFot = new PdfPTable(1);
            PdfPCell cell;
            tabFot.SetTotalWidth(new float[] { 575f });
            tabFot.DefaultCell.HorizontalAlignment = Element.ALIGN_LEFT;
            cell = new PdfPCell(new Phrase(beginning));
            cell.Border = 0;
            tabFot.AddCell(cell);
            tabFot.WriteSelectedRows(0, -1, 10, 50, writer.DirectContent);
        }

        //write on close of document
        public override void OnCloseDocument(PdfWriter writer, Document document)
        {
            base.OnCloseDocument(writer, document);
        }
    }
}
