using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using AutoMapper;
using System;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public UserController(Context context, IMapper mapper, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _db = context;
            _mapper = mapper;
            _config = config;
        }

        private string LinkLoginSecret()
        {
            return _config["LinkLogin:Secret"]
                ?? Environment.GetEnvironmentVariable("LinkLoginSecret")
                ?? "";
        }

        // The raw user table (passwords, stamps) is for the super admin only; no app screen reads it.
        [HttpGet]
        public async Task<Response<List<UserDTO>>> GetAll()
        {
            if (!AuthContext.IsSuperAdmin)
                return new Response<List<UserDTO>>(false, "Not allowed.", null);

            var list = await _db.Users.OrderBy(x => x.Id).ToListAsync();
            List<UserDTO> listDTO = _mapper.Map<List<UserDTO>>(list);
            foreach (var u in listDTO) { u.Password = ""; u.SecurityStamp = ""; }

            return new Response<List<UserDTO>>(true, null, listDTO);
        }

        [HttpGet("{id}")]
        public async Task<Response<UserDTO>> GetSingle(long id)
        {
            if (!AuthContext.IsSuperAdmin && !(AuthContext.Current != null && AuthContext.Current.UserId == id))
                return new Response<UserDTO>(false, "Not allowed.", null);

            var single = await _db.Users.FindAsync(id);
            if (single == null)
                return new Response<UserDTO>(false, "Not Found", null);

            var dto = _mapper.Map<UserDTO>(single);
            dto.Password = "";
            dto.SecurityStamp = "";
            return new Response<UserDTO>(true, null, dto);
        }

        // GET api/user/{mobile}/{dob} used to return the full user row (with password) to anyone who knew
        // a mobile number and a child's date of birth. No app calls it, so it is removed.

        //  [HttpGet("checkUniqueMobile")]
        //  public HttpResponseMessage CheckUniqueMobile(string MobileNumber)
        //  {
        //         {
        //             User userDB = _db.Users.Where(x => x.MobileNumber == MobileNumber)
        //                 .Where(u =>u.UserType== "DOCTOR").FirstOrDefault();
        //             if (userDB == null)
        //              //   return Request.CreateResponse((HttpStatusCode)200);
        //             else
        //             {

        //                 int HTTPResponse = 400;
        //               //  var response = Request.CreateResponse((HttpStatusCode)HTTPResponse);
        //                 response.ReasonPhrase = "Mobile Number already in use";
        //                 return response;
        //             }
        //         }

        //  }


        [HttpPost("login")]
        public Response<UserDTO> login(UserDTO userDTO)
        {
            string throttleKey = "u:" + userDTO.CountryCode + userDTO.MobileNumber;
            if (LoginThrottle.IsBlocked(throttleKey))
                return new Response<UserDTO>(false, "Too many failed attempts. Please try again in 15 minutes.", null);

            var dbUser = _db.Users.FirstOrDefault(
                x =>
                    x.MobileNumber == userDTO.MobileNumber
                    && x.Password == userDTO.Password
                    && x.CountryCode == userDTO.CountryCode
                    && x.UserType == userDTO.UserType
            );

            // Manager logs in through the same PA login form (no visible role picker) — the
            // frontend always sends UserType "PA". If the PA-typed lookup finds nothing, retry
            // the identical credentials against UserType "MANAGER" before failing.
            if (dbUser == null && userDTO.UserType == "PA")
            {
                dbUser = _db.Users.FirstOrDefault(
                    x =>
                        x.MobileNumber == userDTO.MobileNumber
                        && x.Password == userDTO.Password
                        && x.CountryCode == userDTO.CountryCode
                        && x.UserType == "MANAGER"
                );
                if (dbUser != null)
                    userDTO.UserType = "MANAGER";
            }

            if (dbUser == null)
            {
                LoginThrottle.Fail(throttleKey);
                return new Response<UserDTO>(false, "Invalid Mobile Number and Password.", null);
            }
            LoginThrottle.Success(throttleKey);

            userDTO.Id = dbUser.Id;
            userDTO.SecurityStamp = dbUser.SecurityStamp;
            userDTO.Token = AuthContext.IssueForUser(dbUser);

            if (userDTO.UserType.Equals("SUPERADMIN"))
                return new Response<UserDTO>(true, null, userDTO);
            else if (userDTO.UserType.Equals("DOCTOR"))
            {
                var doctorDb = _db.Doctors.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                if (doctorDb == null)
                    return new Response<UserDTO>(false, "Doctor not found.", null);
                if (doctorDb.ValidUpto == null)
                {
                    return new Response<UserDTO>(false, "You are not approved. Contact admin for approval at 923335196658.", null);
                }

                userDTO.DoctorId = doctorDb.Id;
                userDTO.AllowInventory = doctorDb.AllowInventory;
                userDTO.AllowSupplier  = doctorDb.AllowSupplier;
                userDTO.AllowFinancial = doctorDb.AllowFinancial;
                userDTO.AllowSalesReport = doctorDb.AllowSalesReport;
                userDTO.AllowInvoice = doctorDb.AllowInvoice;
                userDTO.ProfileImage = doctorDb.ProfileImage;
                userDTO.DoctorType = doctorDb.DoctorType;
                userDTO.Name = !string.IsNullOrEmpty(doctorDb.DisplayName) ? doctorDb.DisplayName : doctorDb.FirstName;
            }
            else if (userDTO.UserType.Equals("PARENT"))
            {
                var childDB = _db.Childs.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                if (childDB == null)
                    return new Response<UserDTO>(false, "Child not found.", null);
                else
                    userDTO.ChildId = childDB.Id;
            }
            else if (userDTO.UserType.Equals("PA"))
            {
                var paDb = _db.PersonalAssistant.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                if (paDb == null)
                    return new Response<UserDTO>(false, "Personal Assistant not found.", null);
                else if (paDb.IsActive == false)
                    return new Response<UserDTO>(false, "Your account has been deactivated. Contact your doctor.", null);
                else if (paDb.IsVerified == false)
                    return new Response<UserDTO>(false, "You are not approved. Contact doctor for approval.", null);
                else
                    userDTO.PAId = paDb.Id;
                userDTO.DoctorId = paDb.DoctorId;
                userDTO.IsVerified = paDb.IsVerified;
                userDTO.Name = paDb.Name;

                var doctorDb = _db.Doctors.Where(x => x.Id == paDb.DoctorId).FirstOrDefault();
                userDTO.AllowInventory = doctorDb != null && doctorDb.AllowInventory;
            }
            else if (userDTO.UserType.Equals("MANAGER"))
            {
                var managerDb = _db.Manager.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                if (managerDb == null)
                    return new Response<UserDTO>(false, "Manager not found.", null);
                else if (managerDb.IsActive == false)
                    return new Response<UserDTO>(false, "Your account has been deactivated. Contact your doctor.", null);
                else if (managerDb.IsVerified == false)
                    return new Response<UserDTO>(false, "You are not approved. Contact doctor for approval.", null);
                else
                    userDTO.ManagerId = managerDb.Id;
                userDTO.DoctorId = managerDb.DoctorId;
                userDTO.IsVerified = managerDb.IsVerified;
                userDTO.Name = managerDb.Name;
            }

            return new Response<UserDTO>(true, null, userDTO);
        }

        // Magic-link auto-login for the parent app (VacParent). VacParent opens
        // .../child/vaccine/{childId}?t={token}; on load it posts the token here
        // and, if valid, gets back the same PARENT UserDTO the normal login
        // returns, which it stores as the session. The token is a stateless
        // HMAC-signed blob (see LinkLoginToken) carrying UserId + childId +
        // expiry, so there is nothing to store or migrate on the server.
        [HttpGet("link-login")]
        public Response<UserDTO> LinkLogin(string token)
        {
            if (!LinkLoginToken.Validate(token, LinkLoginSecret(), out long userId, out long childId))
                return new Response<UserDTO>(false, "This link is invalid or has expired.", null);

            var dbUser = _db.Users.FirstOrDefault(x => x.Id == userId && x.UserType == "PARENT");
            if (dbUser == null)
                return new Response<UserDTO>(false, "Account not found.", null);

            // The child referenced by the link must actually belong to this parent.
            var childDB = _db.Childs.FirstOrDefault(x => x.Id == childId && x.UserId == userId);
            if (childDB == null)
                return new Response<UserDTO>(false, "Child not found.", null);

            var userDTO = _mapper.Map<UserDTO>(dbUser);
            userDTO.Id = dbUser.Id;
            userDTO.SecurityStamp = dbUser.SecurityStamp;
            userDTO.ChildId = childDB.Id;
            userDTO.Token = AuthContext.IssueForUser(dbUser);
            userDTO.Password = ""; // never echo the password back over a link login

            return new Response<UserDTO>(true, null, userDTO);
        }

        // Magic-link auto-login for a PA-assignment email (VacDoc). VacDoc opens
        // .../members/pa/assignments?t={token}&aid={paAssignmentId}; on load it posts
        // the token here and, if valid, gets back the same PA UserDTO the normal PA
        // login returns, which it stores as the session. The token is a stateless
        // HMAC-signed blob (see PaAssignmentLinkToken) carrying UserId + PAAssignmentId
        // + a 24-hour expiry, so there is nothing to store or migrate on the server.
        [HttpGet("link-login-pa")]
        public Response<UserDTO> LinkLoginPa(string token)
        {
            if (!PaAssignmentLinkToken.Validate(token, LinkLoginSecret(), out long userId, out long paAssignmentId))
                return new Response<UserDTO>(false, "This link is invalid or has expired.", null);

            var dbUser = _db.Users.FirstOrDefault(x => x.Id == userId && x.UserType == "PA");
            if (dbUser == null)
                return new Response<UserDTO>(false, "Account not found.", null);

            var paDb = _db.PersonalAssistant.FirstOrDefault(x => x.UserId == userId);
            if (paDb == null)
                return new Response<UserDTO>(false, "Personal Assistant not found.", null);
            if (paDb.IsActive == false)
                return new Response<UserDTO>(false, "Your account has been deactivated. Contact your doctor.", null);
            if (paDb.IsVerified == false)
                return new Response<UserDTO>(false, "You are not approved. Contact doctor for approval.", null);

            // The assignment referenced by the link must actually belong to this PA.
            var assignmentDb = _db.PAAssignments.FirstOrDefault(x => x.Id == paAssignmentId && x.PersonalAssistantId == paDb.Id);
            if (assignmentDb == null)
                return new Response<UserDTO>(false, "Assignment not found.", null);

            var doctorDb = _db.Doctors.FirstOrDefault(x => x.Id == paDb.DoctorId);

            var userDTO = _mapper.Map<UserDTO>(dbUser);
            userDTO.Id = dbUser.Id;
            userDTO.SecurityStamp = dbUser.SecurityStamp;
            userDTO.PAId = paDb.Id;
            userDTO.DoctorId = paDb.DoctorId;
            userDTO.IsVerified = paDb.IsVerified;
            userDTO.Name = paDb.Name;
            userDTO.AllowInventory = doctorDb != null && doctorDb.AllowInventory;
            userDTO.Token = AuthContext.IssueForUser(dbUser);
            userDTO.Password = ""; // never echo the password back over a link login

            return new Response<UserDTO>(true, null, userDTO);
        }

        [HttpPost("forgot-password")]
        public Response<UserDTO> ForgotPassword(UserDTO userDTO)
        {
            {
                var dbUser = _db.Users
                    .Where(x => x.MobileNumber == userDTO.MobileNumber)
                    .Where(x => x.CountryCode == userDTO.CountryCode)
                    .Where(ut => ut.UserType == userDTO.UserType)
                    .FirstOrDefault();

                // Manager shares the PA forgot-password form (no visible role picker), so the
                // frontend always sends UserType "PA" here too — same PA-then-MANAGER fallback
                // as login().
                if (dbUser == null && userDTO.UserType == "PA")
                {
                    dbUser = _db.Users
                        .Where(x => x.MobileNumber == userDTO.MobileNumber)
                        .Where(x => x.CountryCode == userDTO.CountryCode)
                        .Where(ut => ut.UserType == "MANAGER")
                        .FirstOrDefault();
                }

                if (dbUser == null)
                    return new Response<UserDTO>(false, "Invalid Mobile Number", null);

                if (dbUser.UserType.Equals("DOCTOR"))
                {
                    var doctorDb = _db.Doctors.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                    if (doctorDb == null)
                    {
                        return new Response<UserDTO>(false, "Invalid Mobile Number", null);
                    }
                    else
                    {
                        UserEmail.DoctorForgotPassword(doctorDb, _db);
                        UserSMS u = new UserSMS(_db);
                        u.DoctorForgotPasswordSMS(doctorDb);
                        return new Response<UserDTO>(
                            true,
                            "your password has been sent to your mobile number and email address",
                            null
                        );
                    }
                }
                else if (dbUser.UserType.Equals("PARENT"))
                {
                    var childDB = _db.Childs.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                    if (childDB == null)
                    {
                        return new Response<UserDTO>(false, "Invalid Mobile Number", null);
                    }
                    else
                    {
                        UserEmail.ParentForgotPassword(childDB, _db);
                        UserSMS u = new UserSMS(_db);
                        u.ParentForgotPasswordSMS(childDB);
                        return new Response<UserDTO>(
                            true,
                            "your password has been sent to your mobile number and email address",
                            null
                        );
                    }
                }
                else if (dbUser.UserType.Equals("PA"))
                {
                    var paDb = _db.PersonalAssistant
                        .Include(p => p.User)
                        .FirstOrDefault(x => x.UserId == dbUser.Id);
                    if (paDb == null)
                    {
                        return new Response<UserDTO>(false, "Invalid Mobile Number", null);
                    }
                    else
                    {
                        UserEmail.PaForgotPassword(paDb, _db);
                        return new Response<UserDTO>(
                            true,
                            "Your password has been sent to your email address",
                            null
                        );
                    }
                }
                else if (dbUser.UserType.Equals("MANAGER"))
                {
                    var managerDb = _db.Manager
                        .Include(m => m.User)
                        .FirstOrDefault(x => x.UserId == dbUser.Id);
                    if (managerDb == null)
                    {
                        return new Response<UserDTO>(false, "Invalid Mobile Number", null);
                    }
                    else
                    {
                        UserEmail.ManagerForgotPassword(managerDb, _db);
                        return new Response<UserDTO>(
                            true,
                            "Your password has been sent to your email address",
                            null
                        );
                    }
                }
                else
                {
                    return new Response<UserDTO>(false, "Please contact with admin", null);
                }
            }
        }

        // [HttpPost("verify")]
        // public ActionResult<Response<bool>> VerifyChild(string childname, string fathername, DateTime DOB, string Email)
        // {
        //     var child = _db.Childs
        //         .Where(x => x.Name == childname &&
        //                     x.FatherName == fathername &&
        //                     x.DOB.Date == DOB)
        //         .FirstOrDefault();
        //     if (child == null)
        //     {


        //         return new Response<bool>(false, "No matching record found", false);
        //     }

        //     if (string.IsNullOrEmpty(child.Email))
        //     {
        //         child.Email = Email;
        //         _db.Childs.Update(child); // Mark the entity as updated
        //         _db.SaveChanges();


        //     }
        //     return new Response<bool>(true, "Record matches", true);
        // }
        [HttpPost("verify")]
        public ActionResult<Response<bool>> VerifyChild(ChildDTO childDTO)
        {
            var child = _db.Childs
                .Where(x => x.Name == childDTO.Name &&
                            x.FatherName == childDTO.FatherName &&
                            x.DOB.Date == childDTO.DOB.Date)
                .FirstOrDefault();

            if (child == null)
            {
                return new Response<bool>(false, "No matching record found", false);
            }

            // Retrieve user data based on child's UserId
            var user = _db.Users
                .Where(u => u.Id == child.UserId)
                .Select(u => new { u.MobileNumber, u.Password })
                .FirstOrDefault();

            if (user == null)
            {
                return new Response<bool>(false, "No matching user record found", false);
            }

            // Update child's email if it's empty
            if (string.IsNullOrEmpty(child.Email))
            {
                child.Email = childDTO.Email;
                _db.Childs.Update(child);
                _db.SaveChanges();
            }

            // Prepare email body
            string body = $"We have reset your password, please use the following details to login Your Account \n" +
                          $"Username: {user.MobileNumber}\n" +
                          $"Password: {user.Password}\n";

            // Send email
            try
            {
                var doctorForChild = _db.Clinics.Where(c => c.Id == child.ClinicId).Select(c => c.Doctor).FirstOrDefault();
                var senderForChild = EmailSenderResolver.Resolve(doctorForChild, _db);
                UserEmail.SendEmail(child.Email, body, sender: senderForChild);
                return new Response<bool>(true, "Your login credentials have been sent to your email address", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending email: " + ex.Message);
                return new Response<bool>(false, $"Record matches but error sending email: {ex.Message}", false);
            }
        }


        [HttpPost("change-password")]
        public Response<UserDTO> ChangePassword(ChangePasswordRequestDTO user)
        {
            {
                if (!AuthContext.CallerIsUser(user.UserId))
                    return new Response<UserDTO>(false, "Not allowed.", null);
                User? userDB = _db.Users.Where(x => x.Id == user.UserId).FirstOrDefault();
                if (userDB == null)
                    return new Response<UserDTO>(false, "User not found.", null);
                if (!userDB.Password.Equals(user.OldPassword))
                    return new Response<UserDTO>(false, "Old password doesn't match.", null);
                else
                {
                    userDB.Password = user.NewPassword;
                    // Rotate the stamp so every other device's cached stamp goes stale and gets logged out.
                    userDB.SecurityStamp = Guid.NewGuid().ToString();
                    _db.SaveChanges();
                    AuthDirectory.Forget('U', userDB.Id);
                    return new Response<UserDTO>(true, "Password change successfully.", new UserDTO { SecurityStamp = userDB.SecurityStamp });
                }
            }
        }


        [HttpGet("validate-session")]
        public Response<bool> ValidateSession(long userId, string securityStamp)
        {
            var dbUser = _db.Users.Where(x => x.Id == userId).FirstOrDefault();
            if (dbUser == null)
                return new Response<bool>(false, "User not found.", false);

            bool isValid = dbUser.SecurityStamp == securityStamp;
            return new Response<bool>(true, null, isValid);
        }

        [HttpPost("change-parent-password")]
        public ActionResult<Response<UserDTO>> ChangeParentPassword([FromBody] ChangePasswordRequestDTO request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.OldPassword) || string.IsNullOrEmpty(request.NewPassword) || string.IsNullOrEmpty(request.ConfirmPassword))
                {
                    return BadRequest(new Response<UserDTO>(false, "All password fields are required.", null));
                }

                if (request.NewPassword != request.ConfirmPassword)
                {
                    return BadRequest(new Response<UserDTO>(false, "New password and confirm password do not match.", null));
                }

                // Previously matched the FIRST parent anywhere whose password equalled OldPassword.
                if (request.UserId <= 0 || !AuthContext.CallerIsUser(request.UserId))
                    return Unauthorized(new Response<UserDTO>(false, "Not allowed.", null));

                var user = _db.Users
                    .Include(u => u.Childs)
                    .FirstOrDefault(x => x.Id == request.UserId && x.UserType == "PARENT" && x.Password == request.OldPassword);

                if (user == null)
                {
                    return NotFound(new Response<UserDTO>(false, "Parent user not found or old password is incorrect.", null));
                }

                // Update the password
                user.Password = request.NewPassword;
                user.SecurityStamp = Guid.NewGuid().ToString();

                // If you want to update the Child's password as well (assuming it's stored there)
                if (user.Childs != null && user.Childs.Any())
                {
                    foreach (var child in user.Childs)
                    {
                        user.Password = request.NewPassword;
                    }
                }

                _db.SaveChanges();
                AuthDirectory.Forget('U', user.Id);

                // Map the updated user to UserDTO if needed
                var userDTO = _mapper.Map<UserDTO>(user);
                userDTO.Password = "";

                return Ok(new Response<UserDTO>(true, "Password changed successfully.", userDTO));
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error in ChangeParentPassword: {ex}");
                return StatusCode(500, new Response<UserDTO>(false, "An error occurred while changing the password.", null));
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, User User)
        {
            if (!AuthContext.IsSuperAdmin)
                return StatusCode(403, new { IsSuccess = false, Message = "Not allowed." });
            if (id != User.Id)
                return BadRequest();

            _db.Entry(User).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            if (!AuthContext.IsSuperAdmin)
                return StatusCode(403, new { IsSuccess = false, Message = "Not allowed." });
            var obj = await _db.Users.FindAsync(id);

            if (obj == null)
                return NotFound();

            var stockBlock = VaccineAPI.Services.InventoryDeleteGuard.ForUser(_db, id);
            if (stockBlock != null)
                return Conflict(new { IsSuccess = false, Message = stockBlock });

            _db.Users.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}