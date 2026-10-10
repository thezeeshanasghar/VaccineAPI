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
    }
}
