using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using AutoMapper;


namespace VaccineAPI.Controllers
{
    // [EnableCors("CorsApi")]
    [Route("api/[controller]")]
    [ApiController]
    public class DoseController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public DoseController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<Response<List<DoseDTO>>> GetAll()
        {
            var list = await _db.Doses.OrderBy(x => x.Name).ToListAsync();
            List<DoseDTO> listDTO = _mapper.Map<List<DoseDTO>>(list);

            return new Response<List<DoseDTO>>(true, null, listDTO);
        }
      

        //to add new dose in doctor schedule
        [HttpGet("newdoctor/{id}")]
        public async Task<Response<List<DoseDTO>>> GetNewDoses(long id)
        {
            var doctorschedule = await _db.DoctorSchedules.Where(x => x.DoctorId == id).ToListAsync();
            var dosesa = await _db.Doses.ToListAsync();
            List<Dose> doses = new List<Dose>();
            foreach (var dose in dosesa)
            {
                // var dctschedule = await _db.DoctorSchedules.Where(x=>x.DoctorId == id).Where(x=>x.DoseId == dose.Id).ToListAsync();
                var dctschedule = doctorschedule.Where(x => x.DoseId == dose.Id).FirstOrDefault();
                if (dctschedule == null)
                    doses.Add(dose);
            }

            List<DoseDTO> listDTO = _mapper.Map<List<DoseDTO>>(doses);

            return new Response<List<DoseDTO>>(true, null, listDTO);
        }


        // to add new dose in child schedule
        [HttpGet("newchild/{id}")]
        public async Task<Response<List<DoseDTO>>> GetNewChildDoses(long id)
        {
            var childschedule = await _db.Schedules.Where(x => x.ChildId == id).ToListAsync();
            var dosesa = await _db.Doses.ToListAsync();
            List<Dose> doses = new List<Dose>();
            foreach (var dose in dosesa)
            {
                // var dctschedule = await _db.DoctorSchedules.Where(x=>x.DoctorId == id).Where(x=>x.DoseId == dose.Id).ToListAsync();
                var chldschedule = childschedule.Where(x => x.DoseId == dose.Id).FirstOrDefault();
                if (chldschedule == null)
                    doses.Add(dose);
            }

            List<DoseDTO> listDTO = _mapper.Map<List<DoseDTO>>(doses);

            return new Response<List<DoseDTO>>(true, null, listDTO);
        }
        // [HttpGet("newchild2/{id}")]
        // public async Task<Response<List<DoseDTO>>> GetNewChildDoses2(long id)
        // {
        //     // Step 1: Fetch the clinic ID based on the child ID from the child table
        //     var child = await _db.Childs.FirstOrDefaultAsync(c => c.Id == id);
            

        //     var clinicId = child.ClinicId;

        //     // Step 2: Retrieve the doctor ID associated with the clinic
        //     var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId);
      

        //     var doctorId = clinic.DoctorId;

        //     // Step 3: Fetch the doctor's schedule based on the doctor ID
        //    var doctorSchedule = await _db.DoctorSchedules
        //     .Where(ds => ds.DoctorId == doctorId) 
        //     .Select(ds => ds.DoseId) // Select only DoseId from the DoctorSchedule
        //     .ToListAsync();

        //     var childDoses = await _db.Schedules
        // .Where(s => s.ChildId == id)
        // .Select(s => s.DoseId)
        // .ToListAsync();


        //    var newDoses = doctorSchedule.Where(ds => !childDoses.Contains(ds)).ToList();
        
        //     var newDoseEntities = await _db.Doses
        // .Where(d => newDoses.Contains(d.Id))
        // .ToListAsync();
        //    var newDoseDTOs = _mapper.Map<List<DoseDTO>>(newDoseEntities);

        //     // Return the list of new doses mapped to DoseDTO objects
        //     return new Response<List<DoseDTO>>(true, null, newDoseDTOs);
        // }
                public class NewDoseResponse
                {
                    public DoseDTO Dose { get; set; }
                    public bool IsActive { get; set; }
                }
                [HttpGet("newchild2/{id}")]
                public async Task<Response<List<NewDoseResponse>>> GetNewChildDoses2(long id)
                {
                    // Step 1: Fetch the clinic ID based on the child ID from the child table
                    var child = await _db.Childs.FirstOrDefaultAsync(c => c.Id == id);
                    if (child == null)
                    {
                        return new Response<List<NewDoseResponse>>(false, "Child not found.", null);
                    }

                    var clinicId = child.ClinicId;

                    // Step 2: Retrieve the doctor ID associated with the clinic
                    var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId);
                    if (clinic == null || clinic.DoctorId == null)
                    {
                        return new Response<List<NewDoseResponse>>(false, "Clinic or doctor not found.", null);
                    }

