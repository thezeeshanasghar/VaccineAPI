﻿using System.Collections.Generic;
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
            var list = await _db.Users.OrderBy(x => x.Id).ToListAsync();
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
        public async Task<Response<User>> GetPassword(string mobileNumber, DateTime dob)
        {
            var user = await _db.Users
                .Where(x => x.MobileNumber == mobileNumber)
                .FirstOrDefaultAsync();
            if (user == null)
                return new Response<User>(false, "Not Found", null);
            else
            {
                Console.WriteLine(dob);
                var c = await _db.Childs.Where(x => x.UserId == user.Id).FirstOrDefaultAsync();
                Console.WriteLine(c.DOB.Date);
                var child = await _db.Childs
                    .Where(x => (x.UserId == user.Id && x.DOB.Date == dob))
                    .FirstOrDefaultAsync();
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
            var dbUser = _db.Users.FirstOrDefault(
                x =>
                    x.MobileNumber == userDTO.MobileNumber
                    && x.Password == userDTO.Password
                    && x.CountryCode == userDTO.CountryCode
                    && x.UserType == userDTO.UserType
            );
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
                if (doctorDb.ValidUpto == null)
                {
                    return new Response<UserDTO>(false, "You are not approved. Contact admin for approval at 923335196658.", null);
                }

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

        [HttpPost("forgot-password")]
        public Response<UserDTO> ForgotPassword(UserDTO userDTO)
        {
            {
                var dbUser = _db.Users
                    .Where(x => x.MobileNumber == userDTO.MobileNumber)
                    .Where(x => x.CountryCode == userDTO.CountryCode)
                    .Where(ut => ut.UserType == userDTO.UserType)
                    .FirstOrDefault();

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
                        UserEmail.ParentForgotPassword(childDB);
                        UserSMS u = new UserSMS(_db);
                        u.ParentForgotPasswordSMS(childDB);
                        return new Response<UserDTO>(
                            true,
                            "your password has been sent to your mobile number and email address",
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
                UserEmail.SendEmail2(child.Email, body);
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
                User userDB = _db.Users.Where(x => x.Id == user.UserId).FirstOrDefault();
                if (userDB == null)
                    return new Response<UserDTO>(false, "User not found.", null);
                if (!userDB.Password.Equals(user.OldPassword))
                    return new Response<UserDTO>(false, "Old password doesn't match.", null);
                else
                {
                    userDB.Password = user.NewPassword;
                    _db.SaveChanges();
                    return new Response<UserDTO>(true, "Password change successfully.", null);
                }
            }
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

                var user = _db.Users
                    .Include(u => u.Childs)
                    .FirstOrDefault(x => x.UserType == "PARENT" && x.Password == request.OldPassword);

                if (user == null)
                {
                    return NotFound(new Response<UserDTO>(false, "Parent user not found or old password is incorrect.", null));
                }

                // Update the password
                user.Password = request.NewPassword;

                // If you want to update the Child's password as well (assuming it's stored there)
                if (user.Childs != null && user.Childs.Any())
                {
                    foreach (var child in user.Childs)
                    {
                        user.Password = request.NewPassword;
                    }
                }

                _db.SaveChanges();

                // Map the updated user to UserDTO if needed
                var userDTO = _mapper.Map<UserDTO>(user);

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