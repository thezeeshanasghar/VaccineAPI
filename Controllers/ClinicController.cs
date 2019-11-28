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
            var list = await _db.Clinics.Include(x=>x.ClinicTimings).OrderBy(x=>x.Id).ToListAsync();
            //var list = await _db.Clinics.OrderBy(x=>x.Id).ToListAsync();
            List<ClinicDTO> listDTO = _mapper.Map<List<ClinicDTO>>(list);
           
            return new Response<List<ClinicDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
      public async Task<Response<ClinicDTO>> GetSingle(long id)
        {
            var dbclinic = await _db.Clinics.Include(x=>x.ClinicTimings).FirstOrDefaultAsync();

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
    }
}
