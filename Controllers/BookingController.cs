using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoMapper;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using VaccineAPI.Services;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;
        private readonly EmailService _email;

        static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };
        static readonly string ApplicationName = "VaccineAPI";
        static readonly string SpreadsheetId = "1VxF4JqAPwfZZomaf3GctMkWAq3nEg0N4yTjkCYJr_PY";
        static SheetsService? service;

        public BookingController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
            _email = new EmailService();
        }

        // POST /api/booking
        [HttpPost]
        public Response<BookingDTO> AddBooking(BookingDTO bookingDTO)
        {
            // 1 — resolve child → clinic → doctor
            var child = _db.Childs
                .Include(c => c.Clinic).ThenInclude(cl => cl.Doctor)
                .Include(c => c.User)
                .FirstOrDefault(c => c.Id == bookingDTO.ChildId);

            long doctorId = 0;
            long userId   = bookingDTO.UserId;
            string doctorEmail = "";
            if (child != null && child.Clinic != null && child.Clinic.Doctor != null)
            {
                doctorId    = child.Clinic.Doctor.Id;
                doctorEmail = child.Clinic.Doctor.Email;
            }

            // 2 — save to DB
            var booking = new Booking
            {
                ChildId       = bookingDTO.ChildId,
                DoctorId      = doctorId,
                UserId        = userId,
                Type          = bookingDTO.Status,   // "HomeBooked" | "ClinicBooked"
                Vaccines      = bookingDTO.Vaccines,
                Address       = bookingDTO.Address,
                Location      = bookingDTO.Location,
                City          = bookingDTO.City,
                ChildName     = bookingDTO.ChildName,
                FatherName    = bookingDTO.FatherName,
                Phone         = bookingDTO.Phone,
                Email         = bookingDTO.Email,
                PreferredDate = bookingDTO.PreferredDate,
                Comments      = bookingDTO.Comments,
                Status        = "Pending",
                CreatedAt     = DateTime.UtcNow.AddHours(5)
            };
            _db.Bookings.Add(booking);
            _db.SaveChanges();

            // 3 — create notification for doctor
            if (doctorId > 0)
            {
                _db.Notifications.Add(new Notification
                {
                    BookingId     = booking.Id,
                    RecipientId   = doctorId,
                    RecipientType = "Doctor",
                    Message       = $"New {booking.Type} booking from {booking.ChildName} — {booking.Vaccines}",
                    IsRead        = false,
                    CreatedAt     = DateTime.UtcNow.AddHours(5)
                });
            }

            // 4 — create notification for parent
            _db.Notifications.Add(new Notification
            {
                BookingId     = booking.Id,
                RecipientId   = userId,
                RecipientType = "Parent",
                Message       = $"Your booking for {booking.Vaccines} has been received. We will confirm shortly.",
                IsRead        = false,
                CreatedAt     = DateTime.UtcNow.AddHours(5)
            });

            _db.SaveChanges();

            // 5 — email to doctor
            string bookingTypeName = booking.Type == "HomeBooked" ? "Home Vaccination" : "Clinic Visit";
            string addressRows = booking.Type == "HomeBooked"
                ? $"│  Address         │  {booking.Address,-32}│\n" +
                  $"│  City            │  {booking.City,-32}│\n" +
                  (!string.IsNullOrWhiteSpace(booking.Location)
                    ? $"│  Location (Maps) │  {booking.Location,-32}│\n" : "")
                : "";
            string preferredRow = !string.IsNullOrWhiteSpace(booking.PreferredDate)
                ? $"│  Preferred Date  │  {booking.PreferredDate,-32}│\n" : "";
            string commentsRow = !string.IsNullOrWhiteSpace(booking.Comments)
                ? $"├──────────────────┼──────────────────────────────────┤\n" +
                  $"│  Comments        │  {booking.Comments,-32}│\n" : "";

            string doctorEmailBody =
                $"Dear Doctor,\n\n" +
                $"A new {bookingTypeName} booking has been submitted via the Patient Portal.\n\n" +
                $"┌──────────────────┬──────────────────────────────────┐\n" +
                $"│  BOOKING DETAILS                                    │\n" +
                $"├──────────────────┼──────────────────────────────────┤\n" +
                $"│  Type            │  {bookingTypeName,-32}│\n" +
                $"│  Booked On       │  {booking.CreatedAt:dd MMM yyyy, hh:mm tt}{"",10}│\n" +
                preferredRow +
                $"├──────────────────┼──────────────────────────────────┤\n" +
                $"│  PATIENT                                            │\n" +
                $"├──────────────────┼──────────────────────────────────┤\n" +
                $"│  Name            │  {booking.ChildName,-32}│\n" +
                $"│  Father's Name   │  {booking.FatherName,-32}│\n" +
                $"│  Phone           │  {booking.Phone,-32}│\n" +
                $"├──────────────────┼──────────────────────────────────┤\n" +
                $"│  VACCINES        │  {booking.Vaccines,-32}│\n" +
                (!string.IsNullOrWhiteSpace(addressRows)
                    ? $"├──────────────────┼──────────────────────────────────┤\n" + addressRows : "") +
                commentsRow +
                $"└──────────────────┴──────────────────────────────────┘\n\n" +
                $"Please login to VacDoc to confirm or cancel this booking.\n" +
                $"https://doctor.vaccinationcentre.com";

            UserEmail.SendEmail(doctorEmail, $"New {bookingTypeName} Booking — {booking.ChildName}", doctorEmailBody);

            UserEmail.SendEmail(booking.Email, "Booking Received — vaccinationcentre.com",
                $"Dear {booking.ChildName}'s parent,\n\n" +
                $"Your {bookingTypeName.ToLower()} booking request has been received.\n" +
                $"We will confirm your appointment shortly.\n\n" +
                $"Vaccines: {booking.Vaccines}\n" +
                (!string.IsNullOrWhiteSpace(booking.PreferredDate) ? $"Preferred Date: {booking.PreferredDate}\n" : "") +
                $"\nRegards,\nVaccination Centre Team");

            // 6 — write Google Sheet
            try
            {
                Init();
                AddRow(bookingDTO);
            }
            catch { /* sheet write failure should not block response */ }

            return new Response<BookingDTO>(true, "Booking submitted successfully.", null);
        }

        // GET /api/booking/last/{childId}
        [HttpGet("last/{childId}")]
        public Response<BookingDTO> GetLastBooking(long childId)
        {
            var booking = _db.Bookings
                .Where(b => b.ChildId == childId)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefault();

            if (booking == null)
                return new Response<BookingDTO>(false, "No previous booking found.", null);

            var dto = new BookingDTO
            {
                Address  = booking.Address,
                Location = booking.Location,
                City     = booking.City
            };
            return new Response<BookingDTO>(true, null, dto);
        }

        // PUT /api/booking/{id}/confirm
        [HttpPut("{id}/confirm")]
        public Response<BookingDTO> Confirm(long id)
        {
            return UpdateStatus(id, "Confirmed",
                parentMsg: "Your booking has been confirmed. We look forward to seeing you.",
                notifMsg:  "Your booking has been confirmed.");
        }

        // PUT /api/booking/{id}/cancel
        [HttpPut("{id}/cancel")]
        public Response<BookingDTO> Cancel(long id)
        {
            return UpdateStatus(id, "Cancelled",
                parentMsg: "We regret that your booking could not be confirmed. Please contact the clinic.",
                notifMsg:  "Your booking has been cancelled. Please contact the clinic.");
        }

        private Response<BookingDTO> UpdateStatus(long id, string status, string parentMsg, string notifMsg)
        {
            var booking = _db.Bookings.FirstOrDefault(b => b.Id == id);
            if (booking == null)
                return new Response<BookingDTO>(false, "Booking not found.", null);

            booking.Status = status;
            _db.SaveChanges();

            // notification for parent
            _db.Notifications.Add(new Notification
            {
                BookingId     = booking.Id,
                RecipientId   = booking.UserId,
                RecipientType = "Parent",
                Message       = notifMsg,
                IsRead        = false,
                CreatedAt     = DateTime.UtcNow.AddHours(5)
            });
            _db.SaveChanges();

            UserEmail.SendEmail(booking.Email, $"Booking {status} — vaccinationcentre.com", $"Dear parent,\n\n{parentMsg}\n\nRegards,\nVaccination Centre Team");

            return new Response<BookingDTO>(true, $"Booking {status.ToLower()}.", null);
        }

        // GET /api/booking/doctor/{doctorId}  — bookings list for doctor
        [HttpGet("doctor/{doctorId}")]
        public Response<IEnumerable<Booking>> GetByDoctor(long doctorId)
        {
            var cutoff   = DateTime.UtcNow.AddHours(5).AddDays(-30);
            var bookings = _db.Bookings
                .Where(b => b.DoctorId == doctorId && b.CreatedAt >= cutoff)
                .OrderByDescending(b => b.CreatedAt)
                .ToList();
            return new Response<IEnumerable<Booking>>(true, null, bookings);
        }

        // GET /api/booking/parent/{userId}  — booking history for parent
        [HttpGet("parent/{userId}")]
        public Response<IEnumerable<Booking>> GetByParent(long userId)
        {
            var cutoff   = DateTime.UtcNow.AddHours(5).AddDays(-30);
            var bookings = _db.Bookings
                .Where(b => b.UserId == userId && b.CreatedAt >= cutoff)
                .OrderByDescending(b => b.CreatedAt)
                .ToList();
            return new Response<IEnumerable<Booking>>(true, null, bookings);
        }

        // GET /api/notification/doctor/{doctorId}
        [HttpGet("/api/notification/doctor/{doctorId}")]
        public Response<object> GetDoctorNotifications(long doctorId)
        {
            var cutoff = DateTime.UtcNow.AddHours(5).AddDays(-30);
            var notifs = _db.Notifications
                .Where(n => n.RecipientId == doctorId && n.RecipientType == "Doctor" && n.CreatedAt >= cutoff)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
            int unread = notifs.Count(n => !n.IsRead);
            return new Response<object>(true, null, new { UnreadCount = unread, Notifications = notifs });
        }

        // GET /api/notification/parent/{userId}
        [HttpGet("/api/notification/parent/{userId}")]
        public Response<object> GetParentNotifications(long userId)
        {
            var cutoff = DateTime.UtcNow.AddHours(5).AddDays(-30);
            var notifs = _db.Notifications
                .Where(n => n.RecipientId == userId && n.RecipientType == "Parent" && n.CreatedAt >= cutoff)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
            int unread = notifs.Count(n => !n.IsRead);
            return new Response<object>(true, null, new { UnreadCount = unread, Notifications = notifs });
        }

        // PUT /api/notification/{id}/read
        [HttpPut("/api/notification/{id}/read")]
        public Response<object> MarkRead(long id)
        {
            var notif = _db.Notifications.FirstOrDefault(n => n.Id == id);
            if (notif == null) return new Response<object>(false, "Not found.", null);
            notif.IsRead = true;
            _db.SaveChanges();
            return new Response<object>(true, "Marked as read.", null);
        }

        // PUT /api/notification/doctor/{doctorId}/read-all
        [HttpPut("/api/notification/doctor/{doctorId}/read-all")]
        public Response<object> MarkAllReadDoctor(long doctorId)
        {
            var unread = _db.Notifications
                .Where(n => n.RecipientId == doctorId && n.RecipientType == "Doctor" && !n.IsRead)
                .ToList();
            unread.ForEach(n => n.IsRead = true);
            _db.SaveChanges();
            return new Response<object>(true, "All marked as read.", null);
        }

        // PUT /api/notification/parent/{userId}/read-all
        [HttpPut("/api/notification/parent/{userId}/read-all")]
        public Response<object> MarkAllReadParent(long userId)
        {
            var unread = _db.Notifications
                .Where(n => n.RecipientId == userId && n.RecipientType == "Parent" && !n.IsRead)
                .ToList();
            unread.ForEach(n => n.IsRead = true);
            _db.SaveChanges();
            return new Response<object>(true, "All marked as read.", null);
        }

        // ── Google Sheet helpers ────────────────────────────────────────────
        static void Init()
        {
            GoogleCredential credential;
            using (var stream = new FileStream("app_client_secret.json", FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }
            service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        static void AddRow(BookingDTO data)
        {
            var range      = "A:K";
            var valueRange = new ValueRange();
            var oblist = new List<object>
            {
                data.ChildName, data.FatherName, data.DOB, data.Vaccines,
                data.Email, data.Phone, data.Address, data.Card,
                data.City, data.BookingDate, data.Status
            };
            valueRange.Values = new List<IList<object>> { oblist };
            if (service == null) return;
            var appendRequest = service.Spreadsheets.Values.Append(valueRange, SpreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            appendRequest.Execute();
        }
    }
}
