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
// using iTextSharp.text;
// using iTextSharp.text.pdf;
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
            var dbChilds = _db.Childs.ToList();
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
                    childDB.ChildVaccines.Clear();
                    foreach(VaccineDTO vaccineDTO in childDTO.ChildVaccines) {
                        childDB.ChildVaccines.Add(_db.Vaccines.Where(x=>x.Id==vaccineDTO.Id).FirstOrDefault());
                    }
                    _db.Childs.Add(childDB);
                    _db.SaveChanges();
                }
                else
                {
                    Child existingChild = _db.Childs.FirstOrDefault(x => x.Name.Equals(childDTO.Name) && x.UserId == user.Id);
                     if (existingChild != null)
                         throw new Exception("Children with same name & number already exists. Parent should login and start change doctor process.");
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
                    var dbDose = _db.Doses.Where(x => x.Id == ds.DoseId).FirstOrDefault();
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
                                //     cvd.GivenDate = childDB.DOB;
                            }
                            else if (
                              ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 1", StringComparison.OrdinalIgnoreCase)
                              || ds.Dose.Name.Equals("Pneumococcal # 1", StringComparison.OrdinalIgnoreCase)
                              || ds.Dose.Name.Equals("Rota Virus GE # 1", StringComparison.OrdinalIgnoreCase)
                              )
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                //     DateTime d = childDB.DOB;
                                //    cvd.GivenDate = d.AddDays(42);
                            }
                            else if (
                            ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 2", StringComparison.OrdinalIgnoreCase)
                            || ds.Dose.Name.Equals("Pneumococcal # 2", StringComparison.OrdinalIgnoreCase)
                            || ds.Dose.Name.Equals("Rota Virus GE # 2", StringComparison.OrdinalIgnoreCase)
                              )
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                //      DateTime d = childDB.DOB;
                                //     cvd.GivenDate = d.AddDays(70);
                            }
                            else if (
                            ds.Dose.Name.Equals("OPV/IPV+HBV+DPT+Hib # 3", StringComparison.OrdinalIgnoreCase)
                            || ds.Dose.Name.Equals("Pneumococcal # 3", StringComparison.OrdinalIgnoreCase)
                            )
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                //         DateTime d = childDB.DOB;
                                //       cvd.GivenDate = d.AddDays(98);
                            }
                            else if (
                           ds.Dose.Name.Equals("Measles # 1", StringComparison.OrdinalIgnoreCase)
                           )
                            {
                                cvd.IsDone = true;
                                cvd.Due2EPI = true;
                                //       DateTime d = childDB.DOB;
                                //       cvd.GivenDate = d.AddMonths(9);
                            }
                        }

                        //         cvd.Date = calculateDate(childDTO.DOB, ds.GapInDays);

                        _db.Schedules.Add(cvd);
                        _db.SaveChanges();
                    }
                }
                Child c = _db.Childs.Include("Clinic").Where(x => x.Id == childDTO.Id).FirstOrDefault();
                if (c.Email != "")
                    UserEmail.ParentEmail(c);

                // generate SMS and save it to the db
                UserSMS.ParentSMS(c);

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
                var dbFollowUps = _db.FollowUps
                    .Where(f => f.DoctorId == followUpDto.DoctorId && f.ChildId == followUpDto.ChildId)
                    .OrderByDescending(x => x.CurrentVisitDate).ToList();
                List<FollowUpDTO> followUpDTOs = Mapper.Map<List<FollowUpDTO>>(dbFollowUps);
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

        [HttpPost("Download-Invoice-PDF")]
        public HttpResponseMessage DownloadInvoicePDF(ChildDTO childDTO)
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

            var dbDoctor = _db.Doctors.Where(x => x.Id == childDTO.DoctorId).FirstOrDefault();
            dbDoctor.InvoiceNumber = (dbDoctor.InvoiceNumber > 0) ? dbDoctor.InvoiceNumber + 1 : 1;
            var dbChild = _db.Childs.Include("Clinic").Where(x => x.Id == childDTO.Id).FirstOrDefault();
            var dbSchedules = _db.Schedules.Include("Dose").Include("Brand").Where(x => x.ChildId == childDTO.Id).ToList();
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
            upperTable.AddCell(CreateCell("Date: " + DateTime.UtcNow.AddHours(5), "", 1, "right", "description"));
            upperTable.AddCell(CreateCell(dbDoctor.AdditionalInfo, "", 1, "left", "description"));
            upperTable.AddCell(CreateCell("Bill To: " + dbChild.Name, "bold", 1, "right", "description"));

            upperTable.AddCell(CreateCell(dbChild.Clinic.Name, "", 1, "left", "description"));

            //upperTable.AddCell(CreateCell("Clinic Ph: " + dbChild.Clinic.PhoneNumber, "noColor", 1, "left", "description"));

            upperTable.AddCell(CreateCell("", "", 1, "right", "description"));


            if (childDTO.IsConsultationFee)
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
            if (childDTO.IsBrand)
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
            if (childDTO.IsBrand)
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
                        if (childDTO.IsBrand)
                        {
                            table.AddCell(CreateCell(schedule.Brand.Name, "", 1, "center", "invoiceRecords"));
                        }
                        var brandAmount = _db.BrandAmounts.Where(x => x.BrandId == schedule.BrandId && x.DoctorId == childDTO.DoctorId).FirstOrDefault();
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
            if (childDTO.IsConsultationFee)
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
             var httpPostedSignatureImage = HttpContext.Request.Form.Files["SignatureImage"];
             var imgPath = Path.Combine(_host.WebRootPath, "Content/UserImages", httpPostedSignatureImage.FileName);
            using (var fileStream = new FileStream(imgPath, FileMode.Create)) 
             httpPostedSignatureImage.CopyToAsync(fileStream);
             dbDoctor.SignatureImage = httpPostedSignatureImage.FileName;

            var signatureImage = dbDoctor.SignatureImage;
            if (signatureImage == null)
            {
                signatureImage = "avatar.png";
            }
            Image img = Image.GetInstance(imgPath + "\\" + signatureImage);

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
            return new HttpResponseMessage
            {
                Content = new StreamContent(stream)
                {
                    Headers =
                            {
                                ContentType = new MediaTypeHeaderValue("application/pdf"),
                                ContentDisposition = new ContentDispositionHeaderValue("attachment")
                                {
                                    FileName = childName.Replace(" ","") +"_Invoice"+"_"+DateTime.UtcNow.AddHours(5).Date.ToString("MMMM-dd-yyyy")+".pdf"
                                }
                            }
                },
                StatusCode = HttpStatusCode.OK
            };
        }


        [HttpPut]
        public Response<ChildDTO> Put([FromBody] ChildDTO childDTO)
        {


            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            childDTO.Name = textInfo.ToTitleCase(childDTO.Name);
            childDTO.FatherName = textInfo.ToTitleCase(childDTO.FatherName);

            {
                var dbChild = _db.Childs.FirstOrDefault(c => c.Id == childDTO.Id);
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
    }
}
