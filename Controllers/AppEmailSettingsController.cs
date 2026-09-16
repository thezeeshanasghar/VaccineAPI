using System.Linq;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    // Admin-managed default/fallback SMTP sender (e.g. info@vaccinationcentre.com),
    // used for any doctor without their own AllowOwnEmail SMTP settings configured.
    // Single row — created on first save if it doesn't exist yet.
    [Route("api/[controller]")]
    [ApiController]
    public class AppEmailSettingsController : ControllerBase
    {
        private readonly Context _db;

        public AppEmailSettingsController(Context db)
        {
            _db = db;
        }

        [HttpGet]
        public ActionResult<AppEmailSettingDTO> Get()
        {
            var setting = _db.AppEmailSettings.FirstOrDefault();
            if (setting == null)
            {
                return Ok(new AppEmailSettingDTO());
            }

            return Ok(new AppEmailSettingDTO
            {
                Host = setting.Host,
                Port = setting.Port,
                UseSsl = setting.UseSsl,
                Username = setting.Username,
                Password = setting.Password,
                FromEmail = setting.FromEmail,
                FromName = setting.FromName
            });
        }

        [HttpPut]
        public ActionResult<Response<AppEmailSettingDTO>> Save([FromBody] AppEmailSettingDTO dto)
        {
            var setting = _db.AppEmailSettings.FirstOrDefault();
            if (setting == null)
            {
                setting = new AppEmailSetting();
                _db.AppEmailSettings.Add(setting);
            }

            setting.Host = dto.Host;
            setting.Port = dto.Port;
            setting.UseSsl = dto.UseSsl;
            setting.Username = dto.Username;
            setting.Password = dto.Password;
            setting.FromEmail = dto.FromEmail;
            setting.FromName = dto.FromName;
            _db.SaveChanges();

            return Ok(new Response<AppEmailSettingDTO>(true, null, dto));
        }
    }
}
