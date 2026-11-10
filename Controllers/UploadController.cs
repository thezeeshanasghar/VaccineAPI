using System;
using System.IO;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {

        [HttpPost, RequestSizeLimit(10 * 1024 * 1024)]
        public IActionResult Upload()
        {
            try
            {
                if (Request.Form?.Files == null || Request.Form.Files.Count == 0)
                {
                    return BadRequest("No file uploaded.");
                }

                var file = Request.Form.Files[0];
                var folderName = Path.Combine("Resources", "Images");
                var pathToSave = Path.Combine(Directory.GetCurrentDirectory(), folderName);

                if (file.Length > 0)
                {
                    // The client-supplied name is untrusted: it used to be joined into the path as-is ("../" could write anywhere,
                    // and any extension was accepted). Keep only a whitelisted extension and generate the name.
                    var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
                    if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".gif" && ext != ".webp" && ext != ".pdf")
                        return BadRequest("Unsupported file type.");
                    var fileName = Guid.NewGuid().ToString("N") + ext;
                    var fullPath = Path.Combine(pathToSave, fileName);
                    var dbPath = Path.Combine(folderName, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }
                    return Ok(new { dbPath });
                }
                else
                {
                    return BadRequest();
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Internal server error." + ex);
            }
        }
    }
}