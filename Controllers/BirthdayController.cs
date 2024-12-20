﻿using System;
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
    }
}
