using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClinicTimingController : ControllerBase
    {
        private readonly Context _db;

        public ClinicTimingController(Context context)
        {
            _db = context;
        }

        public class ClinicIdsRequestModel
        {
            public List<long> ClinicIds { get; set; } = new List<long>();
        }

        [HttpPatch("children/schedules")]
        public async Task<ActionResult<IEnumerable<long>>> GetChildIdsWithSchedulesFromClinic([FromBody] ClinicIdsRequestModel model, [FromQuery] string fromDate, [FromQuery] string toDate)
        {
            try
            {
                var parsedFromDate = DateTime.Parse(fromDate);
                var parsedToDate = DateTime.Parse(toDate);

                if (model == null || model.ClinicIds == null || !model.ClinicIds.Any())
                {
                    return BadRequest("No clinic IDs provided in the request.");
                }

                List<long> childIdsWithSchedules = new List<long>();


                foreach (var id in model.ClinicIds)
                {
                    var childIds = await _db.Childs
                                            .Where(c => c.ClinicId == id)
                                            .Select(c => c.Id)
                                            .ToListAsync();

                    if (childIds == null || !childIds.Any())
                    {

                        continue;
                    }


                    foreach (var childId in childIds)
                    {
                        var schedules = await _db.Schedules
                                                .Where(c => c.ChildId == childId && c.Date >= parsedFromDate && c.Date <= parsedToDate)
                                                .ToListAsync();
                        if (schedules.Any())
                        {
                            var daysToAdd = (parsedToDate - parsedFromDate).Days + 1;
                            foreach (var schedule in schedules)
                            {
                                schedule.Date = schedule.Date.AddDays(daysToAdd);
                            }
                            await _db.SaveChangesAsync();
                        }

                        childIdsWithSchedules.Add(childId);
                    }
                }

                return Ok(childIdsWithSchedules);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while retrieving child IDs: {ex.Message}");
            }
        }
    }
}
