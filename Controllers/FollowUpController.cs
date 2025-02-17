using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
using iTextSharp.text;
using iTextSharp.text.pdf;


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
            var list = await _db.FollowUps.OrderBy(x=>x.Id).ToListAsync();
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
        public Response<IEnumerable<FollowUpDTO>> GetFollowUpsByDoctor(
            long doctorId,
            DateTime inputDate
        )
        {
            List<FollowUp> followUps = _db
                .FollowUps.Include(f => f.Child) // Include related Child data
                .ThenInclude(c => c.User) // Include User data through Child
                .Where(f => f.DoctorId == doctorId) // Filter by DoctorId
                .Where(c => c.NextVisitDate == inputDate.Date)
                .OrderBy(x => x.Child.Id)
                .ToList();
            IEnumerable<FollowUpDTO> followUpDTOs = _mapper.Map<IEnumerable<FollowUpDTO>>(
                followUps
            );
            return new Response<IEnumerable<FollowUpDTO>>(true, null, followUpDTOs);
        }

         [HttpGet("alert/{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<FollowUpDTO>> GetAlert(DateTime inputDate, int GapDays, long OnlineClinicId)
        {
                {
                    var doctor = _db.Clinics.Where(x => x.Id == OnlineClinicId).Include(x=>x.Doctor).First<Clinic>().Doctor;
                    long[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<long>();
                  
                  //  int[] ClinicIDs = doctor.Clinics.Select(x => x.Id).ToArray<int>();

                    IEnumerable<FollowUp> followups = new List<FollowUp>();
                    DateTime AddedDateTime = DateTime.UtcNow.AddHours(5).AddDays(GapDays);
                    DateTime pakistanTime = DateTime.UtcNow.AddHours(5);
                    if (GapDays == 0)
                        followups = _db.FollowUps.Include(x=> x.Child).ThenInclude(x=>x.User)
                            .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                       //     .Where(c => System.Data.Entity.DbFunctions.TruncateTime(c.NextVisitDate) == System.Data.Entity.DbFunctions.TruncateTime(pakistanTime))
                           .Where(c => c.NextVisitDate == inputDate.Date)
                            .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate).ToList<FollowUp>();
                    else if (GapDays > 0)
                    {
                        AddedDateTime = AddedDateTime.AddDays(1);
                        followups = _db.FollowUps.Include(x=> x.Child).ThenInclude(x=>x.User)
                        
                            .Where(c => ClinicIDs.Contains(c.Child.ClinicId))
                            .Where(c => c.NextVisitDate > pakistanTime && c.NextVisitDate <= AddedDateTime)
                            .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate)
                            .ToList<FollowUp>();

                    }
                    else if (GapDays < 0)
                    {
                        followups = _db.FollowUps.Include(x=> x.Child).ThenInclude(x=>x.User)
                        //    .Where(c => ClinicIDs.Contains(c.Child.ClinicID))
                            .Where(c => c.NextVisitDate < pakistanTime.Date && c.NextVisitDate >= AddedDateTime)
                            .OrderBy(x => x.Child.Id).ThenBy(x => x.NextVisitDate)
                            .ToList<FollowUp>();
                    }
                        
                    IEnumerable<FollowUpDTO> followUpDTO = _mapper.Map<IEnumerable<FollowUpDTO>>(followups);
                    return new Response<IEnumerable<FollowUpDTO>>(true, null, followUpDTO);
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
            var followUpDetails = _db.FollowUps
                .Where(f => f.ChildId == childId)
                .OrderBy(f => f.NextVisitDate)
                .FirstOrDefault();

            if (followUpDetails == null)
            {
                return NotFound("Follow-up details not found for the child");
            }
            DateTime nextVisitDate = followUpDetails.NextVisitDate ?? DateTime.MinValue;
            string disease = followUpDetails.Disease;
            float weight = followUpDetails.Weight ?? 0;
            float height = followUpDetails.Height ?? 0;
            float ofc = followUpDetails.OFC ?? 0;

            var childDetails = _db.Childs
                .Include(c => c.Clinic)
                .ThenInclude(clinic => clinic.Doctor)
                .FirstOrDefault(c => c.Id == childId);

            if (childDetails == null)
            {
                return NotFound("Child not found");
            }
            string clinicName = childDetails.Clinic.Name;
            string doctorDetails = $"Dr {childDetails.Clinic.Doctor.DisplayName}\n{childDetails.Clinic.Doctor.Qualification}";
            string clinicAddress = childDetails.Clinic.Address;
            string childName = childDetails.Name;
            string fatherName = childDetails.FatherName;

            var output = new MemoryStream();
            var document = new Document();
            PdfWriter.GetInstance(document, output);
            document.Open();
            var headerTable = new PdfPTable(2);
            headerTable.WidthPercentage = 100;
            headerTable.SetWidths(new float[] { 3, 1 });

            PdfPCell headerCell = new PdfPCell();
            headerCell.Border = PdfPCell.NO_BORDER;
            headerCell.AddElement(new Paragraph(clinicName, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12)));
            headerCell.AddElement(new Paragraph(doctorDetails, FontFactory.GetFont(FontFactory.HELVETICA, 8)));
            headerCell.AddElement(new Paragraph(clinicAddress, FontFactory.GetFont(FontFactory.HELVETICA, 8)));
            headerTable.AddCell(headerCell);

            string logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Images", "clinicLogo.png");
            if (System.IO.File.Exists(logoPath))
            {
                var logo = Image.GetInstance(logoPath);
                logo.ScaleToFit(150f, 150f);
                PdfPCell logoCell = new PdfPCell(logo)
                {
                    Border = PdfPCell.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                headerTable.AddCell(logoCell);
            }
            else
            {
                headerTable.AddCell(new PdfPCell(new Phrase("No Logo Available", FontFactory.GetFont(FontFactory.HELVETICA, 10))) { Border = PdfPCell.NO_BORDER });
            }

            document.Add(headerTable);
            var childInfoHeading = new Paragraph("Child Info", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12))
            {
                SpacingBefore = 2, 
                Alignment = Element.ALIGN_CENTER 
            };
            document.Add(childInfoHeading);
            var childDetailsTable = new PdfPTable(2);
            childDetailsTable.WidthPercentage = 100;
            childDetailsTable.SpacingBefore = 10;

            childDetailsTable.AddCell(new PdfPCell(new Phrase("Child Name", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 5 
            });
            childDetailsTable.AddCell(new PdfPCell(new Phrase(childName, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 5 
            });

            childDetailsTable.AddCell(new PdfPCell(new Phrase("Father Name", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 5 
            });
            childDetailsTable.AddCell(new PdfPCell(new Phrase(fatherName, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 5 
            });

            document.Add(childDetailsTable);

            var followUpHeaderTable = new PdfPTable(2);
            followUpHeaderTable.WidthPercentage = 100;
            followUpHeaderTable.SpacingBefore = 10;
            var followUpDetailsHeading = new Paragraph("Next Follow Up Details", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10))
            {
                SpacingBefore = 20, 
                Alignment = Element.ALIGN_CENTER
            };
            document.Add(followUpDetailsHeading);

            followUpHeaderTable.AddCell(new PdfPCell(new Phrase("Next Visit", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT
            });
            followUpHeaderTable.AddCell(new PdfPCell(new Phrase(nextVisitDate.ToString("dd-MM-yyyy"), FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT
            });

            followUpHeaderTable.AddCell(new PdfPCell(new Phrase("Disease", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT
            });
            followUpHeaderTable.AddCell(new PdfPCell(new Phrase(disease, FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT
            });

            followUpHeaderTable.AddCell(new PdfPCell(new Phrase("Weight", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT
            });
            followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{weight} kg", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT
            });

            followUpHeaderTable.AddCell(new PdfPCell(new Phrase("Height", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT
            });
            followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{height} cm", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT
            });

            followUpHeaderTable.AddCell(new PdfPCell(new Phrase("OFC", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)))
            {
                BackgroundColor = BaseColor.LightGray,
                HorizontalAlignment = Element.ALIGN_LEFT
            });
            followUpHeaderTable.AddCell(new PdfPCell(new Phrase($"{ofc} cm", FontFactory.GetFont(FontFactory.HELVETICA, 10)))
            {
                BackgroundColor = BaseColor.White,
                HorizontalAlignment = Element.ALIGN_LEFT
            });

            document.Add(followUpHeaderTable);

            document.Close();
            output.Seek(0, SeekOrigin.Begin);
            return File(output, "application/pdf", "Follow-Up.pdf");
        }
    }
}
