using System;
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
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public UserController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
         public async Task<Response<List<UserDTO>>> GetAll()
        {
            var list = await _db.Users.OrderBy(x=>x.Id).ToListAsync();
            List<UserDTO> listDTO = _mapper.Map<List<UserDTO>>(list);
           
            return new Response<List<UserDTO>>(true, null, listDTO);
        }
        
        [HttpGet("{id}")]
        public async Task<Response<User>> GetSingle(long id)
        {
            var single = await _db.Users.FindAsync(id);
            if (single == null)
                return new Response<User>(false, "Not Found", null);
           
                return new Response<User>(true, null, single);   
        }

        [HttpPost]
        public async Task<ActionResult<User>> Post(User User)
        {
            _db.Users.Update(User);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = User.Id }, User);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, User User)
        {
            if (id != User.Id)
                return BadRequest();

            _db.Entry(User).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Users.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Users.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
