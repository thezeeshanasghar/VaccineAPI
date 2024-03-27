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

        [HttpGet("approved")]
        public async Task<Response<List<DoctorDTO>>> GetApproved()
        {

            var dbdoctor = await _db.Doctors.Where(x => x.IsApproved == true).Include(x => x.User).Include(x => x.Clinics).ToListAsync();
            List<DoctorDTO> doctorDTO = _mapper.Map<List<DoctorDTO>>(dbdoctor);

            if (dbdoctor == null)
                return new Response<List<DoctorDTO>>(false, "Not Found", null);

            return new Response<List<DoctorDTO>>(true, null, doctorDTO);
        }

        [HttpGet("unapproved")]
        public async Task<Response<List<DoctorDTO>>> GetUnApproved()
        {

            var dbdoctor = await _db.Doctors.Where(x => x.IsApproved == false).Include(x => x.User).Include(x => x.Clinics).ToListAsync();
            List<DoctorDTO> doctorDTO = _mapper.Map<List<DoctorDTO>>(dbdoctor);

            if (dbdoctor == null)
                return new Response<List<DoctorDTO>>(false, "Not Found", null);

            return new Response<List<DoctorDTO>>(true, null, doctorDTO);
        }

        // [HttpPost]
        // public async Task<ActionResult<Doctor>> Post(Doctor Doctor)
        // {
        //     _db.Doctors.Update(Doctor);
        //     await _db.SaveChangesAsync();

        //   //  return CreatedAtAction(nameof(GetSingle), new { id = Doctor.Id }, Doctor);
        // }

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

        [HttpPost]
        public Response<DoctorDTO> Post(DoctorDTO doctorDTO)
        {
            var edoctor = _db.Users.Where(x => x.MobileNumber == doctorDTO.MobileNumber && x.UserType == "DOCTOR").FirstOrDefault();
            if (edoctor != null)
                return new Response<DoctorDTO>(false, "Account already exist", null);
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            doctorDTO.FirstName = textInfo.ToTitleCase(doctorDTO.FirstName);
            doctorDTO.LastName = textInfo.ToTitleCase(doctorDTO.LastName);
            doctorDTO.DisplayName = textInfo.ToTitleCase(doctorDTO.DisplayName);
            {

                // 1- send email to doctor

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
                doctorDB.ValidUpto = null;
                doctorDB.UserId = userDB.Id;
                _db.Doctors.Add(doctorDB);
                _db.SaveChanges();
                doctorDTO.Id = doctorDB.Id;

                //generate SMS and save it to the db
                // UserSMS u = new UserSMS (_db);
                // u.DoctorSMS (doctorDTO);
                // UserEmail.DoctorEmail (doctorDTO);

                // 4- check if clinicDto exsist; then save clinic as well
                if (doctorDTO.ClinicDTO != null && !String.IsNullOrEmpty(doctorDTO.ClinicDTO.Name))
                {
                    doctorDTO.ClinicDTO.Name = textInfo.ToTitleCase(doctorDTO.ClinicDTO.Name);

                    doctorDTO.ClinicDTO.DoctorId = doctorDB.Id;

                    Clinic clinicDB = _mapper.Map<Clinic>(doctorDTO.ClinicDTO);
                    clinicDB.IsOnline = true;
                    _db.Clinics.Add(clinicDB);
                    _db.SaveChanges();

                    doctorDTO.ClinicDTO.Id = clinicDB.Id;
                }
            }
            return new Response<DoctorDTO>(true, null, doctorDTO);
        }

        [HttpPost("{id}/update-images")]
        public Response<DoctorDTO> UpdateUploadedImages(int id)
        {
            var dbDoctor = _db.Doctors.Where(d => d.Id == id).FirstOrDefault();

            if (HttpContext.Request.Form.Files.Any())
            {
                var httpPostedProfileImage = HttpContext.Request.Form.Files["ProfileImage"];
                var httpPostedSignatureImage = HttpContext.Request.Form.Files["SignatureImage"];
                
                if (httpPostedProfileImage != null)
                {

                    var fileSavePath = Path.Combine(_host.ContentRootPath, "Content/UserImages", httpPostedProfileImage.FileName);
                    using (var fileStream = new FileStream(fileSavePath, FileMode.Create))
                        httpPostedProfileImage.CopyToAsync(fileStream);
                    dbDoctor.ProfileImage = httpPostedProfileImage.FileName;
                }

                if (httpPostedSignatureImage != null)
                {
                    var fileSavePath = Path.Combine(_host.ContentRootPath, "Content/UserImages", httpPostedSignatureImage.FileName);
                    using (var fileStream = new FileStream(fileSavePath, FileMode.Create))
                        httpPostedSignatureImage.CopyToAsync(fileStream);
                    dbDoctor.SignatureImage = httpPostedSignatureImage.FileName;
                }
                _db.SaveChanges();
                return new Response<DoctorDTO>(true, null, null);
            }

            return new Response<DoctorDTO>(false, "invalid files in request", null);
        }

        // }
        [HttpPut("{id}")]
        public Response<DoctorDTO> Put(int Id, DoctorDTO doctorDTO)
        {

            var dbDoctor = _db.Doctors.Where(c => c.Id == Id).FirstOrDefault();
            dbDoctor.FirstName = doctorDTO.FirstName;
            dbDoctor.LastName = doctorDTO.LastName;
            dbDoctor.DisplayName = doctorDTO.DisplayName;
            dbDoctor.IsApproved = doctorDTO.IsApproved;
            dbDoctor.Email = doctorDTO.Email;
            dbDoctor.PMDC = doctorDTO.PMDC;
            dbDoctor.PhoneNo = doctorDTO.PhoneNo;
            dbDoctor.ShowPhone = doctorDTO.ShowPhone;
            dbDoctor.ShowMobile = doctorDTO.ShowMobile;
            // dbDoctor.Qualification = doctorDTO.Qualification;
            // dbDoctor.AdditionalInfo = doctorDTO.AdditionalInfo;
            dbDoctor.ProfileImage = doctorDTO.ProfileImage;
            dbDoctor.SignatureImage = doctorDTO.SignatureImage;

            //dbDoctor = Mapper.Map<DoctorDTO, Doctor>(doctorDTO, dbDoctor);
            //entities.Entry<Doctor>(dbDoctor).State = System.Data.Entity.EntityState.Modified;
            _db.SaveChanges();
            return new Response<DoctorDTO>(true, null, doctorDTO);

        }

        [HttpPut("{id}/update-permission")]
        public Response<DoctorDTO> UpdatePermissions(int Id, DoctorDTO doctorDTO)
        {
            var dbDoctor = _db.Doctors.Where(c => c.Id == Id).FirstOrDefault();

            dbDoctor.AllowInvoice = doctorDTO.AllowInvoice;
            dbDoctor.AllowFollowUp = doctorDTO.AllowFollowUp;
            dbDoctor.AllowChart = doctorDTO.AllowChart;
            dbDoctor.AllowInventory = doctorDTO.AllowInventory;
            _db.SaveChanges();
            //  dbDoctor = _mapper.Map<DoctorDTO, Doctor>(doctorDTO, dbDoctor);
            return new Response<DoctorDTO>(true, null, doctorDTO);
        }

        [HttpPut("{id}/validUpto")]
        public Response<DoctorDTO> ChangeValidity(int Id, DoctorDTO doctorDTO)
        {
            var dbDoctor = _db.Doctors.Where(x => x.Id == Id).FirstOrDefault();
            dbDoctor.ValidUpto = doctorDTO.ValidUpto;
            _db.SaveChanges();
            DoctorDTO doctorDTOs = _mapper.Map<DoctorDTO>(dbDoctor);
            return new Response<DoctorDTO>(true, null, doctorDTOs);
        }

        [HttpGet("approve/{id}")]
        public Response<string> ApproveDoctor(int id)
        {
            var dbDoctor = _db.Doctors.Where(x => x.Id == id).FirstOrDefault();
            dbDoctor.IsApproved = true;
            dbDoctor.ValidUpto = DateTime.UtcNow.AddHours(5).AddMonths(3);
            _db.SaveChanges();
            var vaccines = _db.Vaccines.Include(x => x.Brands).ToList();
            foreach (var vaccine in vaccines)
            {
                // add default brands amount and inventory count of doctor
                var brands = vaccine.Brands;
                foreach (var brand in brands)
                {
                    BrandAmount ba = new BrandAmount();
                    ba.Amount = 0;
                    ba.DoctorId = dbDoctor.Id;
                    ba.BrandId = brand.Id;
                    _db.BrandAmounts.Add(ba);

                    BrandInventory bi = new BrandInventory();
                    bi.Count = 0;
                    bi.DoctorId = dbDoctor.Id;
                    bi.BrandId = brand.Id;
                    _db.BrandInventorys.Add(bi);
                    _db.SaveChanges();
                }
            }
            // DoctorDTO doctorDTOs = _mapper.Map<DoctorDTO>(dbDoctor);
            return new Response<string>(true, null, "approved");
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
                        var doctorChilds = _db.Childs.Include(x => x.User).Where(x => x.ClinicId == clinic.Id).ToList();
                        if (!String.IsNullOrEmpty(searchKeyword))
                        {
                            searchKeyword = searchKeyword.Trim();
                            if (searchKeyword.StartsWith("+")) searchKeyword = searchKeyword.Substring(1);
                            if (searchKeyword.StartsWith("0")) searchKeyword = searchKeyword.Substring(1);
                            if (searchKeyword.StartsWith("00")) searchKeyword = searchKeyword.Substring(2);
                            if (searchKeyword.StartsWith("92")) searchKeyword = searchKeyword.Substring(2);
                            childDTOs.AddRange(_mapper.Map<List<ChildDTO>>(
                                clinic.Childs.Where(x => x.Name.Trim().ToLower().Contains(searchKeyword.ToLower()) ||
                                   x.FatherName.Trim().ToLower().Contains(searchKeyword.ToLower()) ||
                                   x.Email.Trim().Contains(searchKeyword.ToLower()) ||
                                   x.User.MobileNumber.Trim().Contains(searchKeyword.ToLower())).ToList<Child>()));

                        }
                        else
                            childDTOs.AddRange(_mapper.Map<List<ChildDTO>>(clinic.Childs.ToList<Child>()));
                    }
                    foreach (var item in childDTOs)
                    {
                        var dbChild = _db.Childs.Where(x => x.Id == item.Id).Include(x => x.User).FirstOrDefault();
                        item.MobileNumber = dbChild.User.CountryCode + dbChild.User.MobileNumber;
                    }
                    return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs.OrderByDescending(x => x.Id).ToList().Skip(15 * currentPage).Take(15));
                }
            }
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id)
        {
            {
                var dbDoctor = _db.Doctors.Include(x => x.User).Include(x => x.DoctorSchedules).Include(x => x.FollowUps)
                    .Include(x => x.Clinics).ThenInclude(x => x.ClinicTimings).Include(x => x.Clinics).ThenInclude(x => x.Childs).Where(c => c.Id == Id).FirstOrDefault();
                foreach (var clinic in dbDoctor.Clinics)
                {
                    foreach (var child in clinic.Childs)
                    {
                        var dbChild = _db.Childs.Where(x => x.Id == child.Id).Include(x => x.Schedules).Include(x => x.User).ThenInclude(x => x.Childs).Include(x => x.FollowUps).FirstOrDefault();
                        _db.Schedules.RemoveRange(dbChild.Schedules);
                        _db.FollowUps.RemoveRange(dbChild.FollowUps);
                        if (dbChild.User.Childs.Count == 1)
                            _db.Users.Remove(dbChild.User);
                        _db.Childs.Remove(dbChild);
                    }
                    _db.ClinicTimings.RemoveRange(clinic.ClinicTimings);
                }
                _db.DoctorSchedules.RemoveRange(dbDoctor.DoctorSchedules);
                _db.Clinics.RemoveRange(dbDoctor.Clinics);
                _db.Users.Remove(dbDoctor.User);
                _db.Doctors.Remove(dbDoctor);
                _db.SaveChanges();
                return new Response<string>(true, "Doctor is deleted successfully", null);
            }
        }
    }
}
//}