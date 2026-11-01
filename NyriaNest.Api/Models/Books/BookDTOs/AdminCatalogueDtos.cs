using NyriaNest.Api.Helpers.Enums;

namespace NyriaNest.Api.Models.Books.BookDTOs
{
    // Query string for GET /api/Book/catalogue. Every filter is optional.
    // Enums accept names in any case or numbers, e.g. ?status=checkedOut or ?status=2.
    public class AdminCatalogueQuery
    {
        public string? Q { get; set; }              // matches title or author
        public GenreEnums? Genre { get; set; }
        public BookTypeEnums? BookType { get; set; }

        // Worked out from loans, reservations and the admin status, not read from the
        // stored BookStatus (Overdue, for example, comes from the due date).
        // Returned and Added aren't states a book stays in, so they're rejected.
        public BookStatusEnum? Status { get; set; }

        public bool NeedsAttention { get; set; }    // e-books with no EPUB file
        public CatalogueSort Sort { get; set; } = CatalogueSort.TitleAsc;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public enum CatalogueSort
    {
        TitleAsc,
        TitleDesc,
        AuthorAsc,
        YearDesc,   // newest first
        YearAsc,    // oldest first
    }

    // One page of results plus the total, so the UI can show "1–10 of 42"
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // Admin-only. Includes borrower details, so never return it from LibraryController.
    public class AdminBookDto
    {
        public int BookId { get; set; }
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string? CoverImagePath { get; set; }
        public GenreEnums Genre { get; set; }
        public LanguageEnums Language { get; set; }
        public BookTypeEnums BookType { get; set; }
        public int PublicationYear { get; set; }
        public bool HasEpub { get; set; }

        public BookStatusEnum BookStatus { get; set; }
        public bool IsOverdue { get; set; }
        public string? BorrowerName { get; set; }
        public DateTime? DueDate { get; set; }

        // First person in the reservation queue, and when the book is expected
        // to reach them. An estimate, not a hold expiry: there's no hold time limit.
        public string? NextInLineName { get; set; }
        public DateTime? ExpectedDate { get; set; }

        public string? AdminStatusNotes { get; set; }
    }
}
