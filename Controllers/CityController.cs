using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CityController : ControllerBase
    {
        private readonly Context _context;

        public CityController(Context context)
        {
            _context = context;
        }

        // GET: api/City
        [HttpGet]
        public async Task<ActionResult<IEnumerable<City>>> GetAllCities()
        {
            return await _context.Cities.ToListAsync();
        }
        [HttpGet("Names")]
        public async Task<ActionResult<IEnumerable<string>>> GetAllCityNames()
        {
            var cityNames = await _context.Cities.Select(city => city.Name).ToListAsync();
            return cityNames;
        }

        // GET: api/City/5
        [HttpGet("{id}")]
        public async Task<ActionResult<City>> GetCity(int id)
        {
            var city = await _context.Cities.FindAsync(id);

            if (city == null)
            {
                return NotFound();
            }

            return city;
        }

        // POST: api/City
        [HttpPost]
        public async Task<ActionResult<City>> PostCity(City city)
        {
            _context.Cities.Add(city);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetCity", new { id = city.Id }, city);
        }

        // PUT: api/City/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCity(int id, City city)
        {
            if (id != city.Id)
            {
                return BadRequest();
            }

            _context.Entry(city).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CityExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/City/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCity(int id)
        {
            var city = await _context.Cities.FindAsync(id);
            if (city == null)
            {
                return NotFound();
            }

            _context.Cities.Remove(city);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CityExists(int id)
        {
            return _context.Cities.Any(e => e.Id == id);
        }
        [HttpGet("CityAlert")]
        public async Task<IEnumerable<string>> GetLatestPatientCitiesNotInCityTableAsync()
        {
            var latestCities = await _context.Childs
            .OrderByDescending(p => p.Id)
            .Take(3)
            .Select(p => p.City)
            .ToListAsync();

            var existingCities = await _context.Cities
                .Where(c => latestCities.Contains(c.Name))
                .Select(c => c.Name)
                .ToListAsync();

            var citiesNotInCityTable = latestCities.Except(existingCities);
            // if (citiesNotInCityTable.Any())
            // {
            //     var newCities = citiesNotInCityTable.Select(city => new City { Name = city }).ToList();
            //     _context.Cities.AddRange(newCities);
            //     await _context.SaveChangesAsync();
            // }

            return citiesNotInCityTable;
        }
        // [HttpPut("update")]
        // public async Task<ActionResult<Response<Object>>> UpdateChildCity(string currentCity, [FromBody] string newCity)
        // {
        //     var AlreadyCity = await _context.Cities.FirstOrDefaultAsync(c => c.Name == newCity);
        //     if (AlreadyCity != null)
        //     {
        //          return new ActionResult<Response<Object>>(new Response<Object>(false, "Cannot update the city because it already exists.", null));
        //     }
            
        //     var childs = await _context.Childs.Where(c => c.City == currentCity).ToListAsync();
        //     if (childs == null || !childs.Any())
        //     {
        //         return NotFound();
        //     }

        //     foreach (var child in childs)
        //     {
        //         // Update the city of each child with the new city
        //         child.City = newCity;
        //         _context.Childs.Update(child);
        //     }
            
        //     if (AlreadyCity == null)
        //     {
        //         var city = new City { Name = newCity };
        //         _context.Cities.Add(city);
        //     }
        
        //     await _context.SaveChangesAsync();

        //     return new Response<object>(true, "City updated successfully.", null);
        // }
        [HttpPut("update")]
        public async Task<ActionResult<Response<Object>>> UpdateChildCity(string currentCity, [FromBody] string newCity)
        {
            var AlreadyCity = await _context.Cities.FirstOrDefaultAsync(c => c.Name == newCity);
            if (AlreadyCity != null)
            {
                return BadRequest(new Response<Object>(false, "Cannot update the city because it already exists 1.", null));
            }
            
            var childs = await _context.Childs.Where(c => c.City == currentCity).ToListAsync();
            if (childs == null || !childs.Any())
            {
                return NotFound();
            }

            foreach (var child in childs)
            {
                // Update the city of each child with the new city
                child.City = newCity;
                _context.Childs.Update(child);
            }
            
            if (AlreadyCity == null)
            {
                var city = new City { Name = newCity };
                _context.Cities.Add(city);
            }
        
            await _context.SaveChangesAsync();

            return Ok(new Response<object>(true, "City updated successfully.", null));
        }


    }
}
