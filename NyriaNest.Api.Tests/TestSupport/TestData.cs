using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models.Books;
using NyriaNest.Api.Models.History;
using NyriaNest.Api.Models.Users;

namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>
/// Small builders with sensible defaults. Set only what a test cares about,
/// e.g. <c>TestData.Book(type: BookTypeEnums.EBook)</c>.
/// Note: the database already holds the four seeded books (IDs 1001–1004).
/// </summary>
public static class TestData
{
    public static Book Book(
        string title = "Test book",
        BookTypeEnums type = BookTypeEnums.Paperback,
        BookStatusEnum status = BookStatusEnum.Available) => new()
    {
        Title = title,
        Author = "Test Author",
        BookType = type,
        BookStatus = status,
        Genre = GenreEnums.Fiction,
        Language = LanguageEnums.English,
        PublicationYear = 2020,
    };

    public static Book Ebook(string title = "Test e-book") =>
        Book(title, BookTypeEnums.EBook);

    public static User User(string name = "reader", string role = Roles.User) => new()
    {
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@test.local",
        NormalizedEmail = $"{name}@test.local".ToUpperInvariant(),
        FirstName = "Test",
        LastName = name,
        Role = role,
    };

    /// <summary>A loan of <paramref name="book"/> to <paramref name="user"/>, due in <paramref name="dueInDays"/> days.</summary>
    public static CheckedOutItem Loan(Book book, User user, int dueInDays = 14) => new()
    {
        Book = book,
        BookId = book.BookId,
        User = user,
        UserId = user.Id,
        CheckOutDate = DateTime.UtcNow,
        ReturnDate = DateTime.UtcNow.AddDays(dueInDays),
    };

    /// <summary>A reservation made <paramref name="daysAgo"/> days ago (queue order is by reservation date).</summary>
    public static ReservationItem Reservation(Book book, User user, int daysAgo = 0) => new()
    {
        Book = book,
        BookId = book.BookId,
        User = user,
        UserId = user.Id,
        ReservationDate = DateTime.UtcNow.AddDays(-daysAgo),
    };
}
