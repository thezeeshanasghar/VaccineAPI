using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly Context _db;
        private readonly IMapper _mapper;

        public NotificationController(Context context, IMapper mapper)
        {
            _db = context;
            _mapper = mapper;
        }

        [HttpGet("parent/{userId}")]
        public async Task<Response<IEnumerable<NotificationDTO>>> GetByParent(long userId)
        {
            try
            {
                var notifications = await _db.Notifications.AsNoTracking()
                    .Where(n => n.RecipientType == "PARENT" && n.RecipientId == userId)
                    .OrderByDescending(n => n.Id)
                    .ToListAsync();

                var dtos = _mapper.Map<List<NotificationDTO>>(notifications);
                return new Response<IEnumerable<NotificationDTO>>(true, null, dtos);
            }
            catch (Exception ex)
            {
                return new Response<IEnumerable<NotificationDTO>>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpPut("{notifId}/read")]
        public async Task<Response<object>> MarkRead(long notifId)
        {
            try
            {
                var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notifId);
                if (notification == null)
                {
                    return new Response<object>(false, "Notification not found.", null);
                }

                notification.IsRead = true;
                notification.ReadAt = DateTime.Now;
                await _db.SaveChangesAsync();

                return new Response<object>(true, "Marked as read.", null);
            }
            catch (Exception ex)
            {
                return new Response<object>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpPut("parent/{userId}/read-all")]
        public async Task<Response<object>> MarkAllReadParent(long userId)
        {
            try
            {
                var unread = await _db.Notifications
                    .Where(n => n.RecipientType == "PARENT" && n.RecipientId == userId && !n.IsRead)
                    .ToListAsync();

                foreach (var n in unread)
                {
                    n.IsRead = true;
                    n.ReadAt = DateTime.Now;
                }

                await _db.SaveChangesAsync();
                return new Response<object>(true, "All notifications marked as read.", null);
            }
            catch (Exception ex)
            {
                return new Response<object>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpGet("doctor/{doctorId}")]
        public async Task<Response<IEnumerable<NotificationDTO>>> GetByDoctor(long doctorId)
        {
            try
            {
                var notifications = await _db.Notifications.AsNoTracking()
                    .Where(n => n.RecipientType == "DOCTOR" && n.RecipientId == doctorId)
                    .OrderByDescending(n => n.Id)
                    .ToListAsync();

                var dtos = _mapper.Map<List<NotificationDTO>>(notifications);
                return new Response<IEnumerable<NotificationDTO>>(true, null, dtos);
            }
            catch (Exception ex)
            {
                return new Response<IEnumerable<NotificationDTO>>(false, $"An error occurred: {ex.Message}", null);
            }
        }

        [HttpGet("doctor/{doctorId}/unread-count")]
        public async Task<Response<int>> GetUnreadCount(long doctorId)
        {
            try
            {
                var count = await _db.Notifications.CountAsync(
                    n => n.RecipientType == "DOCTOR" && n.RecipientId == doctorId && !n.IsRead);

                return new Response<int>(true, null, count);
            }
            catch (Exception ex)
            {
                return new Response<int>(false, $"An error occurred: {ex.Message}", 0);
            }
        }

        [HttpPut("doctor/{doctorId}/read-all")]
        public async Task<Response<object>> MarkAllReadDoctor(long doctorId)
        {
            try
            {
                var unread = await _db.Notifications
                    .Where(n => n.RecipientType == "DOCTOR" && n.RecipientId == doctorId && !n.IsRead)
                    .ToListAsync();

                foreach (var n in unread)
                {
                    n.IsRead = true;
                    n.ReadAt = DateTime.Now;
                }

                await _db.SaveChangesAsync();
                return new Response<object>(true, "All notifications marked as read.", null);
            }
            catch (Exception ex)
            {
                return new Response<object>(false, $"An error occurred: {ex.Message}", null);
            }
        }
    }
}
