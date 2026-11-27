using NyriaNest.Application.DTOs.Books;

namespace NyriaNest.Application.DTOs.History
{
    public class ReservationItemDto
    {

        public int Id { get; set; }
        public int BookId { get; set; }
        public SlimBookDto Book { get; set; }
        public DateTime ReservationDate { get; set; }
        public DateTime? AvailabilityDate { get; set; }
        public DateTime? BookIsAvaliableEmailSent { get; set; }
        // #85: whose book it is (only friends' lent books can be reserved)
        public string? OwnerUserName { get; set; }
        // #85: my place in the book's queue, 1 = next in line (as MyQueuePosition in the Library)
        public int QueuePosition { get; set; }
    }
}
