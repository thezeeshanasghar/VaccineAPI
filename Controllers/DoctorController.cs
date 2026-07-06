using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DoctorController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        private readonly IWebHostEnvironment _host;
        public DoctorController(Context context, IMapper mapper, IWebHostEnvironment host)
        {
            _db = context;
            _mapper = mapper;
            _host = host;
        }


        [HttpGet]
        public async Task<Response<List<DoctorDTO>>> GetAll()
        {
            var list = await _db.Doctors.OrderBy(x => x.Id).ToListAsync();
            List<DoctorDTO> listDTO = _mapper.Map<List<DoctorDTO>>(list);

            return new Response<List<DoctorDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<DoctorDTO>> GetSingle(long id)
        {

            var dbdoctor = await _db.Doctors.Where(x => x.Id == id).Include(x => x.User).Include(x => x.Clinics).FirstOrDefaultAsync();
            DoctorDTO doctorDTO = _mapper.Map<DoctorDTO>(dbdoctor);

            if (dbdoctor == null)
                return new Response<DoctorDTO>(false, "Not Found", null);
            doctorDTO.MobileNumber = dbdoctor.User.MobileNumber;

            return new Response<DoctorDTO>(true, null, doctorDTO);

        }

        [HttpGet("user/{id}")]
        public async Task<Response<DoctorDTO>> GetSinglebyuser(long id)
        {

            var dbdoctor = await _db.Doctors.Where(x => x.UserId == id).Include(x => x.User).FirstOrDefaultAsync();
            DoctorDTO doctorDTO = _mapper.Map<DoctorDTO>(dbdoctor);

            if (dbdoctor == null)
                return new Response<DoctorDTO>(false, "Not Found", null);
            doctorDTO.MobileNumber = dbdoctor.User.MobileNumber;

            return new Response<DoctorDTO>(true, null, doctorDTO);

        }

        [HttpGet("{id}/clinics")]
        public Response<IEnumerable<ClinicDTO>> GetAllClinicsOfaDoctor(int id)
        {
            var doctor = _db.Doctors.Include(x => x.Clinics).ThenInclude(x => x.ClinicTimings).Include(x => x.Childs).FirstOrDefault(c => c.Id == id);
            if (doctor == null)
                return new Response<IEnumerable<ClinicDTO>>(false, "Doctor not found", null);
            else
            {
                var dbClinics = _db.Clinics.Include(x => x.Childs).Where(x => x.DoctorId == doctor.Id).ToList();
                // var dbClinics = doctor.Clinics.ToList();
                List<ClinicDTO> clinicDTOs = new List<ClinicDTO>();
                foreach (var clinic in dbClinics)
                {
                    ClinicDTO clinicDTO = _mapper.Map<ClinicDTO>(clinic);
                    clinicDTO.childrenCount = clinic.Childs.Count();
                    clinicDTOs.Add(clinicDTO);
                }
                //var clinicDTOs = Mapper.Map<List<ClinicDTO>>(dbClinics);
                return new Response<IEnumerable<ClinicDTO>>(true, null, clinicDTOs);
            }
        }

        [HttpGet("/forget/{email}")]
        public ActionResult<DoctorDTO> GetDoctorDetailsByEmail(string email)
        {

            var doctor = _db.Doctors.FirstOrDefault(d => d.Email == email);
            if (doctor == null)
            {
                return NotFound();
            }
            var userDetails = _db.Users.FirstOrDefault(u => u.Id == doctor.UserId);
            if (userDetails != null)
            {

                var body = "Hi " + doctor.FirstName + ",\n"
                + "Welcome to vaccinationcentre.com\n\n"
                + "Your account credentials are:\n"
                + "ID/Mobile Number: " + userDetails.MobileNumber + "\n"
                + "Password: " + userDetails.Password + "\n"
                + "Web Link: https://doctor.vaccinationcentre.com";
                try
                {
                    UserEmail.SendEmail(doctor.Email, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error sending email: " + ex.Message);

                    // Return a 500 status code
                    return StatusCode(500, ex.Message);
                }
            }
            return Ok();
        }

        [HttpPost]
        public Response<DoctorDTO> Post(DoctorDTO doctorDTO)
        {
            // Check if the phone number exists in either Users or Doctors table
            var existingUserWithPhone = _db.Users.FirstOrDefault(x => x.MobileNumber == doctorDTO.MobileNumber);
            var existingDoctorWithPhone = _db.Doctors.FirstOrDefault(d => d.PhoneNo == doctorDTO.PhoneNo);
            var existingDoctorWithEmail = _db.Doctors.FirstOrDefault(d => d.Email == doctorDTO.Email);

            if ((existingUserWithPhone != null || existingDoctorWithPhone != null) && existingDoctorWithEmail != null)
            {
                return new Response<DoctorDTO>(false, "Both phone number and email are already in use. Please try different ones.", null);
            }
            else if (existingDoctorWithPhone != null)
            {
                return new Response<DoctorDTO>(false, "Phone number is already in use. Please try a different phone number.", null);
            }
            else if (existingDoctorWithEmail != null)
            {
                return new Response<DoctorDTO>(false, "Email already exists. Please try another email.", null);
            }

            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

            if (!string.IsNullOrEmpty(doctorDTO.DisplayName))
                doctorDTO.FirstName = textInfo.ToTitleCase(doctorDTO.DisplayName);
            if (!string.IsNullOrEmpty(doctorDTO.DisplayName))
                doctorDTO.DisplayName = textInfo.ToTitleCase(doctorDTO.DisplayName);
            if (!string.IsNullOrEmpty(doctorDTO.Qualification))
                doctorDTO.Qualification = textInfo.ToTitleCase(doctorDTO.Qualification);
            
            {
                // 2- save User first
                User userDB = new User();
                userDB.MobileNumber = doctorDTO.MobileNumber;
                userDB.Password = doctorDTO.Password;
                userDB.CountryCode = doctorDTO.CountryCode;
                userDB.UserType = "DOCTOR";
                _db.Users.Add(userDB);
                _db.SaveChanges();
                // 2- save Doctor 
                Doctor doctorDB = _mapper.Map<Doctor>(doctorDTO);
                doctorDB.ValidUpto = DateTime.Now.AddDays(30);
                doctorDB.ProfileImage = "";
                // doctorDB.SignatureImage = "";
                doctorDB.UserId = userDB.Id;
                doctorDB.AllowInvoice = true;
                doctorDB.AllowFollowUp = true;
                doctorDB.AllowChart = true;
                doctorDB.AllowInventory = true;
                _db.Doctors.Add(doctorDB);
                _db.SaveChanges();
                doctorDTO.Id = doctorDB.Id;

                // var vaccines = _db.Vaccines.Include(x => x.Brands).ToList();
                // bool brandamount = _db.BrandAmounts.Any(x => x.DoctorId == doctorDTO.Id);
                // if (brandamount == false)
                // {
                //     foreach (var vaccine in vaccines)
                //     {
                //         var brands = vaccine.Brands;
                //         foreach (var brand in brands)
                //         {
                //             BrandAmount ba = new BrandAmount();
                //             ba.Amount = 0;
                //             ba.DoctorId = doctorDB.Id;
                //             ba.Count = 0;
                //             ba.BrandId = brand.Id;
                //             _db.BrandAmounts.Add(ba);
                //             _db.SaveChanges();
                //         }
                //     }
                // }

                var body = "Hi " + doctorDTO.DisplayName + ",\n"
                    + "You are successfully registered in vaccinationcentre.com\n\n"
                    + "Your account credentials are:\n"
                    + "ID/Mobile Number: " + doctorDTO.MobileNumber + "\n"
                    + "Password: " + doctorDTO.Password + "\n"
                    + "Web Link: https://doctor.vaccinationcentre.com";
                UserEmail.SendEmail(doctorDTO.Email, body);
            }
            return new Response<DoctorDTO>(true, null, doctorDTO);
        }

        [HttpPost("{id}/update-images")]
        public Response<DoctorDTO> UpdateUploadedImages(int id)
        {
            var dbDoctor = _db.Doctors.Where(d => d.Id == id).FirstOrDefault();
            if (dbDoctor == null)
            {
                return new Response<DoctorDTO>(false, "Doctor not found", null);
            }

            if (HttpContext.Request.Form.Files.Any())
            {
                var httpPostedProfileImage = HttpContext.Request.Form.Files["ProfileImage"];
                // var httpPostedSignatureImage = HttpContext.Request.Form.Files["SignatureImage"];
                if (httpPostedProfileImage != null)
                {
                    var fileSavePath = Path.Combine(_host.ContentRootPath, "Content/UserImages", httpPostedProfileImage.FileName);
                    using (var fileStream = new FileStream(fileSavePath, FileMode.Create))
                        httpPostedProfileImage.CopyToAsync(fileStream);
                    dbDoctor.ProfileImage = httpPostedProfileImage.FileName;
                }

                // if (httpPostedSignatureImage != null)
                // {
                //     var fileSavePath = Path.Combine(_host.ContentRootPath, "Content/UserImages", httpPostedSignatureImage.FileName);
                //     using (var fileStream = new FileStream(fileSavePath, FileMode.Create))
                //         httpPostedSignatureImage.CopyToAsync(fileStream);
                //     dbDoctor.SignatureImage = httpPostedSignatureImage.FileName;
                // }
                _db.SaveChanges();
                return new Response<DoctorDTO>(true, null, null);
            }

            return new Response<DoctorDTO>(false, "invalid files in request", null);
        }

        [HttpPut("{id}")]
        public Response<DoctorDTO> Put(int Id, DoctorDTO doctorDTO)
        {

            var dbDoctor = _db.Doctors.Where(c => c.Id == Id).FirstOrDefault();
            if (dbDoctor == null)
            {
                return new Response<DoctorDTO>(false, "Doctor not found", null);
            }
            dbDoctor.FirstName = doctorDTO.FirstName;
            dbDoctor.DisplayName = doctorDTO.DisplayName;
            dbDoctor.Email = doctorDTO.Email;
            dbDoctor.PMDC = doctorDTO.PMDC;
            dbDoctor.PhoneNo = doctorDTO.PhoneNo;
            dbDoctor.ShowPhone = doctorDTO.ShowPhone;
            dbDoctor.ShowMobile = doctorDTO.ShowMobile;
            dbDoctor.Qualification = doctorDTO.Qualification;
            dbDoctor.AdditionalInfo = doctorDTO.AdditionalInfo;
            dbDoctor.ProfileImage = doctorDTO.ProfileImage;
            // dbDoctor.SignatureImage = doctorDTO.SignatureImage;

            //dbDoctor = Mapper.Map<DoctorDTO, Doctor>(doctorDTO, dbDoctor);
            //entities.Entry<Doctor>(dbDoctor).State = System.Data.Entity.EntityState.Modified;
            _db.SaveChanges();
            return new Response<DoctorDTO>(true, null, doctorDTO);

        }

        [HttpPut("{id}/update-permission")]
        public Response<DoctorDTO> UpdatePermissions(int Id, DoctorDTO doctorDTO)
        {
            var dbDoctor = _db.Doctors.Where(c => c.Id == Id).FirstOrDefault();
            if (dbDoctor == null)
            {
                return new Response<DoctorDTO>(false, "Doctor not found", null);
            }

            dbDoctor.AllowInvoice = doctorDTO.AllowInvoice;
            dbDoctor.AllowFollowUp = doctorDTO.AllowFollowUp;
            dbDoctor.AllowChart = doctorDTO.AllowChart;
            dbDoctor.AllowInventory = doctorDTO.AllowInventory;
            dbDoctor.AllowSupplier  = doctorDTO.AllowInventory;
            dbDoctor.AllowFinancial = doctorDTO.AllowFinancial;
            dbDoctor.AllowSalesReport = doctorDTO.AllowSalesReport;
            dbDoctor.AllowAgent     = doctorDTO.AllowAgent;
            dbDoctor.AllowTravel    = doctorDTO.AllowTravel;
            dbDoctor.AllowAdult     = doctorDTO.AllowAdult;
            dbDoctor.AllowDevelopmentalAssessment = doctorDTO.AllowDevelopmentalAssessment;
            dbDoctor.AllowHomeBooking  = doctorDTO.AllowHomeBooking;
            dbDoctor.AllowClinicBooking = doctorDTO.AllowClinicBooking;
            dbDoctor.AllowAnalytics     = doctorDTO.AllowAnalytics;
            dbDoctor.AllowAssistant     = doctorDTO.AllowAssistant;
            _db.SaveChanges();
            //  dbDoctor = _mapper.Map<DoctorDTO, Doctor>(doctorDTO, dbDoctor);
            return new Response<DoctorDTO>(true, null, doctorDTO);
        }

        [HttpPut("{id}/validUpto")]
        public Response<DoctorDTO> ChangeValidity(int Id, DoctorDTO doctorDTO)
        {
            var dbDoctor = _db.Doctors.Where(x => x.Id == Id).FirstOrDefault();
            if (dbDoctor == null)
            {
                return new Response<DoctorDTO>(false, "Doctor not found", null);
            }
            dbDoctor.ValidUpto = doctorDTO.ValidUpto;
            _db.SaveChanges();

            DoctorDTO doctorDTOs = _mapper.Map<DoctorDTO>(dbDoctor);
            return new Response<DoctorDTO>(true, null, doctorDTOs);
        }

         [HttpGet("{id}/{currentPage}/childs/")]
        public Response<IEnumerable<ChildDTO>> GetAllChildsOfaDoctor(int id, int currentPage, [FromQuery] string searchKeyword)
        {
            {
                var doctor = _db.Doctors.Include(x => x.Clinics).Where(c => c.Id == id).FirstOrDefault();
                if (doctor == null)
                    return new Response<IEnumerable<ChildDTO>>(false, "Doctor not found", null);
                else
                {
                    List<ChildDTO> childDTOs = new List<ChildDTO>();
                    // var doctorClinics = doctor.Clinics;
                    var doctorClinics = _db.Clinics.Include(x => x.Childs).Where(x => x.DoctorId == doctor.Id).ToList();

                   foreach (var clinic in doctorClinics)
                    {
                        var doctorChilds = _db
                            .Childs.Include(x => x.User)
                            .Where(x => x.ClinicId == clinic.Id)
                            .ToList();
                        if (!String.IsNullOrEmpty(searchKeyword))
                        {
                            // Normalize the search keyword
                            searchKeyword = NormalizePhoneNumber(searchKeyword);

                            childDTOs.AddRange(
                                _mapper.Map<List<ChildDTO>>(
                                    clinic
                                        .Childs.Where(x =>
                                            x.Name.Trim()
                                                .ToLower()
                                                .Contains(searchKeyword.ToLower())
                                            || x.FatherName.Trim()
                                                .ToLower()
                                                .Contains(searchKeyword.ToLower())
                                            || x.Email.Trim().Contains(searchKeyword.ToLower())
                                            || NormalizePhoneNumber(
                                                    x.User.CountryCode + x.User.MobileNumber
                                                )
                                                .Contains(searchKeyword) // Normalize phone number
                                        )
                                        .ToList<Child>()
                                )
                            );
                        }
                        else
                        {
                            childDTOs.AddRange(
                                _mapper.Map<List<ChildDTO>>(clinic.Childs.ToList<Child>())
                            );
                        }
                    }
                    foreach (var item in childDTOs)
                    {
                        var dbChild = _db.Childs.Where(x => x.Id == item.Id).Include(x => x.User).FirstOrDefault();
                        if (dbChild?.User != null)
                        {
                            item.MobileNumber = dbChild.User.CountryCode + dbChild.User.MobileNumber;
                        }
                    }
                    return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs.OrderByDescending(x => x.Id).ToList().Skip(15 * currentPage).Take(15));
                }
            }
        }

        private string NormalizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            phoneNumber = phoneNumber.Trim();
            if (phoneNumber.StartsWith("+"))
                phoneNumber = phoneNumber.Substring(1);
            if (phoneNumber.StartsWith("00"))
                phoneNumber = phoneNumber.Substring(2);
            if (phoneNumber.StartsWith("0"))
                phoneNumber = phoneNumber.Substring(1);

            return phoneNumber;
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id)
        {
            {
                var dbDoctor = _db.Doctors.Include(x => x.User).Include(x => x.DoctorSchedules).Include(x => x.FollowUps)
                    .Include(x => x.Clinics).ThenInclude(x => x.ClinicTimings).Include(x => x.Clinics).ThenInclude(x => x.Childs).Where(c => c.Id == Id).FirstOrDefault();
                if (dbDoctor == null)
                {
                    return new Response<string>(false, "Doctor not found", null);
                }
                foreach (var clinic in dbDoctor.Clinics)
                {
                    foreach (var child in clinic.Childs)
                    {
                        var dbChild = _db.Childs.Where(x => x.Id == child.Id).Include(x => x.Schedules).Include(x => x.User).ThenInclude(x => x.Childs).Include(x => x.FollowUps).FirstOrDefault();
                        if (dbChild == null)
                        {
                            continue;
                        }
                        _db.Schedules.RemoveRange(dbChild.Schedules);
                        _db.FollowUps.RemoveRange(dbChild.FollowUps);
                        if (dbChild.User != null && dbChild.User.Childs.Count == 1)
                            _db.Users.Remove(dbChild.User);
                        _db.Childs.Remove(dbChild);
                    }
                    _db.ClinicTimings.RemoveRange(clinic.ClinicTimings);
                }
                _db.DoctorSchedules.RemoveRange(dbDoctor.DoctorSchedules);
                _db.Clinics.RemoveRange(dbDoctor.Clinics);
                if (dbDoctor.User != null)
                {
                    _db.Users.Remove(dbDoctor.User);
                }
                _db.Doctors.Remove(dbDoctor);
                _db.SaveChanges();
                return new Response<string>(true, "Doctor is deleted successfully", null);
            }
        }

        [HttpGet("{id}/appointments")]
        public async Task<ActionResult<string>> GetDoctorAppointmentsWithinDateRange(long id, DateTime fromDate, DateTime toDate)
        {
            // Query the database to find the clinics associated with the provided doctor ID
            var clinics = await _db.Clinics.Where(c => c.DoctorId == id).ToListAsync();

            // Print the provided parameters and clinic data
            string result = $"Doctor ID: {id}, From Date: {fromDate}, To Date: {toDate}\n";
            foreach (var clinic in clinics)
            {
                result += $"Clinic ID: {clinic.Id}, Clinic Name: {clinic.Name}\n";
            }

            // Return the result
            return result;
        }

        [HttpGet("{id}/children/{childId}/schedules")]
        public async Task<ActionResult<IEnumerable<Schedule>>> GetSchedulesForChild(long id, long childId, DateTime fromDate, DateTime toDate)
        {
            try
            {
                // Query the database to find the schedules for the specified child ID within the provided date range
                var schedules = await _db.Schedules
                    .Where(s => s.ChildId == childId && s.Date >= fromDate && s.Date <= toDate)
                    .ToListAsync();

                if (schedules == null || !schedules.Any())
                {
                    return NotFound($"No schedules found for child ID {childId} within the provided date range");
                }

                return Ok(schedules);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while retrieving schedules for child ID {childId}: {ex.Message}");
            }
        }

        [HttpPatch]
        [Route("/update_date_for_Vacations")]
        public async Task<IActionResult> UpdateSchedulesForChild(long childId, [FromQuery] string fromDate, [FromQuery] string toDate)
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);

                // Fetch schedules for the specified child ID
                var schedules = await _db.Schedules
                    .Where(s => s.ChildId == childId && s.Date >= parsedFromDate && s.Date <= parsedToDate)
                    .ToListAsync();

                if (schedules == null || !schedules.Any())
                {
                    return NotFound($"No schedules found for child ID {childId} to update");
                }

                // Update the dates in the fetched schedules
                var updatedDate = parsedToDate.AddDays(1);
                foreach (var schedule in schedules)
                {
                    schedule.Date = updatedDate;
                }

                // Save changes to the database
                await _db.SaveChangesAsync();

                return Ok("Schedules updated successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while updating schedules for child ID {childId}: {ex.Message}");
            }
        }
        [HttpGet("allDoc")]
        public async Task<Response<List<DoctorDTO>>> GetDoc()
        {

            var dbdoctor = await _db.Doctors.Include(x => x.User).Include(x => x.Clinics).ToListAsync();
            List<DoctorDTO> doctorDTO = _mapper.Map<List<DoctorDTO>>(dbdoctor);

            if (dbdoctor == null)
                return new Response<List<DoctorDTO>>(false, "Not Found", null);

            return new Response<List<DoctorDTO>>(true, null, doctorDTO);
        }

        [HttpPatch("update-clinic-id")]
        public async Task<ActionResult<Response<string>>> UpdateClinicIdForChild(
            [FromQuery] int? doctorId,
            [FromQuery] long childId,
            [FromQuery] long? clinicId
        )
        {
            try
            {

                var child = await _db.Childs.FindAsync(childId);
                if (child == null)
                    return NotFound(new Response<string>(false, $"Child with ID {childId} not found", null));

                Clinic clinic;
                if (clinicId.HasValue && clinicId.Value > 0)
                {
                    clinic = await _db.Clinics.FirstOrDefaultAsync(x => x.Id == clinicId.Value);
                    if (clinic == null)
                        return NotFound(new Response<string>(false, $"Clinic with ID {clinicId.Value} not found", null));
                }
                else
                {
                    if (!doctorId.HasValue || doctorId.Value <= 0)
                        return BadRequest(new Response<string>(false, "Either clinicId or doctorId is required", null));

                    clinic = await _db.Clinics.FirstOrDefaultAsync(x => x.DoctorId == doctorId.Value);
                    if (clinic == null)
                        return NotFound(new Response<string>(false, $"Clinic with Doctor ID {doctorId.Value} not found", null));
                }

                child.ClinicId = clinic.Id;

                await _db.SaveChangesAsync();

                return Ok(new Response<string>(true, null, "Clinic ID updated successfully"));
            }
            catch (DbUpdateException dbEx)
            {
                return StatusCode(500, new Response<string>(
                    false,
                    $"An error occurred while updating clinic ID for child ID {childId}: {dbEx.InnerException?.Message}",
                    null
                ));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new Response<string>(
                    false,
                    $"An error occurred while updating clinic ID for child ID {childId}: {ex.Message}",
                    null
                ));
            }
        }

        [HttpGet("with-clinics")]
        public async Task<Response<List<DoctorDTO>>> GetDoctorsWithClinics()
        {
            try
            {
                var doctors = await _db.Doctors
                    .Include(x => x.Clinics)
                    .Where(x => x.Clinics.Any())  // Only get doctors that have clinics
                    .OrderBy(x => x.Id)
                    .ToListAsync();

                if (doctors == null || !doctors.Any())
                {
                    return new Response<List<DoctorDTO>>(false, "No doctors found with clinics", null);
                }

                List<DoctorDTO> doctorDTOs = _mapper.Map<List<DoctorDTO>>(doctors);
                return new Response<List<DoctorDTO>>(true, null, doctorDTOs);
            }
            catch (Exception ex)
            {
                return new Response<List<DoctorDTO>>(false, $"An error occurred while fetching doctors: {ex.Message}", null);
            }
        }
    }
}