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
    public class MessageController : ControllerBase
    {
        private readonly Context _db;

        public MessageController(Context context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Message>>> GetAll()
        {
            return await _db.Messages.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Message>> GetSingle(long id)
        {
            var single = await _db.Messages.FindAsync(id);
            if (single == null)
                return NotFound();

            return single;
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
