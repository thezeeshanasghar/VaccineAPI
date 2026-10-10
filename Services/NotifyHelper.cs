using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Adds a Notification row (and, through Context.SaveChanges, the matching push). The caller saves.
    public static class NotifyHelper
    {
        public static void Add(Context db, string type, string recipientType, long recipientId, string title, string message,
                               long? childId = null, long? clinicId = null)
        {
            db.Notifications.Add(new Notification
            {
                Type = type,
                RecipientType = recipientType,
                RecipientId = recipientId,
                ChildId = childId,
                ClinicId = clinicId,
                Title = title,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
