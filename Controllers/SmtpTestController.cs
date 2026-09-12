using System;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;

namespace VaccineAPI.Controllers
{
    public class SmtpTestRequest
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public bool UseSsl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FromEmail { get; set; }
        public string FromName { get; set; }
        public string ToEmail { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class SmtpTestController : ControllerBase
    {
        [HttpPost("send")]
        public IActionResult Send([FromBody] SmtpTestRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { IsSuccess = false, Message = "Request body is required." });
            }
            if (string.IsNullOrWhiteSpace(request.Host) || request.Port <= 0
                || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(request.FromEmail) || string.IsNullOrWhiteSpace(request.ToEmail))
            {
                return BadRequest(new { IsSuccess = false, Message = "Host, Port, Username, Password, FromEmail, and ToEmail are all required." });
            }

            try
            {
                using (var client = new SmtpClient(request.Host, request.Port))
                {
                    client.Credentials = new NetworkCredential(request.Username, request.Password);
                    client.EnableSsl = request.UseSsl;
                    client.Timeout = 15000;

                    using (var message = new MailMessage())
                    {
                        message.From = new MailAddress(request.FromEmail, string.IsNullOrWhiteSpace(request.FromName) ? request.FromEmail : request.FromName);
                        message.To.Add(request.ToEmail);
                        message.Subject = string.IsNullOrWhiteSpace(request.Subject) ? "SMTP Test Email" : request.Subject;
                        message.Body = string.IsNullOrWhiteSpace(request.Body) ? "This is a test email sent from VaccineAPI to verify SMTP settings." : request.Body;
                        message.IsBodyHtml = false;

                        client.Send(message);
                    }
                }

                return Ok(new { IsSuccess = true, Message = "Test email sent successfully to " + request.ToEmail + "." });
            }
            catch (Exception ex)
            {
                Console.WriteLine("SmtpTest.Send failed: " + ex);
                return Ok(new { IsSuccess = false, Message = ex.GetType().Name + ": " + ex.Message });
            }
        }
    }
}
