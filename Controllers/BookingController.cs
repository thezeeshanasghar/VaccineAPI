using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using System.IO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets }; //SheetsService.Scope.SpreadsheetsReadonly
        static readonly string ApplicationName = "VaccineAPI"; //"quickstart-1599807090946";
        static readonly string SpreadsheetId = "1DZnUf0by6jm0qTqVne-29-3Pym4vRtJ3pHR94ZJcbFA";
        static SheetsService? service;

        public BookingController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("last/{childId}")]
        public async Task<Response<BookingDTO>> GetLastBooking(long childId)
        {
            try
            {
                var booking = await _db.Bookings.AsNoTracking()
                    .Where(b => b.ChildId == childId)
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync();

                if (booking == null)
                {
                    return new Response<BookingDTO>(true, null, null);
                }

                var dto = MapBookingToDTO(booking);
                return new Response<BookingDTO>(true, null, dto);
            }
            catch (Exception ex)
            {
                return new Response<BookingDTO>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpGet("parent/{userId}")]
        public async Task<Response<IEnumerable<BookingDTO>>> GetByParent(long userId)
        {
            try
            {
                var bookings = await _db.Bookings.AsNoTracking()
                    .Where(b => b.ParentUserId == userId)
                    .OrderByDescending(b => b.Id)
                    .ToListAsync();

                var dtos = bookings.Select(MapBookingToDTO).ToList();
                return new Response<IEnumerable<BookingDTO>>(true, null, dtos);
            }
            catch (Exception ex)
            {
                return new Response<IEnumerable<BookingDTO>>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpPost]
        public async Task<Response<BookingDTO>> AddBooking(BookingDTO bookingDTO)
        {
            try
            {
                if (bookingDTO.ChildId <= 0)
                {
                    return new Response<BookingDTO>(false, "Child not found.", null);
                }

                var child = await _db.Childs
                    .Include(c => c.Clinic)
                        .ThenInclude(cl => cl.Doctor)
                    .FirstOrDefaultAsync(c => c.Id == bookingDTO.ChildId);

                if (child == null || child.Clinic == null)
                {
                    return new Response<BookingDTO>(false, "Child not found.", null);
                }

                var booking = new Booking
                {
                    ChildId = child.Id,
                    ParentUserId = child.UserId,
                    ClinicId = child.ClinicId,
                    DoctorId = child.Clinic.DoctorId,
                    ChildName = bookingDTO.ChildName,
                    FatherName = bookingDTO.FatherName,
                    Email = bookingDTO.Email,
                    Phone = bookingDTO.Phone,
                    DOB = child.DOB,
                    Vaccines = bookingDTO.Vaccines,
                    Type = bookingDTO.Status,
                    Status = "Pending",
                    Address = bookingDTO.Address,
                    Location = bookingDTO.Location,
                    City = bookingDTO.City,
                    Card = bookingDTO.Card,
                    PreferredDate = ParseDate(bookingDTO.PreferredDate),
                    Comments = bookingDTO.Comments,
                    DoctorComment = "",
                    CreatedAt = DateTime.Now
                };

                _db.Bookings.Add(booking);
                await _db.SaveChangesAsync();

                try
                {
                    Init();
                    AddRow(new BookingSheetDTO
                    {
                        ChildName = booking.ChildName,
                        FatherName = booking.FatherName,
                        DOB = bookingDTO.DOB,
                        Vaccines = booking.Vaccines,
                        Email = booking.Email,
                        Phone = booking.Phone,
                        Address = booking.Address,
                        Card = booking.Card,
                        City = booking.City,
                        BookingDate = bookingDTO.BookingDate,
                        Status = booking.Type
                    });
                }
                catch (Exception)
                {
                    // Best-effort secondary audit log — never block the booking on a Sheets failure
                }

                var doctor = child.Clinic.Doctor;
                if (doctor != null)
                {
                    var isHome = booking.Type == "HomeBooked";
                    var title = isHome ? "New Home-Visit Booking Request" : "New Clinic Visit Booking Request";
                    var message = booking.ChildName + " (" + booking.FatherName + ") has requested a " +
                        (isHome ? "home vaccination visit" : "clinic visit") +
                        " for " + booking.Vaccines +
                        (booking.PreferredDate.HasValue ? " on " + booking.PreferredDate.Value.ToString("dd MMM yyyy") : "") + "." +
                        (isHome ? " Address: " + booking.Address : "");

                    _db.Notifications.Add(new Notification
                    {
                        Type = "NewBooking",
                        RecipientType = "DOCTOR",
                        RecipientId = doctor.Id,
                        BookingId = booking.Id,
                        ChildId = booking.ChildId,
                        ClinicId = booking.ClinicId,
                        Title = title,
                        Message = message,
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });
                    await _db.SaveChangesAsync();

                    try
                    {
                        UserEmail.SendEmail(doctor.Email, message + "\n\nOpen VacDoc → Bookings to respond.", title);
                    }
                    catch (Exception)
                    {
                        // Best-effort — email failure must not fail the booking
                    }
                }

                var resultDto = MapBookingToDTO(booking);
                return new Response<BookingDTO>(true, "Booking request submitted successfully.", resultDto);
            }
            catch (Exception)
            {
                return new Response<BookingDTO>(false, "An error occurred while submitting your booking.", null);
            }
        }

        [HttpGet("clinic/{clinicId}")]
        public async Task<Response<IEnumerable<BookingDTO>>> GetByClinic(long clinicId, [FromQuery] string? status, [FromQuery] string? type)
        {
            try
            {
                var query = _db.Bookings.AsNoTracking().Where(b => b.ClinicId == clinicId);

                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(b => b.Status == status);
                }
                if (!string.IsNullOrEmpty(type))
                {
                    query = query.Where(b => b.Type == type);
                }

                var bookings = await query.OrderByDescending(b => b.Id).ToListAsync();
                var dtos = bookings.Select(MapBookingToDTO).ToList();
                return new Response<IEnumerable<BookingDTO>>(true, null, dtos);
            }
            catch (Exception ex)
            {
                return new Response<IEnumerable<BookingDTO>>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpGet("{id}")]
        public async Task<Response<BookingDTO>> GetSingle(long id)
        {
            try
            {
                var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                {
                    return new Response<BookingDTO>(false, "Booking not found.", null);
                }
                var dto = MapBookingToDTO(booking);
                return new Response<BookingDTO>(true, null, dto);
            }
            catch (Exception ex)
            {
                return new Response<BookingDTO>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpPut("{id}/confirm")]
        public async Task<Response<BookingDTO>> ConfirmBooking(long id, BookingActionDTO actionDTO)
        {
            return await UpdateBookingStatus(id, actionDTO, "Confirmed", "BookingConfirmed",
                "Booking Confirmed",
                booking => "Your " + (booking.Type == "HomeBooked" ? "home visit" : "clinic visit") +
                    " booking for " + booking.ChildName + " has been confirmed." +
                    (string.IsNullOrEmpty(booking.DoctorComment) ? "" : " Note: " + booking.DoctorComment));
        }

        [HttpPut("{id}/cancel")]
        public async Task<Response<BookingDTO>> CancelBooking(long id, BookingActionDTO actionDTO)
        {
            return await UpdateBookingStatus(id, actionDTO, "Cancelled", "BookingCancelled",
                "Booking Cancelled",
                booking => "Your " + (booking.Type == "HomeBooked" ? "home visit" : "clinic visit") +
                    " booking for " + booking.ChildName + " has been cancelled." +
                    (string.IsNullOrEmpty(booking.DoctorComment) ? "" : " Note: " + booking.DoctorComment));
        }

        [HttpPut("{id}/comment")]
        public async Task<Response<BookingDTO>> AddComment(long id, BookingActionDTO actionDTO)
        {
            try
            {
                var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                {
                    return new Response<BookingDTO>(false, "Booking not found.", null);
                }

                booking.DoctorComment = actionDTO.DoctorComment;
                booking.UpdatedAt = DateTime.Now;
                await _db.SaveChangesAsync();

                var dto = MapBookingToDTO(booking);
                return new Response<BookingDTO>(true, "Comment saved.", dto);
            }
            catch (Exception ex)
            {
                return new Response<BookingDTO>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpGet("pending-count/{clinicId}")]
        public async Task<Response<int>> GetPendingCount(long clinicId)
        {
            try
            {
                var count = await _db.Bookings.CountAsync(b => b.ClinicId == clinicId && b.Status == "Pending");
                return new Response<int>(true, null, count);
            }
            catch (Exception ex)
            {
                return new Response<int>(false, $"An error occurred: {ex.Message}", 0);
            }
        }

        private async Task<Response<BookingDTO>> UpdateBookingStatus(long id, BookingActionDTO actionDTO, string status, string notificationType, string notificationTitle, Func<Booking, string> messageBuilder)
        {
            try
            {
                var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
                if (booking == null)
                {
                    return new Response<BookingDTO>(false, "Booking not found.", null);
                }

                booking.Status = status;
                booking.DoctorComment = actionDTO.DoctorComment;
                booking.UpdatedAt = DateTime.Now;
                await _db.SaveChangesAsync();

                _db.Notifications.Add(new Notification
                {
                    Type = notificationType,
                    RecipientType = "PARENT",
                    RecipientId = booking.ParentUserId,
                    BookingId = booking.Id,
                    ChildId = booking.ChildId,
                    ClinicId = booking.ClinicId,
                    Title = notificationTitle,
                    Message = messageBuilder(booking),
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
                await _db.SaveChangesAsync();

                var dto = MapBookingToDTO(booking);
                return new Response<BookingDTO>(true, "Booking " + status.ToLower() + ".", dto);
            }
            catch (Exception ex)
            {
                return new Response<BookingDTO>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        private BookingDTO MapBookingToDTO(Booking booking)
        {
            var dto = _mapper.Map<BookingDTO>(booking);
            dto.UserId = booking.ParentUserId;
            dto.DOB = booking.DOB.ToString("yyyy-MM-dd");
            dto.BookingDate = booking.CreatedAt.ToString("yyyy-MM-dd");
            dto.PreferredDate = booking.PreferredDate.HasValue ? booking.PreferredDate.Value.ToString("yyyy-MM-dd") : "";
            return dto;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }
            return null;
        }

        static void Init()
        {
            GoogleCredential credential;
            //Reading Credentials File...
            using (var stream = new FileStream("app_client_secret.json", FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(Scopes);

            }
            // Creating Google Sheets API service...
            service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        static void AddRow(BookingSheetDTO data)
        {
            // Specifying Column Range for reading...
            var range = "A:K"; //$"{sheet}!A:B";
            var valueRange = new ValueRange();
            // Data for new row
            var oblist = new List<object> { data.ChildName, data.FatherName, data.DOB, data.Vaccines, data.Email, data.Phone, data.Address, data.Card, data.City, data.BookingDate, data.Status };//{ "Harry", "80" };
                                                                                                                                                                                                  // Console.WriteLine(oblist);
            valueRange.Values = new List<IList<object>> { oblist };
            // Append the above record...
            if (service == null)
            {
                return;
            }

            var appendRequest = service.Spreadsheets.Values.Append(valueRange, SpreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            var appendReponse = appendRequest.Execute();
        }

    }
}

// https://dottutorials.net/google-sheets-read-write-operations-dotnet-core-tutorial/
