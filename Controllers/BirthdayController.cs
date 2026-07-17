using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using System.Text;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Globalization;
using CsvHelper;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BirthdayController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public BirthdayController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("birthdaymail/{childId}")]
        public Response<object> SendBirthdayEmailByChildId(long childId)
        {
            try
            {
                var child = _db.Childs
                    .Include(c => c.User)
                    .Include(c => c.Clinic)
                        .ThenInclude(c => c.Doctor)
                    .FirstOrDefault(c => c.Id == childId);

                if (child == null)
                {
                    return new Response<object>(false, "Child not found.", null);
                }

                if (string.IsNullOrEmpty(child.Email))
                {
                    return new Response<object>(false, "No email address found for the child.", null);
                }

                var emailTo = child.Email;
                var today = DateTime.Today;
                var age = today.Year - child.DOB.Year;
                string emailBody = $@"Dear {child.Name},

🎉 Happy {age}{GetOrdinalSuffix(age)} Birthday! 🎂

We hope your special day is filled with joy, laughter, and wonderful memories!

From,
{child.Clinic.Doctor.DisplayName}
{child.Clinic.Name}

Stay healthy and keep smiling! 😊

Best wishes from all of us at {child.Clinic.Name}

Note: This is an automated birthday wish. For any medical queries, please contact the clinic directly.
Contact: {child.Clinic.PhoneNumber}
Website: https://vaccinationcentre.com";

                try
                {
                    UserEmail.SendEmail(emailTo, emailBody, $"Happy {age}{GetOrdinalSuffix(age)} Birthday, {child.Name}!");

                    return new Response<object>(true,
                        "Birthday email sent successfully.",
                        new
                        {
                            ChildId = child.Id,
                            Name = child.Name,
                            Email = emailTo,
                            Age = age,
                            ClinicName = child.Clinic.Name
                        });
                }
                catch (Exception ex)
                {
                    return new Response<object>(
                        false,
                        $"Failed to send birthday email: {ex.Message}",
                        new { ChildId = child.Id, Name = child.Name }
                    );
                }
            }
            catch (Exception ex)
            {
                return new Response<object>(
                    false,
                    $"Error processing birthday email: {ex.Message}",
                    null
                );
            }
        }

        [HttpGet("birthdaymail/bulk/{doctorId}")]
        public Response<object> SendBulkBirthdayEmails(long doctorId)
        {
            try
            {
                var today = DateTime.Today;
                var children = _db
                    .Childs.Include(c => c.User)
                    .Include(c => c.Clinic)
                    .ThenInclude(c => c.Doctor)
                    .Where(c =>
                        c.DOB.Month == today.Month
                        && c.DOB.Day == today.Day
                        && c.Clinic.DoctorId == doctorId
                        && c.IsInactive == false
                        && !string.IsNullOrEmpty(c.Email)
                    ) 
                    .ToList();

                if (!children.Any())
                {
                    return new Response<object>(false, "No birthdays found for today.", null);
                }

                var emailsSent = new List<object>();

                foreach (var child in children)
                {
                    var emailTo = child.Email;
                    var age = today.Year - child.DOB.Year;
                    string emailBody =
                        $@"Dear {child.Name},

🎉 Happy {age}{GetOrdinalSuffix(age)} Birthday! 🎂

We hope your special day is filled with joy, laughter, and wonderful memories!

From,
{child.Clinic.Doctor.DisplayName}
{child.Clinic.Name}

Stay healthy and keep smiling! 😊

Best wishes from all of us at {child.Clinic.Name}

Note: This is an automated birthday wish. For any medical queries, please contact the clinic directly.
Contact: {child.Clinic.PhoneNumber}
Website: https://vaccinationcentre.com";

                    try
                    {
                        UserEmail.SendEmail(
                            emailTo,
                            emailBody,
                            $"Happy {age}{GetOrdinalSuffix(age)} Birthday, {child.Name}!"
                        );

                        emailsSent.Add(
                            new
                            {
                                ChildId = child.Id,
                                Name = child.Name,
                                Email = emailTo,
                                Age = age,
                                ClinicName = child.Clinic.Name,
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email to {child.Name}: {ex.Message}");
                    }
                }

                if (!emailsSent.Any())
                {
                    return new Response<object>(false, "No emails were sent.", null);
                }

                return new Response<object>(
                    true,
                    "Bulk birthday emails sent successfully.",
                    emailsSent
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in SendBulkBirthdayEmails: " + ex.Message);
                return new Response<object>(
                    false,
                    "An error occurred while processing the request.",
                    null
                );
            }
        }

        [HttpGet("export-birthdays-csv")]
        public IActionResult ExportBirthdaysToCsv([FromQuery(Name = "arr[]")] long[] arr)
        {
            try
            {
                if (arr == null || !arr.Any())
                {
                    return BadRequest("No child IDs provided.");
                }
                List<Child> allChildren = new List<Child>();
                foreach (long id in arr)
                {
                    var children = _db
                        .Childs.Where(f => f.Id == id)
                        .Include(c => c.User)
                        .Include(c => c.Clinic)
                        .ThenInclude(c => c.Doctor)
                        .Where(c =>
                            c.Id == id && !string.IsNullOrEmpty(c.Email)
                        )
                        .ToList();

                    allChildren.AddRange(children);
                }
                if (!allChildren.Any())
                {
                    return NotFound("No children found for the provided IDs.");
                }
                var stream = new MemoryStream();
                using (var writeFile = new StreamWriter(stream, Encoding.UTF8, 512, true))
                {
                    var csv = new CsvWriter(writeFile, CultureInfo.InvariantCulture);
                    var birthdayCsvData = allChildren.Select(c => new BirthdayCsvDTO
                    {
                        ChildName = c.Name ?? "N/A",
                        FatherName = c.FatherName ?? "N/A",
                        DOB = c.DOB.ToString("yyyy-MM-dd") ?? "N/A",
                        ClinicName = c.Clinic?.Name ?? "N/A",
                        DoctorName = c.Clinic?.Doctor?.DisplayName ?? "N/A",
                        Age = DateTime.Today.Year - c.DOB.Year,
                        Phone = c.User?.MobileNumber ?? "N/A",
                        Email = c.Email ?? "N/A",
                    });
                    csv.WriteRecords(birthdayCsvData);
                }
                stream.Position = 0;
                return File(stream, "application/octet-stream", "Birthdays.csv");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error exporting birthdays to CSV: " + ex.Message);
                return StatusCode(500, "An error occurred while generating the CSV file.");
            }
        }

        [HttpGet("{doctorId}")]
        public Response<IEnumerable<ChildDTO>> GetBirthdayAlertByDoctor(DateTime inputDate,long doctorId)
        {
            List<Child> childs = _db
                .Childs.Include(c => c.User)
                .Include(c => c.Clinic)
                .ThenInclude(cl => cl.Doctor)
                .Where(c =>
                    c.DOB.Month == inputDate.Month
                    && c.DOB.Day == inputDate.Day
                    && c.Clinic.DoctorId == doctorId 
                    && c.IsInactive == false 
                )
                .ToList();

            IEnumerable<ChildDTO> childDTOs = _mapper.Map<IEnumerable<ChildDTO>>(childs);

            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }

        // Persists "birthday WhatsApp wish sent" for this year, so the tick badge on
        // birthday-alert.page.ts survives reload/logout-login. Annual, not one-time — the
        // frontend only treats the tick as current if LastBirthdayAlertSentAt falls within
        // the current year, so it naturally resets each birthday with no reset job needed.
        [HttpPost("{childId}/mark-alert-sent")]
        public Response<object> MarkBirthdayAlertSent(long childId)
        {
            var child = _db.Childs.FirstOrDefault(c => c.Id == childId);
            if (child == null)
            {
                return new Response<object>(false, "Child not found.", null);
            }
            child.LastBirthdayAlertSentAt = DateTime.UtcNow;
            _db.SaveChanges();
            return new Response<object>(true, null, new { child.Id, child.LastBirthdayAlertSentAt });
        }

        private static List<Child> GetBirthdayAlertData(int GapDays,long OnlineClinicId,Context db)
        {
            return db
                .Childs.Where(x =>
                    x.DOB.Date == DateTime.Today && x.IsInactive.HasValue.Equals(false)
                )
                .ToList<Child>();
        }

        private static string GetOrdinalSuffix(int number)
        {
            var lastDigit = number % 10;
            if (number >= 11 && number <= 13)
                return "th";

            return lastDigit switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }
    }
}
