namespace NyriaNest.Application.DTOs.History
{
    /// <summary>
    /// One place in a book's reservation queue, for admins (GET api/Book/{bookId}/reservations).
    /// Readers never get this: they only see their own place, in the library list (#71).
    /// </summary>
    public class BookReservationDto
    {
        public int ReservationId { get; set; }
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
        public DateTime ReservationDate { get; set; }
        public DateTime? AvailabilityDate { get; set; }
        /// <summary>1 = next in line.</summary>
        public int QueuePosition { get; set; }
    }
}
