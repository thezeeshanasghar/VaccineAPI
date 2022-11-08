using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;

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

        [HttpGet("{GapDays}/{OnlineClinicId}")]
        public Response<IEnumerable<ChildDTO>> GetBirthdayAlert(int GapDays, long OnlineClinicId)
        {
            List<Child> childs = GetBirthdayAlertData(GapDays, OnlineClinicId, _db);
            IEnumerable<ChildDTO> childDTOs = _mapper.Map<IEnumerable<ChildDTO>>(childs);
            return new Response<IEnumerable<ChildDTO>>(true, null, childDTOs);
        }

        private static List<Child> GetBirthdayAlertData(int GapDays, long OnlineClinicId, Context db)
        {
            return db.Childs.Where(x=>
                        x.DOB.Date== DateTime.Today &&
                        x.IsInactive.HasValue.Equals(false)).ToList<Child>();
        }

    }
}
