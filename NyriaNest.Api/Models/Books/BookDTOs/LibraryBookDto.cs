using NyriaNest.Domain.Enums;

namespace NyriaNest.Api.Models.Books.BookDTOs
{
    // What the public library list needs. No other users' IDs or loan details.
    public class LibraryBookDto
    {
        // Identity
        public int BookId { get; set; }

        // Book info
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string? BookDescription { get; set; }
        public int PublicationYear { get; set; }

        // Classification
        public GenreEnums Genre { get; set; }
        public BookTypeEnums BookType { get; set; }
        public LanguageEnums Language { get; set; }

        // Library status
        public BookStatusEnum BookStatus { get; set; }
        public DateTime AvailabilityDate { get; set; }
        public DateTime DateAdded { get; set; }

        // Files
        public string? CoverImagePath { get; set; }
        public bool HasEpub { get; set; }

        // Aggregates (computed in the query)
        public int ReviewCount { get; set; }
        public double? AverageRating { get; set; }
        public int ReservationCount { get; set; }

        // Only about the logged-in user (false/null when not logged in)
        public bool IsCheckedOutByMe { get; set; }
        public bool IsReservedByMe { get; set; }
        public DateTime? MyReservationAvailableDate { get; set; }
    }
}