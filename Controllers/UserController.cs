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

        public UserController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet]
         public async Task<Response<List<UserDTO>>> GetAll()
        {
            var list = await _db.Users.OrderBy(x=>x.Id).ToListAsync();
            List<UserDTO> listDTO = _mapper.Map<List<UserDTO>>(list);
           
            return new Response<List<UserDTO>>(true, null, listDTO);
        }
        
        [HttpGet("{id}")]
        public async Task<Response<User>> GetSingle(long id)
        {
            var single = await _db.Users.FindAsync(id);
            if (single == null)
                return new Response<User>(false, "Not Found", null);
           
                return new Response<User>(true, null, single);   
        }


         [HttpGet("{mobileNumber}/{dob}")]
        public async Task<Response<User>> GetPassword(string mobileNumber , DateTime dob)
        {
            var user = await _db.Users.Where(x=> x.MobileNumber == mobileNumber).FirstOrDefaultAsync();
            if (user == null)
                return new Response<User>(false, "Not Found", null);
            else {
                Console.WriteLine(dob);
                var c = await _db.Childs.Where(x=> x.UserId == user.Id).FirstOrDefaultAsync();
                Console.WriteLine(c.DOB.Date);
                var child = await _db.Childs.Where(x=> (x.UserId == user.Id && x.DOB.Date == dob)).FirstOrDefaultAsync();
                if (child == null)
                return new Response<User>(false, "Not Found", null);
                else 
                return new Response<User>(true, null, user);
            }
        }

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
        
                {
                    var dbUser = _db.Users.FirstOrDefault(x => 
                                                                x.MobileNumber == userDTO.MobileNumber && 
                                                                x.Password == userDTO.Password && 
                                                                x.CountryCode==userDTO.CountryCode &&
                                                                x.UserType == userDTO.UserType);
                    if (dbUser == null)
                        return new Response<UserDTO>(false, "Invalid Mobile Number and Password.", null);

                    userDTO.Id = dbUser.Id;

                    if (userDTO.UserType.Equals("SUPERADMIN"))
                        return new Response<UserDTO>(true, null, userDTO);
                    else if (userDTO.UserType.Equals("DOCTOR"))
                    {

                        var doctorDb = _db.Doctors.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                        if (doctorDb == null)
                            return new Response<UserDTO>(false, "Doctor not found.", null);
                        if (doctorDb.IsApproved != true)
                            return new Response<UserDTO>(false, "You are not approved. Contact admin for approval at 923335196658", null);

                        userDTO.DoctorId = doctorDb.Id;
                        userDTO.AllowInventory = doctorDb.AllowInventory;
                        userDTO.AllowInvoice = doctorDb.AllowInvoice;
                        userDTO.ProfileImage = doctorDb.ProfileImage;
                        userDTO.DoctorType = doctorDb.DoctorType;

                    }
                    else if (userDTO.UserType.Equals("PARENT"))
                    {
                        var childDB = _db.Childs.Where(x => x.UserId == dbUser.Id).FirstOrDefault();
                        if (childDB == null)
                            return new Response<UserDTO>(false, "Child not found.", null);
                        else
                            userDTO.ChildId = childDB.Id;
                    }

                    return new Response<UserDTO>(true, null, userDTO);
                }
        }


         [HttpPost("forgot-password")]
        public Response<UserDTO> ForgotPassword(UserDTO userDTO)
        {
                {
                    var dbUser = _db.Users.Where(x => x.MobileNumber == userDTO.MobileNumber)
                        .Where(x => x.CountryCode == userDTO.CountryCode)
                        .Where(ut =>ut.UserType==userDTO.UserType).FirstOrDefault();

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
                            UserEmail.DoctorForgotPassword(doctorDb);
                            UserSMS u = new UserSMS(_db);
                            u.DoctorForgotPasswordSMS(doctorDb);
                            return new Response<UserDTO>(true, "your password has been sent to your mobile number and email address", null);

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
                            UserEmail.ParentForgotPassword(childDB);
                            UserSMS u = new UserSMS(_db);
                            u.ParentForgotPasswordSMS(childDB);
                            return new Response<UserDTO>(true, "your password has been sent to your mobile number and email address", null);
                        }
                    }
                    else
                    {
                        return new Response<UserDTO>(false, "Please contact with admin", null);
                    }

                }
        }
        
        [HttpPost("change-password")]
        public Response<UserDTO> ChangePassword(ChangePasswordRequestDTO user)
        {
    
            
            
                {
                    User userDB = _db.Users.Where(x => x.Id == user.UserId).FirstOrDefault();
                    if (userDB == null)
                        return new Response<UserDTO>(false, "User not found.", null);
                    if(!userDB.Password.Equals(user.OldPassword))
                        return new Response<UserDTO>(false, "Old password doesn't match.", null);
                    else
                    {
                        userDB.Password = user.NewPassword;
                        _db.SaveChanges();
                        return new Response<UserDTO>(true, "Password change successfully.", null);
                    }
                }
            }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(long id, User User)
        {
            if (id != User.Id)
                return BadRequest();

            _db.Entry(User).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var obj = await _db.Users.FindAsync(id);

            if (obj == null)
                return NotFound();

            _db.Users.Remove(obj);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
