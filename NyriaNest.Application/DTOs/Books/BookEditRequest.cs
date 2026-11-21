using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Application.DTOs.Books
{
    /// <summary>
    /// What the admin edit page sends (PUT api/Book/{id}): every field, plus a reason.
    /// The reason is required only when the book belongs to someone else (#83); it goes
    /// into the book's history. The page picks it from a list or writes its own.
    /// </summary>
    public class BookEditRequest : SlimBookDto
    {
        [MaxLength(BookChangeReason.MaxLength)]
        public string? Reason { get; set; }
    }

    /// <summary>The body of DELETE api/Book/{id}. Both parts are optional, as for SlimBookDto edits.</summary>
    public record BookDeleteRequest(
        [MaxLength(BookChangeReason.MaxLength)] string? Reason,
        bool ConfirmLent);

    public static class BookChangeReason
    {
        public const int MaxLength = 200;
    }
}
