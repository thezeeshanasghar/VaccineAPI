using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
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

        // POST: api/Agent — doctor-side "Add Agent". Name + Phone only; every new agent gets
        // the default PIN "0000" and must change it on first VacAgent login (MustChangePassword).
        [HttpPost]
        public async Task<ActionResult<Agent>> PostAgent(Agent agent)
        {
            if (string.IsNullOrWhiteSpace(agent.Name) || string.IsNullOrWhiteSpace(agent.PhoneNumber))
                return BadRequest(new { IsSuccess = false, Message = "Agent name and phone number are required." });

            var existing = await _context.Agents.FirstOrDefaultAsync(a => a.PhoneNumber == agent.PhoneNumber);
            if (existing != null)
                return BadRequest(new { IsSuccess = false, Message = "An agent with this phone number already exists." });

            agent.Password = "0000";
            agent.MustChangePassword = true;
            agent.Email = agent.Email ?? "";
            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();
            agent.AgentCode = $"{DateTime.UtcNow.AddHours(5).Year}-{agent.Id}";
            await _context.SaveChangesAsync();
            return CreatedAtAction("GetAgent", new { id = agent.Id }, agent);
        }

        // PUT: api/Agent/5 — doctor-side "Edit Agent" (Name/Phone/ReferralFeePerClient only).
        // Merges onto the existing row rather than replacing it wholesale, so this endpoint
        // never needs the agent's Password/MustChangePassword round-tripped from the client —
        // those are owned exclusively by login/change-password.
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAgent(int id, Agent agent)
        {
            if (id != agent.Id)
            {
                return BadRequest();
            }

            var dbAgent = await _context.Agents.FindAsync(id);
            if (dbAgent == null) return NotFound();

            dbAgent.Name = agent.Name;
            dbAgent.PhoneNumber = agent.PhoneNumber;
            dbAgent.ReferralFeePerClient = agent.ReferralFeePerClient;
            dbAgent.ClinicId = agent.ClinicId;
            dbAgent.CanRegisterRegular = agent.CanRegisterRegular;
            dbAgent.CanRegisterEPI = agent.CanRegisterEPI;
            dbAgent.CanRegisterCustomize = agent.CanRegisterCustomize;
            dbAgent.CanRegisterTravel = agent.CanRegisterTravel;

            await _context.SaveChangesAsync();

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
        //
        // Referral fee rule: an agent earns a fee only for a patient's FIRST-EVER given dose
        // that was billed on an invoice (Schedule.IsDone && Schedule.InvoiceSubmissionId != null,
        // earliest GivenDate). Any later dose/visit for that same patient earns nothing further,
        // regardless of this report's date range — so "eligible" here means that first-ever
        // qualifying dose specifically fell inside [from, to], not just any dose in range.
        // Fee amount: AgentVaccineFeeOverride for that dose's vaccine if one exists, else the
        // agent's flat ReferralFeePerClient.
        [HttpGet("{id}/report")]
        public async Task<ActionResult<object>> GetAgentReport(int id, [FromQuery] DateTime from, [FromQuery] DateTime to)
        {
            var agent = await _context.Agents.FindAsync(id);
            if (agent == null) return NotFound();

            var overrides = await _context.AgentVaccineFeeOverrides
                .Where(o => o.AgentId == id)
                .ToDictionaryAsync(o => o.VaccineId, o => o.Fee);

            var referredChildren = await _context.Childs
                .Where(c => c.AgentId == id)
                .Include(c => c.Schedules)
                    .ThenInclude(s => s.Dose)
                .ToListAsync();

            var evaluated = referredChildren
                .Select(c =>
                {
                    var firstQualifying = c.Schedules
                        .Where(s => s.IsDone && s.InvoiceSubmissionId.HasValue && s.GivenDate.HasValue)
                        .OrderBy(s => s.GivenDate)
                        .FirstOrDefault();

                    long? vaccineId = firstQualifying?.Dose?.VaccineId;
                    decimal fee = 0;
                    if (vaccineId.HasValue)
                        fee = overrides.TryGetValue(vaccineId.Value, out var overrideFee) ? overrideFee : agent.ReferralFeePerClient;

                    return new
                    {
                        c.Id,
                        c.Name,
                        c.Guardian,
                        FirstDoseGivenDate = firstQualifying?.GivenDate,
                        Fee = fee
                    };
                })
                .ToList();

            var eligibleInRange = evaluated
                .Where(x => x.FirstDoseGivenDate.HasValue
                            && x.FirstDoseGivenDate.Value.Date >= from.Date
                            && x.FirstDoseGivenDate.Value.Date <= to.Date)
                .ToList();

            return Ok(new
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                PhoneNumber = agent.PhoneNumber,
                ReferralFeePerClient = agent.ReferralFeePerClient,
                From = from.ToString("yyyy-MM-dd"),
                To = to.ToString("yyyy-MM-dd"),
                TotalReferred = referredChildren.Count,
                ClientCount = eligibleInRange.Count,
                TotalFee = eligibleInRange.Sum(x => x.Fee),
                Clients = eligibleInRange.Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Guardian,
                    FirstVisitDate = x.FirstDoseGivenDate.Value.ToString("yyyy-MM-dd"),
                    Fee = x.Fee
                })
            });
        }

        // GET: api/Agent/{id}/clients?query=... — the agent's OWN referred children only
        // (Child.AgentId == id, the same referral-attribution FK the report/summary endpoints
        // use), never any other patient at the clinic. query is optional: blank returns the
        // agent's most-recent referrals; non-blank filters by name/guardian/mobile, case-
        // insensitive substring match, no MR/CNIC lookup here (that's agent-search's job for
        // Travel-only verification, a different, non-ownership-scoped use case).
        [HttpGet("{id}/clients")]
        public async Task<ActionResult<object>> GetAgentClients(int id, [FromQuery] string query)
        {
            var agent = await _context.Agents.FindAsync(id);
            if (agent == null) return NotFound(new { IsSuccess = false, Message = "Agent not found." });

            var childrenQuery = _context.Childs
                .Include(c => c.User)
                .Where(c => c.AgentId == id);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim().ToLower();
                childrenQuery = childrenQuery.Where(c =>
                    (c.Name != null && c.Name.ToLower().Contains(q)) ||
                    (c.FatherName != null && c.FatherName.ToLower().Contains(q)) ||
                    (c.User != null && c.User.MobileNumber != null && c.User.MobileNumber.Contains(q)));
            }

            var children = await childrenQuery
                .OrderByDescending(c => c.Id)
                .Take(100)
                .ToListAsync();

            return Ok(new
            {
                IsSuccess = true,
                ResponseData = children.Select(c => new
                {
                    c.Id,
                    c.Name,
                    Guardian = c.FatherName,
                    c.Gender,
                    DOB = c.DOB.ToString("yyyy-MM-dd"),
                    MobileNumber = c.User != null ? c.User.MobileNumber : null,
                    c.Type
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

        // POST: api/Agent/login
        [HttpPost("login")]
        public async Task<ActionResult<object>> LoginAgent([FromBody] AgentLoginDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.PhoneNumber) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { IsSuccess = false, Message = "Phone number and password are required." });

            var agent = await _context.Agents
                .FirstOrDefaultAsync(a => a.PhoneNumber == dto.PhoneNumber && a.Password == dto.Password);

            if (agent == null)
                return Unauthorized(new { IsSuccess = false, Message = "Invalid phone number or password." });

            return Ok(new
            {
                IsSuccess = true,
                Message = "Login successful.",
                ResponseData = new
                {
                    agent.Id,
                    agent.Name,
                    agent.PhoneNumber,
                    agent.Email,
                    agent.AgentCode,
                    agent.ReferralFeePerClient,
                    agent.MustChangePassword,
                    agent.ClinicId,
                    agent.CanRegisterRegular,
                    agent.CanRegisterEPI,
                    agent.CanRegisterCustomize,
                    agent.CanRegisterTravel
                }
            });
        }

        // PUT: api/Agent/change-password
        [HttpPut("change-password")]
        public async Task<ActionResult<object>> ChangePassword([FromBody] AgentChangePasswordDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.PhoneNumber) || string.IsNullOrWhiteSpace(dto.OldPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { IsSuccess = false, Message = "All fields are required." });

            if (dto.NewPassword.Length < 4)
                return BadRequest(new { IsSuccess = false, Message = "New password must be at least 4 characters." });

            var agent = await _context.Agents
                .FirstOrDefaultAsync(a => a.PhoneNumber == dto.PhoneNumber && a.Password == dto.OldPassword);

            if (agent == null)
                return Unauthorized(new { IsSuccess = false, Message = "Current password is incorrect." });

            agent.Password = dto.NewPassword;
            agent.MustChangePassword = false;
            await _context.SaveChangesAsync();
            return Ok(new { IsSuccess = true, Message = "Password changed successfully." });
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

        // GET: api/Agent/summary — powers the Agent Module list page's per-agent stat columns
        // (referred / availed-1st-dose / owed-to-date, all-time, no date range). "Owed" here
        // is a running total, independent of whether it's been paid out via the monthly report.
        [HttpGet("summary")]
        public async Task<ActionResult<object>> GetAgentsSummary()
        {
            var agents = await _context.Agents.ToListAsync();
            var overridesByAgent = await _context.AgentVaccineFeeOverrides
                .GroupBy(o => o.AgentId)
                .ToDictionaryAsync(g => g.Key, g => g.ToDictionary(o => o.VaccineId, o => o.Fee));
            // Raw SELECT of just Id/Name — some prod rows have a NULL MonogramImage
            // (declared non-nullable string on the Clinic entity), which makes EF's
            // normal DbSet query materialize the full entity and throw InvalidCastException
            // on that column. This endpoint only needs the name, so it never touches that column.
            // Use EF's connection-management wrapper (ref-counted open/close) rather than the
            // raw ADO connection directly — calling OpenAsync/CloseAsync on the raw connection
            // ourselves fights EF's own open/close around the other _context calls in this
            // method and throws ObjectDisposedException on whichever runs next.
            var clinicNamesById = new Dictionary<long, string>();
            await _context.Database.OpenConnectionAsync();
            try
            {
                using var cmd = _context.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "SELECT Id, Name FROM clinics";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    clinicNamesById[reader.GetInt64(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            var children = await _context.Childs
                .Where(c => c.AgentId.HasValue)
                .Include(c => c.Schedules)
                    .ThenInclude(s => s.Dose)
                .ToListAsync();

            var byAgentChildren = children.GroupBy(c => c.AgentId!.Value).ToDictionary(g => g.Key, g => g.ToList());

            var result = agents.Select(agent =>
            {
                var referred = byAgentChildren.TryGetValue(agent.Id, out var list) ? list : new List<Child>();
                var agentOverrides = overridesByAgent.TryGetValue(agent.Id, out var ov) ? ov : new Dictionary<long, decimal>();

                int availed = 0;
                decimal owed = 0;
                foreach (var child in referred)
                {
                    var firstQualifying = child.Schedules
                        .Where(s => s.IsDone && s.InvoiceSubmissionId.HasValue && s.GivenDate.HasValue)
                        .OrderBy(s => s.GivenDate)
                        .FirstOrDefault();
                    if (firstQualifying == null) continue;

                    availed++;
                    var vaccineId = firstQualifying.Dose?.VaccineId;
                    owed += (vaccineId.HasValue && agentOverrides.TryGetValue(vaccineId.Value, out var fee))
                        ? fee
                        : agent.ReferralFeePerClient;
                }

                return new
                {
                    agent.Id,
                    agent.Name,
                    agent.PhoneNumber,
                    agent.AgentCode,
                    agent.ReferralFeePerClient,
                    agent.MustChangePassword,
                    agent.ClinicId,
                    ClinicName = agent.ClinicId.HasValue && clinicNamesById.TryGetValue(agent.ClinicId.Value, out var cname) ? cname : null,
                    OverrideCount = agentOverrides.Count,
                    ReferredCount = referred.Count,
                    AvailedCount = availed,
                    Owed = owed
                };
            }).ToList();

            return Ok(new
            {
                IsSuccess = true,
                ResponseData = new
                {
                    ActiveAgents = agents.Count,
                    TotalReferred = result.Sum(r => r.ReferredCount),
                    TotalAvailed = result.Sum(r => r.AvailedCount),
                    TotalOwed = result.Sum(r => r.Owed),
                    Agents = result
                }
            });
        }

        // GET: api/Agent/{id}/fee-overrides
        [HttpGet("{id}/fee-overrides")]
        public async Task<ActionResult<IEnumerable<AgentVaccineFeeOverride>>> GetFeeOverrides(int id)
        {
            return await _context.AgentVaccineFeeOverrides.Where(o => o.AgentId == id).ToListAsync();
        }

        // PUT: api/Agent/{id}/fee-overrides/{vaccineId} — upsert a per-vaccine fee override.
        [HttpPut("{id}/fee-overrides/{vaccineId}")]
        public async Task<ActionResult<object>> UpsertFeeOverride(int id, long vaccineId, [FromBody] AgentFeeOverrideDTO dto)
        {
            var agentExists = await _context.Agents.AnyAsync(a => a.Id == id);
            if (!agentExists) return NotFound(new { IsSuccess = false, Message = "Agent not found." });

            var existing = await _context.AgentVaccineFeeOverrides
                .FirstOrDefaultAsync(o => o.AgentId == id && o.VaccineId == vaccineId);

            if (existing != null)
            {
                existing.Fee = dto.Fee;
            }
            else
            {
                _context.AgentVaccineFeeOverrides.Add(new AgentVaccineFeeOverride
                {
                    AgentId = id,
                    VaccineId = vaccineId,
                    Fee = dto.Fee
                });
            }
            await _context.SaveChangesAsync();
            return Ok(new { IsSuccess = true, Message = "Fee override saved." });
        }

        // DELETE: api/Agent/{id}/fee-overrides/{vaccineId}
        [HttpDelete("{id}/fee-overrides/{vaccineId}")]
        public async Task<IActionResult> DeleteFeeOverride(int id, long vaccineId)
        {
            var existing = await _context.AgentVaccineFeeOverrides
                .FirstOrDefaultAsync(o => o.AgentId == id && o.VaccineId == vaccineId);
            if (existing == null) return NotFound();

            _context.AgentVaccineFeeOverrides.Remove(existing);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}