using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

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
                // Get child with all required information
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

                // Create birthday email content
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

        [HttpGet("{doctorId}")]
        public Response<IEnumerable<ChildDTO>> GetBirthdayAlertByDoctor(
            DateTime inputDate,
            long doctorId
        )
        {
            // Filter records where DOB matches the input date (month and day) and DoctorId matches
            List<Child> childs = _db
                .Childs.Include(c => c.User) // Include User
                .Include(c => c.Clinic) // Include Clinic
                .ThenInclude(cl => cl.Doctor) // Include Doctor via Clinic
                .Where(c =>
                    c.DOB.Month == inputDate.Month
                    && c.DOB.Day == inputDate.Day
                    && c.Clinic.DoctorId == doctorId // Filter by DoctorId
                    && c.IsInactive == false // Filter out inactive records
                )
                .ToList();

            // Map entities to DTOs
            IEnumerable<ChildDTO> childDTOs = _mapper.Map<IEnumerable<ChildDTO>>(childs);

            // Return the response
            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }

        private static List<Child> GetBirthdayAlertData(
            int GapDays,
            long OnlineClinicId,
            Context db
        )
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
