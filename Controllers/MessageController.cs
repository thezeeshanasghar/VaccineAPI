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
        // public async Task<Response<List<MessageDTO>>> GetAll()
        public Response<List<MessageDTO>> Get([FromQuery]string mobileNumber = "", [FromQuery]string fromDate = "", [FromQuery]string toDate = "")
        {
            List<Message> dbMessages = new List<Message>();
            var prevDays = DateTime.Now.AddDays(-10);
            if (!string.IsNullOrEmpty(mobileNumber) || !string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate))
            {
                var dbUser = _db.Users.Where(x => x.MobileNumber == mobileNumber && x.UserType == "DOCTOR").FirstOrDefault();
                if (dbUser == null)
                    return new Response<List<MessageDTO>>(false, "No records found", null);
                if (fromDate != null && toDate == null)
                {
                    DateTime FromDate = DateTime.ParseExact(fromDate, "dd-MM-yyyy", null);
                    dbMessages = _db.Messages.Where(m => m.UserId == dbUser.Id && m.Created >= FromDate).ToList();
                }
                if (toDate != null && fromDate == null)
                {
                    DateTime ToDate = DateTime.ParseExact(toDate, "dd-MM-yyyy", null);
                    dbMessages = _db.Messages.Where(m => m.UserId == dbUser.Id &&
                                    m.Created <= ToDate).ToList();
                }
                if (toDate != null && fromDate != null)
                {
                    DateTime FromDate = DateTime.ParseExact(fromDate, "dd-MM-yyyy", null);
                    DateTime ToDate = DateTime.ParseExact(toDate, "dd-MM-yyyy", null);

                    dbMessages = _db.Messages.Where(m => m.UserId == dbUser.Id && m.Created >= FromDate &&
                                    m.Created <= ToDate).ToList();

                }
                if (toDate == null && fromDate == null)
                {
                    dbMessages = _db.Messages.Where(m => m.UserId == dbUser.Id && m.Created > prevDays).ToList();
                }

            }
            else
            {

                dbMessages = _db.Messages.Where(m => m.Created > prevDays).ToList();
            }


            var messageDTOs = _mapper.Map<List<MessageDTO>>(dbMessages.OrderByDescending(x => x.Created));
            return new Response<List<MessageDTO>>(true, null, messageDTOs);
        }
          protected bool IsJson(string input)
        {
            input = input.Trim();
            return input.StartsWith("{") && input.EndsWith("}")
                   || input.StartsWith("[") && input.EndsWith("]");
        }

         [HttpGet("{id}/doctor")]
        public Response<List<MessageDTO>> Get(int id)
        {
    
    
                {
                    var dbMessages = _db.Messages.Where(x => x.UserId == id).Include(x=>x.User).OrderByDescending(x => x.Created).ToList();
                    var messageDTOs = _mapper.Map<List<MessageDTO>>(dbMessages);
                    // foreach (var msg in messageDTOs)
                    // {
                    //     if (IsJson(msg.ApiResponse))
                    //     {
                    //         JObject json = JObject.Parse(msg.ApiResponse);
                    //         msg.ApiResponse = (string)json["returnString"];
                    //     }
                    //     else
                    //     {
                    //         XmlDocument xmlDoc = new XmlDocument();
                    //         xmlDoc.LoadXml(msg.ApiResponse);

                    //         string xpath = "Response";
                    //         var parentNode = xmlDoc.SelectNodes(xpath);

                    //         foreach (XmlNode childrenNode in parentNode)
                    //             msg.ApiResponse = childrenNode.FirstChild.InnerText;
                    //     }
                    // }

                    return new Response<List<MessageDTO>>(true, null, messageDTOs);
                }
            }

            

        [HttpPost]
        public Response<MessageDTO> Post([FromBody] MessageDTO msg)
        {
            if (!string.IsNullOrEmpty(msg.SMS) && !string.IsNullOrEmpty(msg.MobileNumber))
            {
                UserSMS u = new UserSMS(_db);
                var response = u.SendSMS("92", msg.MobileNumber, "", msg.SMS);
                u.addMessageToDB(msg.MobileNumber, response, msg.SMS, 1);
                return new Response<MessageDTO>(true, null, null);
            }
            else
            {
                return new Response<MessageDTO>(false, "Please fill sms and mobile number text fields", null);
            }
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
