using System;
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
    public class HomeCityController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public HomeCityController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("doctor/{doctorId}")]
        public async Task<Response<IEnumerable<HomeServiceCityDTO>>> GetByDoctor(long doctorId)
        {
            try
            {
                var cities = await _db.HomeServiceCities.AsNoTracking()
                    .Where(c => c.DoctorId == doctorId && c.IsActive)
                    .OrderBy(c => c.CityName)
                    .ToListAsync();

                var dtos = _mapper.Map<List<HomeServiceCityDTO>>(cities);
                return new Response<IEnumerable<HomeServiceCityDTO>>(true, null, dtos);
            }
            catch (Exception ex)
            {
                return new Response<IEnumerable<HomeServiceCityDTO>>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpPost]
        public async Task<Response<HomeServiceCityDTO>> AddCity(HomeServiceCityDTO dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.CityName))
                {
                    return new Response<HomeServiceCityDTO>(false, "City name is required.", null);
                }

                var exists = await _db.HomeServiceCities.AnyAsync(
                    c => c.DoctorId == dto.DoctorId && c.CityName.ToLower() == dto.CityName.ToLower().Trim() && c.IsActive);

                if (exists)
                {
                    return new Response<HomeServiceCityDTO>(false, "City already exists.", null);
                }

                var city = new HomeServiceCity
                {
                    CityName = dto.CityName.Trim(),
                    DoctorId = dto.DoctorId,
                    IsActive = true
                };

                _db.HomeServiceCities.Add(city);
                await _db.SaveChangesAsync();

                var result = _mapper.Map<HomeServiceCityDTO>(city);
                return new Response<HomeServiceCityDTO>(true, "City added.", result);
            }
            catch (Exception ex)
            {
                return new Response<HomeServiceCityDTO>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpDelete("{id}")]
        public async Task<Response<object>> DeleteCity(long id)
        {
            try
            {
                var city = await _db.HomeServiceCities.FirstOrDefaultAsync(c => c.Id == id);
                if (city == null)
                {
                    return new Response<object>(false, "City not found.", null);
                }

                city.IsActive = false;
                await _db.SaveChangesAsync();

                return new Response<object>(true, "City removed.", null);
            }
            catch (Exception ex)
            {
                return new Response<object>(false, $"An error occurred: {ex.Message}", null);
            }
        }
    }
}
