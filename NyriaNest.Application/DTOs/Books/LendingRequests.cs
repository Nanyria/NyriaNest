using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Application.DTOs.Books
{
    /// <summary>
    /// The body of POST api/my/books/{id}/lend (#83). Exactly one borrower: a member's user id
    /// or the name of someone without an account. Members are refused until friends exist (#85).
    /// The name is trimmed, then checked (not blank, at most 100), so it has no attributes here.
    /// LentOn is for recording a loan after the fact: today or earlier, today when left out.
    /// </summary>
    public record LendBookRequest(string? BorrowerUserId, string? BorrowerName, DateOnly? LentOn);

    /// <summary>
    /// The body of POST api/my/books/{id}/return (#83), optional. ReturnedOn: today or earlier,
    /// not before the book was lent, today when left out.
    /// </summary>
    public record ReturnBookRequest(DateOnly? ReturnedOn);

    /// <summary>
    /// The body of POST api/Book/{id}/lend: the owner's request plus a reason, required when
    /// the admin isn't the book's owner. It goes into the history and the owner's notification.
    /// </summary>
    public record AdminLendBookRequest(
        string? BorrowerUserId,
        string? BorrowerName,
        DateOnly? LentOn,
        [MaxLength(BookChangeReason.MaxLength)] string? Reason);

    /// <summary>The body of POST api/Book/{id}/return: the owner's request plus a reason, as for lending.</summary>
    public record AdminReturnBookRequest(
        DateOnly? ReturnedOn,
        [MaxLength(BookChangeReason.MaxLength)] string? Reason);

    public static class BorrowerNameLimit
    {
        public const int MaxLength = 100; // CheckedOutItem.BorrowerName's column
    }
}
