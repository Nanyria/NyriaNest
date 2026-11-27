using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.DTOs.Books
{
    /// <summary>
    /// What the edit book page loads (GET api/Book/{id}): the editable fields from
    /// SlimBookDto, plus what only the server sets. The page still sends back a plain
    /// SlimBookDto, so a client can't set the EPUB fields (#76).
    /// </summary>
    public class EditBookDto : SlimBookDto
    {
        public int BookId { get; set; }
        public bool HasEpub { get; set; }
        public string? EpubFileName { get; set; }
        public long? EpubFileSize { get; set; }

        // #83: whose book it is (the owner's username); both null = the shared library.
        // The admin edit page asks for a reason when the book is someone else's.
        public string? OwnerId { get; set; }
        public string? OwnerName { get; set; }
    }

    /// <summary>
    /// A book on My library (GET api/my/books): the edit fields plus where the book is.
    /// The loan fields are null when it's on the owner's shelf (#83).
    /// </summary>
    public class MyBookDto : EditBookDto
    {
        public BookStatusEnum BookStatus { get; set; }
        // Lost/Removed by an admin: hidden everywhere else, but the owner still sees it here
        public BookStatusEnum? AdminStatus { get; set; }
        public string? LentTo { get; set; }
        public DateTime? LentOn { get; set; }
        public DateTime? DueDate { get; set; }
        // #85: the friend says they returned it on this date; the owner hasn't confirmed yet
        public DateTime? ReportedReturnedDate { get; set; }
        // The "Added" column on My owned books
        public DateTime DateAdded { get; set; }
    }
}
