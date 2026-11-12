using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.DTOs
{
    public class NotificationItemDto
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public NotificationType Type { get; set; }

        // Null when the notification isn't about a book, or the book has been deleted
        public int? BookId { get; set; }
    }
}
