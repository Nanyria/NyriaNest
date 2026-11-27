using NyriaNest.Application.DTOs.Books;

namespace NyriaNest.Application.DTOs.History
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
        // #85: whose book it is (a friend), or null for a shared-library book
        public string? OwnerUserName { get; set; }
        // #85: I said I returned it on this date; the owner hasn't confirmed yet
        public DateTime? ReportedReturnedDate { get; set; }
    }
}
