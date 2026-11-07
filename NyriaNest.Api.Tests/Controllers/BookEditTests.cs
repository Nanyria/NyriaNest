using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #39 part C: PUT api/Book/{bookId}, which the edit book page uses. It always
/// sends every field, so what it sends is what gets saved.
/// </summary>
public class BookEditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyDescriptionAndCover_AreSavedAsNull_ClearingTheOldValues(string empty)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(b => { b.BookDescription = "Old description"; b.CoverImagePath = "old-cover.jpg"; });

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body(bookDescription: empty, coverImagePath: empty), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.BookDescription.Should().BeNull();
        book.CoverImagePath.Should().BeNull();
    }

    [Fact]
    public async Task GenreLanguageAndType_AreSavedAsSent_IncludingTheDefaultValues()
    {
        // 0 is None / Undefined, and used to be ignored as "not sent"
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(b => { b.Genre = GenreEnums.Fiction; b.Language = LanguageEnums.English; b.BookType = BookTypeEnums.Paperback; });

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}",
            Body(genre: default(GenreEnums), language: default(LanguageEnums), bookType: default(BookTypeEnums)), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.Genre.Should().Be(default(GenreEnums));
        book.Language.Should().Be(default(LanguageEnums));
        book.BookType.Should().Be(default(BookTypeEnums));
    }

    [Fact]
    public async Task ChangingAnEbookToAnotherType_DropsItsEpub_AndDeletesTheFile()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var key = $"{Guid.NewGuid():N}/file.epub";
        var bookId = await AddEbookWithEpubAsync(key);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body(bookType: BookTypeEnums.Paperback), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.EpubFileKey.Should().BeNull();
        book.EpubFileName.Should().BeNull();
        book.EpubFileSize.Should().BeNull();
        factory.FileStorage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IfDeletingTheDroppedFileFails_TheUpdateStillSucceeds()
    {
        // The book no longer points at the file, so a leftover file is harmless (it's logged)
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var key = $"{Guid.NewGuid():N}/broken.epub";
        factory.FileStorage
            .Setup(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk said no"));
        var bookId = await AddEbookWithEpubAsync(key);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body(bookType: BookTypeEnums.Hardcover), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.BookType.Should().Be(BookTypeEnums.Hardcover);
        book.EpubFileKey.Should().BeNull();
    }

    [Fact]
    public async Task EditingAnEbook_WithoutChangingItsType_KeepsItsEpub()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var key = $"{Guid.NewGuid():N}/kept.epub";
        var bookId = await AddEbookWithEpubAsync(key);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body(title: "New title", bookType: BookTypeEnums.EBook), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.Title.Should().Be("New title");
        book.EpubFileKey.Should().Be(key);
        factory.FileStorage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EditingABookWithAReservation_Works()
    {
        // Not in #39's list: the endpoint returned the Book entity, and deleting a
        // book with reservations crashed on exactly that (object cycle)
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(b => b.BookStatus = BookStatusEnum.Reserved);
        await factory.WithDbAsync(async db =>
        {
            db.ReservationItems.Add(new() { BookId = bookId, UserId = reader.Id, ReservationDate = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        });

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", Body(title: "Renamed"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookAsync(bookId)).Title.Should().Be("Renamed");
    }

    // ---- Helpers ----

    /// <summary>What the edit page sends: every field.</summary>
    private static object Body(
        string title = "Test book",
        string? bookDescription = "A description",
        string? coverImagePath = "cover.jpg",
        GenreEnums genre = GenreEnums.Fiction,
        LanguageEnums language = LanguageEnums.English,
        BookTypeEnums bookType = BookTypeEnums.Paperback) => new
    {
        title,
        author = "Test Author",
        bookDescription,
        publicationYear = 2020,
        coverImagePath,
        genre,
        bookType,
        language,
    };

    private Task<int> AddBookAsync(Action<Book>? setup = null) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book();
        setup?.Invoke(book);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<int> AddEbookWithEpubAsync(string key) => AddBookAsync(b =>
    {
        b.BookType = BookTypeEnums.EBook;
        b.EpubFileKey = key;
        b.EpubFileName = "file.epub";
        b.EpubFileSize = 1234;
    });

    private Task<Book> BookAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AsNoTracking().SingleAsync(b => b.BookId == bookId, Ct));
}
