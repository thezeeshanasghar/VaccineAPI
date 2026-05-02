using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

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

        // GET: api/Agent/{id}/report?from=2024-01-01&to=2024-12-31
        [HttpGet("{id}/report")]
        public async Task<ActionResult<object>> GetAgentReport(int id, [FromQuery] DateTime from, [FromQuery] DateTime to)
        {
            var agent = await _context.Agents.FindAsync(id);
            if (agent == null) return NotFound();

            var travelChildren = await _context.Childs
                .Where(c => c.Type == "Travel" && c.Agent == agent.Name)
                .Include(c => c.Schedules)
                .ToListAsync();

            var clientsInRange = travelChildren
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Guardian,
                    FirstVisitDate = c.Schedules
                        .Where(s => s.IsDone && s.GivenDate.HasValue)
                        .OrderBy(s => s.GivenDate)
                        .Select(s => s.GivenDate)
                        .FirstOrDefault()
                })
                .Where(x => x.FirstVisitDate.HasValue
                            && x.FirstVisitDate.Value.Date >= from.Date
                            && x.FirstVisitDate.Value.Date <= to.Date)
                .ToList();

            return Ok(new
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                PhoneNumber = agent.PhoneNumber,
                ReferralFeePerClient = agent.ReferralFeePerClient,
                From = from.ToString("yyyy-MM-dd"),
                To = to.ToString("yyyy-MM-dd"),
                ClientCount = clientsInRange.Count,
                TotalFee = clientsInRange.Count * agent.ReferralFeePerClient,
                Clients = clientsInRange.Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Guardian,
                    FirstVisitDate = x.FirstVisitDate.Value.ToString("yyyy-MM-dd")
                })
            });
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
        public async Task<ActionResult<Response<Object>>> UpdateAgent([FromBody] string newAgent)
        {
            if (string.IsNullOrWhiteSpace(newAgent))
            {
                return BadRequest(new Response<Object>(false, "Agent name cannot be empty.", null));
            }

            // Check if agent already exists
            var existingAgent = await _context.Agents.FirstOrDefaultAsync(c => c.Name.ToLower() == newAgent.ToLower());
            if (existingAgent != null)
            {
                return BadRequest(new Response<Object>(false, $"Agent '{newAgent}' already exists.", null));
            }

            // Add new agent to Agents table
            var agent = new Agent { Name = newAgent };
            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            return Ok(new Response<object>(true, "Agent added successfully.", null));
        }
    }
}