using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #83 step 6b: which types a book may have depends on the book, not on who changes it.
/// The shared library has e-books only (ADR 0004); an owned book has the types owners
/// can have (physical for now, e-books later), also when an admin edits it.
/// </summary>
public class SharedLibraryTypeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SharedLibraryMessage = "The shared library has e-books only.";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<BookTypeEnums> NotEbooks => new()
    {
        BookTypeEnums.Paperback, BookTypeEnums.Hardcover, BookTypeEnums.Audiobook, BookTypeEnums.Undefined,
    };

    // ---- The shared library ----

    [Theory]
    [MemberData(nameof(NotEbooks))]
    public async Task AdminAdd_AnythingButAnEbook_Returns400_AndAddsNothing(BookTypeEnums type)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var title = $"Not an e-book {Guid.NewGuid():N}";

        var response = await admin.PostAsJsonAsync("api/Book", Body(title, type), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(SharedLibraryMessage);
        (await factory.WithDbAsync(db => db.Books.AnyAsync(b => b.Title == title, Ct))).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(NotEbooks))]
    public async Task AdminEdit_ASharedBook_ToAnythingButAnEbook_Returns400_AndChangesNothing(BookTypeEnums type)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(ownerId: null, BookTypeEnums.EBook);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body("Changed", type), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(SharedLibraryMessage);
        var book = await BookAsync(bookId);
        book.BookType.Should().Be(BookTypeEnums.EBook);
        book.Title.Should().Be("Original");
    }

    // ---- Someone's own book ----

    [Theory]
    [InlineData(BookTypeEnums.EBook)] // owners get e-books later; then this case moves to "allowed"
    [InlineData(BookTypeEnums.Audiobook)]
    [InlineData(BookTypeEnums.Undefined)]
    public async Task AdminEdit_SomeoneElsesBook_ToATypeOwnersCantHave_Returns400_AndChangesNothing(BookTypeEnums type)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, BookTypeEnums.Paperback);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body("Changed", type, reason: "Wrong or missing details"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var book = await BookAsync(bookId);
        book.BookType.Should().Be(BookTypeEnums.Paperback);
        book.Title.Should().Be("Original");
        (await factory.WithDbAsync(db => db.StatusHistoryItems.AnyAsync(h => h.BookId == bookId, Ct))).Should().BeFalse();
    }

    // ---- Helpers ----

    private static object Body(string title, BookTypeEnums bookType, string? reason = null) => new
    {
        title,
        author = "Test Author",
        bookDescription = "A description",
        publicationYear = 2020,
        coverImagePath = (string?)null,
        genre = GenreEnums.Fiction,
        bookType,
        language = LanguageEnums.English,
        reason,
    };

    private Task<int> AddBookAsync(string? ownerId, BookTypeEnums type) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book("Original", type);
        book.OwnerId = ownerId;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<Book> BookAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AsNoTracking().SingleAsync(b => b.BookId == bookId, Ct));
}
