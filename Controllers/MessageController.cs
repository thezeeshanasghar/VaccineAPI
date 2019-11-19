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
    public class MessageController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public MessageController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
         public async Task<Response<List<MessageDTO>>> GetAll()
        {
            var list = await _db.Messages.OrderBy(x=>x.Id).ToListAsync();
            List<MessageDTO> listDTO = _mapper.Map<List<MessageDTO>>(list);
           
            return new Response<List<MessageDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<Message>> GetSingle(long id)
        {
            var single = await _db.Messages.FindAsync(id);
            if (single == null)
                 return new Response<Message>(false, "Not Found", null);
           
                 return new Response<Message>(true, null, single);

        
        }

        [HttpPost]
        public async Task<ActionResult<Message>> Post(Message Message)
        {
            _db.Messages.Update(Message);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSingle), new { id = Message.Id }, Message);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, Message Message)
        {
            if (id != Message.Id)
                return BadRequest();

            _db.Entry(Message).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Messages.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Messages.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
