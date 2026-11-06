
using NyriaNest.Api.Models.Books.BookDTOs;

namespace NyriaNest.Api.Models
{
    public class CheckedOutItemDto
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public string UserId { get; set; }
        public SlimBookDto Book { get; set; }
        public DateTime CheckOutDate { get; set; }
        public DateTime ReturnDate { get; set; }
        public DateTime? ReminderEmailSent { get; set; }
    }
}
