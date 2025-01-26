using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AgentController : ControllerBase
    {
        private readonly Context _context;

        public AgentController(Context context)
        {
            _context = context;
        }

        // GET: api/Agent
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Agent>>> GetAllAgents()
        {
            return await _context.Agents.ToListAsync();
        }

        // GET: api/Agent/Names
        [HttpGet("Names")]
        public async Task<ActionResult<IEnumerable<string>>> GetAllAgentNames()
        {
            var agentNames = await _context.Agents.Select(agent => agent.Name).ToListAsync();
            return agentNames;
        }

        // GET: api/Agent/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Agent>> GetAgent(int id)
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound();
            }

            return agent;
        }

        // POST: api/Agent
        [HttpPost]
        public async Task<ActionResult<Agent>> PostAgent(Agent agent)
        {
            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetAgent", new { id = agent.Id }, agent);
        }

        // PUT: api/Agent/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAgent(int id, Agent agent)
        {
            if (id != agent.Id)
            {
                return BadRequest();
            }

            _context.Entry(agent).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AgentExists(id))
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

        // DELETE: api/Agent/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAgent(int id)
        {
            var agent = await _context.Agents.FindAsync(id);
            if (agent == null)
            {
                return NotFound();
            }

            _context.Agents.Remove(agent);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool AgentExists(int id)
        {
            return _context.Agents.Any(e => e.Id == id);
        }

        // GET: api/Agent/AgentAlert
        [HttpGet("AgentAlert")]
        public async Task<IEnumerable<string>> GetLatestPatientAgentsNotInAgentTableAsync()
        {
            var latestAgents = await _context.Childs
                .OrderByDescending(p => p.Id)
                .Take(3)
                .Select(p => p.Agent)
                .ToListAsync();

            var existingAgents = await _context.Agents
                .Where(c => latestAgents.Contains(c.Name))
                .Select(c => c.Name)
                .ToListAsync();

            var agentsNotInAgentTable = latestAgents.Except(existingAgents);

            return agentsNotInAgentTable;
        }

        // PUT: api/Agent/update
        [HttpPut("update")]
        public async Task<ActionResult<Response<object>>> UpdateChildAgent(string currentAgent, [FromBody] string newAgent)
        {
            var alreadyAgent = await _context.Agents.FirstOrDefaultAsync(c => c.Name == newAgent);
            if (alreadyAgent != null)
            {
                return BadRequest(new Response<object>(false, "Cannot update the agent because it already exists.", null));
            }

            var childs = await _context.Childs.Where(c => c.Agent == currentAgent).ToListAsync();
            if (childs == null || !childs.Any())
            {
                return NotFound();
            }

            foreach (var child in childs)
            {
                // Update the agent of each child with the new agent
                child.Agent = newAgent;
                _context.Childs.Update(child);
            }

            if (alreadyAgent == null)
            {
                var agent = new Agent { Name = newAgent };
                _context.Agents.Add(agent);
            }

            await _context.SaveChangesAsync();

            return Ok(new Response<object>(true, "Agent updated successfully.", null));
        }
    }
}