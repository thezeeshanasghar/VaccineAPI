using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    // Registers / removes the FCM token of the phone the caller is logged in on. The owner is taken
    // from the session token, never from the body, except for old clients while the API is in Log mode.
    [Route("api/[controller]")]
    [ApiController]
    public class DeviceController : ControllerBase
    {
        private readonly Context _db;
        public DeviceController(Context db) { _db = db; }

        public class DeviceDto
        {
            public string Token { get; set; } = "";
            public string? Platform { get; set; }
            public string? AppFlavor { get; set; }
            // Only honoured when the request carries no session token (Log mode).
            public string? RecipientType { get; set; }
            public long? RecipientId { get; set; }
        }

        private static (string type, long id)? Owner(DeviceDto dto)
        {
            var id = AuthContext.Current;
            if (id == null)
            {
                if (AuthContext.Enforcing || dto.RecipientId == null || string.IsNullOrEmpty(dto.RecipientType)) return null;
                var t = dto.RecipientType.ToUpperInvariant();
                return t == "PARENT" || t == "DOCTOR" || t == "PA" || t == "AGENT" ? (t, dto.RecipientId.Value) : null;
            }
            switch (id.Role)
            {
                case "PARENT": return ("PARENT", id.UserId);
                case "AGENT": return ("AGENT", id.UserId);
                case "DOCTOR": return id.DoctorId.HasValue ? ("DOCTOR", id.DoctorId.Value) : null;
                case "PA": return id.PaId.HasValue ? ("PA", id.PaId.Value) : null;
                default: return null;
            }
        }

        [HttpPost("register")]
        public async Task<ActionResult> Register([FromBody] DeviceDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Token) || dto.Token.Length > 500)
                return Ok(new { IsSuccess = false, Message = "Token missing." });
            var owner = Owner(dto);
            if (owner == null) return Ok(new { IsSuccess = false, Message = "No push recipient for this login." });

            var row = await _db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == dto.Token);
            if (row == null)
            {
                row = new DeviceToken { Token = dto.Token, CreatedAt = DateTime.UtcNow };
                _db.DeviceTokens.Add(row);
            }
            row.RecipientType = owner.Value.type;
            row.RecipientId = owner.Value.id;
            row.Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "android" : dto.Platform!.Trim().ToLowerInvariant();
            row.AppFlavor = (dto.AppFlavor ?? "").Trim();
            row.LastSeenAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* same token registered twice at once; the other call won */ }
            return Ok(new { IsSuccess = true, Message = "Registered." });
        }

        // Called on logout so the next person on a shared phone does not receive the previous user's pushes.
        [HttpPost("unregister")]
        public async Task<ActionResult> Unregister([FromBody] DeviceDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Token)) return Ok(new { IsSuccess = true });
            var owner = Owner(dto);
            var row = await _db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == dto.Token);
            if (row != null && owner != null && row.RecipientType == owner.Value.type && row.RecipientId == owner.Value.id)
            {
                _db.DeviceTokens.Remove(row);
                await _db.SaveChangesAsync();
            }
            return Ok(new { IsSuccess = true });
        }

        // Who can and cannot receive pushes. A doctor/manager sees their own PAs; Log-mode callers without a
        // session token pass doctorId (same trust level as the rest of the API in that mode).
        [HttpGet("status")]
        public async Task<ActionResult> Status([FromQuery] long? doctorId)
        {
            var id = AuthContext.Current;
            long? doc = id != null ? id.DoctorId : doctorId;
            if (id == null && AuthContext.Enforcing) return Unauthorized();
            if (id != null && id.Role != "DOCTOR" && id.Role != "MANAGER" && id.Role != "SUPERADMIN") return Forbid();
            if (doc == null) return Ok(new { IsSuccess = false, Message = "doctorId required." });

            var pas = await _db.PersonalAssistant.Where(p => p.DoctorId == doc.Value && p.IsActive)
                .Select(p => new { p.Id, p.Name }).ToListAsync();
            var ids = pas.Select(p => p.Id).ToList();
            var tokens = await _db.DeviceTokens.Where(t => t.RecipientType == "PA" && ids.Contains(t.RecipientId))
                .GroupBy(t => t.RecipientId)
                .Select(g => new { PaId = g.Key, Count = g.Count(), LastSeen = g.Max(t => t.LastSeenAt), Flavor = g.Max(t => t.AppFlavor) })
                .ToListAsync();
            var since = DateTime.UtcNow.AddDays(-30);
            var attempts = await _db.PushAttempts.Where(a => a.RecipientType == "PA" && ids.Contains(a.RecipientId) && a.CreatedAt >= since)
                .Select(a => new { a.RecipientId, a.Result, a.Type, a.CreatedAt }).ToListAsync();
            var rows = pas.Select(p =>
            {
                var t = tokens.FirstOrDefault(x => x.PaId == p.Id);
                var last = attempts.Where(a => a.RecipientId == p.Id).OrderByDescending(a => a.CreatedAt).FirstOrDefault();
                return new
                {
                    PaId = p.Id, p.Name,
                    Tokens = t?.Count ?? 0, LastSeenAt = t?.LastSeen, AppFlavor = t?.Flavor,
                    CanReceivePush = (t?.Count ?? 0) > 0,
                    LastPushResult = last?.Result, LastPushType = last?.Type, LastPushAt = last?.CreatedAt
                };
            }).OrderBy(r => r.CanReceivePush).ThenBy(r => r.Name).ToList();
            return Ok(new { IsSuccess = true, PushEnabled = VaccineAPI.Services.PushService.Enabled, ResponseData = rows });
        }

        // Recent attempts for one recipient, newest first: the answer to "why didn't X get it".
        [HttpGet("attempts")]
        public async Task<ActionResult> Attempts([FromQuery] string recipientType, [FromQuery] long recipientId, [FromQuery] int take = 50)
        {
            var id = AuthContext.Current;
            if (id == null && AuthContext.Enforcing) return Unauthorized();
            if (id != null && id.Role != "DOCTOR" && id.Role != "MANAGER" && id.Role != "SUPERADMIN") return Forbid();
            var type = (recipientType ?? "").ToUpperInvariant();
            if (id != null && id.Role != "SUPERADMIN" && id.DoctorId.HasValue)
            {
                // A doctor/manager may only look at their own PAs.
                bool own = type == "PA" && await _db.PersonalAssistant.AnyAsync(p => p.Id == recipientId && p.DoctorId == id.DoctorId.Value);
                if (!own) return Forbid();
            }
            take = Math.Clamp(take, 1, 200);
            var rows = await _db.PushAttempts.Where(a => a.RecipientType == type && a.RecipientId == recipientId)
                .OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync();
            return Ok(new { IsSuccess = true, ResponseData = rows });
        }
    }
}