                    var doctorId = clinic.DoctorId;

                    // Step 3: Fetch the doctor's schedule based on the doctor ID
                    var doctorSchedule = await _db.DoctorSchedules
                        .Where(ds => ds.DoctorId == doctorId)
                        .Select(ds => new { DoseId = ds.DoseId, IsActive = ds.IsActive }) // Select DoseId and IsActive
                        .ToListAsync();

                    // Step 4: Fetch doses from the schedule table based on the child ID
                    var childDoses = await _db.Schedules
                        .Where(s => s.ChildId == id)
                        .Select(s => s.DoseId)
                        .ToListAsync();

                    // Step 5: Filter doses from the doctor's schedule that are not present in child's doses
                    var newDoses = doctorSchedule.Where(ds => !childDoses.Contains(ds.DoseId)).ToList();

                    // Step 6: Fetch the corresponding Dose entities from the database
                    var newDoseEntities = await _db.Doses
                        .Where(d => newDoses.Select(nd => nd.DoseId).Contains(d.Id))
                        .ToListAsync();

                    // Step 7: Create NewDoseResponse objects with DoseDTO and IsActive properties
                    var responseObjects = newDoseEntities.Select(dose => new NewDoseResponse
                    {
                        Dose = _mapper.Map<DoseDTO>(dose),
                        IsActive = newDoses.FirstOrDefault(nd => nd.DoseId == dose.Id)?.IsActive ?? false
                    }).ToList();

                    // Return the list of NewDoseResponse objects
                    return new Response<List<NewDoseResponse>>(true, null, responseObjects);
                }


        [HttpGet("{id}")]
        public async Task<Response<DoseDTO>> GetSingle(long id)
        {
            var dbdose = await _db.Doses.Where(x => x.Id == id).FirstOrDefaultAsync();

            DoseDTO doseDTO = _mapper.Map<DoseDTO>(dbdose);

            if (dbdose == null)
                return new Response<DoseDTO>(false, "Not Found", null);

            return new Response<DoseDTO>(true, null, doseDTO);
        }

        [HttpGet("vaccinedoses/{VaccineId}")]
        public async Task<Response<List<DoseDTO>>> GetDosesByVaccineId(long VaccineId)
        {
            var dbdose = await _db.Doses.Where(x => x.VaccineId == VaccineId).ToListAsync();
            List<DoseDTO> doseDTO = _mapper.Map<List<DoseDTO>>(dbdose);
            if (dbdose == null)
            {
                return new Response<List<DoseDTO>>(false, "not found", null);
            }
            return new Response<List<DoseDTO>>(true, null, doseDTO);
        }

        [HttpPost]
        public Response<DoseDTO> Post(DoseDTO doseDTO)
        {
            Dose doseDb = _mapper.Map<Dose>(doseDTO);
            _db.Doses.Add(doseDb);
            _db.SaveChanges();
            doseDTO.Id = doseDb.Id;
            return new Response<DoseDTO>(true, null, doseDTO);
        }

        [HttpPut("{id}")]
        public Response<DoseDTO> Put(int Id, DoseDTO doseDTO)
        {
            var dbDose = _db.Doses.Where(c => c.Id == Id).FirstOrDefault();
            dbDose.Name = doseDTO.Name;
            dbDose.MinAge = doseDTO.MinAge;
            dbDose.MaxAge = doseDTO.MaxAge;
            dbDose.MinGap = doseDTO.MinGap;
            dbDose.DoseOrder = doseDTO.DoseOrder;
            _db.SaveChanges();
            return new Response<DoseDTO>(true, null, doseDTO);
        }

        [HttpDelete("{id}")]
        public Response<string> Delete(int Id)
        {
            var dbDose = _db.Doses.Where(c => c.Id == Id).FirstOrDefault();
            _db.Doses.Remove(dbDose);
            _db.SaveChanges();
            return new Response<string>(true, null, "record deleted");
        }
        [HttpGet("doses/{childId}")]
        public async Task<Response<List<DoseDTO>>> GetSDosesForChild(int childId)
        {
            // Retrieve the scheduled doses for the specified child
            var scheduledDosesIds = await _db.Schedules
                                            .Where(s => s.ChildId == childId)
                                            .Select(s => s.DoseId)
                                            .ToListAsync();

            // Retrieve special doses that are not already scheduled for the child
            var Doses = await _db.Doses
                                        .Where(x => !scheduledDosesIds.Contains(x.Id))
                                        .OrderBy(x => x.MinAge)
                                        .ToListAsync();

            // Map the special doses to DTOs
            List<DoseDTO> specialDoseDTOs = _mapper.Map<List<DoseDTO>>(Doses);

            return new Response<List<DoseDTO>>(true, null, specialDoseDTOs);
        }

    }
}
