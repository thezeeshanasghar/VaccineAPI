using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
using System.Globalization;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClinicController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public ClinicController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<ClinicDTO>>> GetAll()
        {
            var list = await _db.Clinics.Include(x => x.ClinicTimings).OrderBy(x => x.Id).ToListAsync();
            //var list = await _db.Clinics.OrderBy(x=>x.Id).ToListAsync();
            List<ClinicDTO> listDTO = _mapper.Map<List<ClinicDTO>>(list);
            return new Response<List<ClinicDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<ClinicDTO>> GetSingle(long id)
        {
            var dbclinic = await _db.Clinics.Include(x => x.ClinicTimings).Where(x => x.Id == id).FirstOrDefaultAsync();
            ClinicDTO clinicDTO = _mapper.Map<ClinicDTO>(dbclinic);
            if (dbclinic == null)
                return new Response<ClinicDTO>(false, "Not Found", null);

            return new Response<ClinicDTO>(true, null, clinicDTO);
        }

        [HttpPost]
        public Response<ClinicDTO> Add([FromBody] ClinicDTO clinicDTO)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            clinicDTO.Name = textInfo.ToTitleCase(clinicDTO.Name);

            var clinicList = _db.Clinics.Where(x => x.DoctorId == clinicDTO.DoctorId).ToList();

            var dbClinic = _mapper.Map<Clinic>(clinicDTO);


            dbClinic.IsOnline = clinicList.Count == 0;

            _db.Clinics.Add(dbClinic);
            _db.SaveChanges();

            clinicDTO.Id = dbClinic.Id;
            clinicDTO.IsOnline = dbClinic.IsOnline; // Ensure the DTO reflects the correct value

            return new Response<ClinicDTO>(true, null, clinicDTO);
        }

        [HttpPut("{id}")]
        public Response<ClinicDTO> Put(int Id, ClinicDTO clinicDTO)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            clinicDTO.Name = textInfo.ToTitleCase(clinicDTO.Name);
            {
                var dbClinic = _db.Clinics.Where(c => c.Id == Id).FirstOrDefault();
                clinicDTO.IsOnline = false;
                dbClinic.Name = clinicDTO.Name;
                dbClinic.ConsultationFee = clinicDTO.ConsultationFee;
                dbClinic.PhoneNumber = clinicDTO.PhoneNumber;
                dbClinic.Lat = clinicDTO.Lat;
                dbClinic.Long = clinicDTO.Long;
                dbClinic.Address = clinicDTO.Address;
                dbClinic.MonogramImage = clinicDTO.MonogramImage;
                dbClinic.RegNo = clinicDTO.RegNo;
                _db.SaveChanges();
                foreach (var clinicTiming in clinicDTO.ClinicTimings)
                {
                    ClinicTiming dbClinicTiming = _db.ClinicTimings.Where(x => x.Id == clinicTiming.Id).FirstOrDefault();
                    if (dbClinicTiming != null)
                    {
                        dbClinicTiming.ClinicId = Id;
                        dbClinicTiming.Day = clinicTiming.Day;
                        dbClinicTiming.StartTime = clinicTiming.StartTime;
                        dbClinicTiming.EndTime = clinicTiming.EndTime;
                        dbClinicTiming.Session = clinicTiming.Session;
                        dbClinicTiming.IsOpen = clinicTiming.IsOpen;
                    }
                    else if (dbClinicTiming == null && clinicTiming.IsOpen)
                    {
                        ClinicTiming newClinicTiming = new ClinicTiming();
                        newClinicTiming.ClinicId = Id;
                        newClinicTiming.Day = clinicTiming.Day;
                        newClinicTiming.StartTime = clinicTiming.StartTime;
                        newClinicTiming.EndTime = clinicTiming.EndTime;
                        newClinicTiming.Session = clinicTiming.Session;
                        newClinicTiming.IsOpen = clinicTiming.IsOpen;
                        _db.ClinicTimings.Add(newClinicTiming);
                    }
                    _db.SaveChanges();
                }
                return new Response<ClinicDTO>(true, null, clinicDTO);
            }
        }

        [HttpPut("editClinic")]
        public Response<ClinicDTO> Edit([FromBody] ClinicDTO clinicDTO)
        {
            {
                var dbClinic = _db.Clinics.Where(c => c.Id == clinicDTO.Id).FirstOrDefault();
                if (clinicDTO.IsOnline)
                {
                    dbClinic.IsOnline = true;
                }

                // Get all clinics for this doctor
                var clinicList = _db.Clinics.Where(x => x.DoctorId == clinicDTO.DoctorId).ToList();
                
                // If this is the only clinic for the doctor, set it as online
                if (clinicList.Count == 1)
                {
                    dbClinic.IsOnline = true;
                }
                else if (clinicList.Count > 1)
                {
                    // If there are multiple clinics, set others to offline when one is set online
                    foreach (var clinic in clinicList.Where(x => x.Id != clinicDTO.Id))
                    {
                        clinic.IsOnline = false;
                        _db.Clinics.Attach(clinic);
                        _db.Entry(clinic).State = EntityState.Modified;
                    }
                }

                _db.SaveChanges();
                clinicDTO.Name = dbClinic.Name;
                return new Response<ClinicDTO>(true, null, clinicDTO);
            }
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id)
        {
            var dbClinic = _db.Clinics.Where(c => c.Id == Id).FirstOrDefault();
            _db.Clinics.Remove(dbClinic);
            _db.SaveChanges();
            return new Response<string>(true, null, "record deleted");
        }

        [HttpGet("doctor/{clinicId}")]
        public async Task<Response<DoctorDTO>> GetDoctorByClinicId(long clinicId)
        {
            var clinic = await _db.Clinics
                .Include(x => x.Doctor)
                .Where(x => x.Id == clinicId)
                .FirstOrDefaultAsync();

            if (clinic == null)
                return new Response<DoctorDTO>(false, "Clinic not found", null);

            if (clinic.Doctor == null)
                return new Response<DoctorDTO>(false, "No doctor assigned to this clinic", null);

            var doctorDTO = _mapper.Map<DoctorDTO>(clinic.Doctor);
            return new Response<DoctorDTO>(true, null, doctorDTO);
        }
    }
}
