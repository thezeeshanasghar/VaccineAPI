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
        public Response<ClinicDTO> Post([FromBody] ClinicDTO clinicDTO)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            clinicDTO.Name = textInfo.ToTitleCase(clinicDTO.Name);
            {
                Clinic clinicDb = _mapper.Map<Clinic>(clinicDTO);
                _db.Clinics.Add(clinicDb);
                _db.SaveChanges();
                clinicDTO.Id = clinicDb.Id;
                return new Response<ClinicDTO>(true, null, clinicDTO);
            }
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
        public Response<ClinicDTO> EditClinic(ClinicDTO clinicDTO)
        {
            {
                var dbClinic = _db.Clinics.Where(c => c.Id == clinicDTO.Id).FirstOrDefault();
                if (clinicDTO.IsOnline)
                {
                    dbClinic.IsOnline = true;

                }

                var clinicList = _db.Clinics.Where(x => x.DoctorId == clinicDTO.DoctorId).Where(x => x.Id != clinicDTO.Id).ToList();
                if (clinicList.Count != 0)
                    foreach (var clinic in clinicList)
                    {
                        clinic.IsOnline = false;
                        _db.Clinics.Attach(clinic);
                        _db.Entry(clinic).State = EntityState.Modified;
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
        [HttpGet("{id}/doctor")]
        public async Task<Response<DoctorDTO>> GetDoctorByClinicId(long id)
        {
            var clinic = await _db
                .Clinics.Include(c => c.Doctor) 
                .FirstOrDefaultAsync(c => c.Id == id);

            if (clinic == null)
            {
                return new Response<DoctorDTO>(false, "Clinic not found", null);
            }
            var doctor = clinic.Doctor; 
            var doctorDTO = new DoctorDTO
            {
                Id = doctor.Id,
                FirstName = doctor.FirstName,
                LastName = doctor.LastName,
                Email = doctor.Email,
            };

            return new Response<DoctorDTO>(true, null, doctorDTO);
        }
        [HttpPatch("update-clinic-id")]
        public async Task<IActionResult> UpdateClinicIdForChild(
            [FromQuery] string doctorDisplayName,
            [FromQuery] long childId
        )
        {
            try
            {
                // Fetch the doctor by display name
                var doctor = await _db.Doctors.FirstOrDefaultAsync(d =>
                    d.DisplayName == doctorDisplayName
                );
                if (doctor == null)
                {
                    return NotFound($"Doctor with display name {doctorDisplayName} not found");
                }

                // Fetch the list of clinics associated with the doctor
                var clinics = await _db.Clinics.Where(c => c.DoctorId == doctor.Id).ToListAsync();
                if (clinics == null || !clinics.Any())
                {
                    return NotFound(
                        $"No clinics found for doctor with display name {doctorDisplayName}"
                    );
                }

                // Select the clinic that is online
                var onlineClinic = clinics.FirstOrDefault(c => c.IsOnline);
                if (onlineClinic == null)
                {
                    return NotFound(
                        $"No online clinics found for doctor with display name {doctorDisplayName}"
                    );
                }

                // Fetch the child by child ID
                var child = await _db.Childs.FirstOrDefaultAsync(c => c.Id == childId);
                if (child == null)
                {
                    return NotFound($"Child with ID {childId} not found");
                }

                // Update the clinic ID of the child
                child.ClinicId = onlineClinic.Id;

                // Save changes to the database
                await _db.SaveChangesAsync();

                return Ok(
                    $"Clinic ID for child ID {childId} updated successfully to clinic ID {onlineClinic.Id}"
                );
            }
            catch (DbUpdateException dbEx)
            {
                // Log the detailed error
                Console.WriteLine(
                    $"An error occurred while updating clinic ID for child ID {childId}: {dbEx.InnerException?.Message}"
                );
                return StatusCode(
                    500,
                    $"An error occurred while updating clinic ID for child ID {childId}: {dbEx.InnerException?.Message}"
                );
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    $"An error occurred while updating clinic ID for child ID {childId}: {ex.Message}"
                );
            }
        }
    }
}
