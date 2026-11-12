using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Domain.Entities
{
    public class NotificationItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }

        [Required]
        public string Message { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public NotificationType Type { get; set; } = NotificationType.BookRemoved;

        // The book this is about, if any. Becomes null when the book is deleted,
        // so the notification stays but no longer links anywhere.
        public int? BookId { get; set; }

        [ForeignKey("BookId")]
        public Book? Book { get; set; }
    }
}
