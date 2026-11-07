using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #76: the BookController endpoints that returned the Book entity. For a book with
/// reservations that looped (reservation → book → reservations …) and ended in a 500.
/// - GET api/Book/{id} returns an EditBookDto: what the edit page reads, nothing more.
/// - Set / clear admin status return true.
/// - Add book / add e-book return the new book's id.
/// </summary>
public class BookResponsesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] EditBookFields =
    [
        "bookId", "title", "author", "bookDescription", "publicationYear", "coverImagePath",
        "genre", "bookType", "language", "hasEpub", "epubFileName", "epubFileSize",
    ];

    // ---- GET api/Book/{id}: what the edit page loads ----

    [Fact]
    public async Task GetBook_WithAReservationAndAReview_Works()
    {
        // The edit page couldn't open a reserved book: the response looped
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await ReservedAndReviewedBookAsync();

        var response = await admin.GetAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task GetBook_ReturnsExactlyWhatTheEditPageReads()
    {
        // No loans, reservations or reviews: the edit page doesn't use them
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await ReservedAndReviewedBookAsync();

        var result = await ResultOf(await admin.GetAsync($"api/Book/{bookId}", Ct));

        result.AsObject().Select(p => p.Key).Should().BeEquivalentTo(EditBookFields);
    }

    [Fact]
    public async Task GetBook_HasTheBooksValues_IncludingItsEpub()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(b =>
        {
            b.Title = "Edit me";
            b.BookDescription = "About the book";
            b.BookType = BookTypeEnums.EBook;
            b.EpubFileKey = $"{Guid.NewGuid():N}/edit-me.epub";
            b.EpubFileName = "edit-me.epub";
            b.EpubFileSize = 4321;
        });

        var result = await ResultOf(await admin.GetAsync($"api/Book/{bookId}", Ct));

        ((int)result["bookId"]!).Should().Be(bookId);
        ((string?)result["title"]).Should().Be("Edit me");
        ((string?)result["bookDescription"]).Should().Be("About the book");
        ((int)result["bookType"]!).Should().Be((int)BookTypeEnums.EBook);
        ((bool)result["hasEpub"]!).Should().BeTrue();
        ((string?)result["epubFileName"]).Should().Be("edit-me.epub");
        ((long?)result["epubFileSize"]).Should().Be(4321);
    }

    // ---- Admin status: mark as lost / removed, and restore ----

    [Fact]
    public async Task SetAdminStatus_OnAReservedBook_Works_AndReturnsTrue()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await ReservedAndReviewedBookAsync();

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}/admin-status",
            new { status = BookStatusEnum.Lost, notes = "Gone missing" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ((bool)(await ResultOf(response))).Should().BeTrue();
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Lost);
    }

    [Fact]
    public async Task ClearAdminStatus_OnAReservedBook_Works_AndReturnsTrue()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await ReservedAndReviewedBookAsync(b => { b.AdminStatus = BookStatusEnum.Lost; b.BookStatus = BookStatusEnum.Lost; });

        var response = await admin.DeleteAsync($"api/Book/{bookId}/admin-status", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ((bool)(await ResultOf(response))).Should().BeTrue();
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Reserved, "the queue is still there");
    }

    // ---- Adding a book: returns the new book's id ----

    [Fact]
    public async Task AddBook_ReturnsTheNewBooksId()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var title = $"New book {Guid.NewGuid():N}";

        var response = await admin.PostAsJsonAsync("api/Book", new
        {
            title,
            author = "Test Author",
            publicationYear = 2020,
            genre = GenreEnums.Fiction,
            bookType = BookTypeEnums.Paperback,
            language = LanguageEnums.English,
        }, Ct);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        var id = (int)(await ResultOf(response));
        (await factory.WithDbAsync(db => db.Books.Where(b => b.BookId == id).Select(b => b.Title).SingleAsync(Ct))).Should().Be(title);
    }

    [Fact]
    public async Task AddEbook_ReturnsTheNewBooksId()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var title = $"New e-book {Guid.NewGuid():N}";
        factory.FileStorage
            .Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, CancellationToken _) => key);

        var response = await admin.PostAsync("api/Book/ebook", EbookForm(title), Ct);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        var id = (int)(await ResultOf(response));
        var book = await factory.WithDbAsync(db => db.Books.AsNoTracking().SingleAsync(b => b.BookId == id, Ct));
        book.Title.Should().Be(title);
        book.EpubFileKey.Should().NotBeNull();
    }

    // ---- Helpers ----

    private static async Task<JsonNode> ResultOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;

    private Task<int> AddBookAsync(Action<Book>? setup = null) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book();
        setup?.Invoke(book);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    /// <summary>A book with one reservation and one review: the case that looped.</summary>
    private async Task<int> ReservedAndReviewedBookAsync(Action<Book>? setup = null)
    {
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(b => { b.BookStatus = BookStatusEnum.Reserved; setup?.Invoke(b); });
        await factory.WithDbAsync(async db =>
        {
            db.ReservationItems.Add(new() { BookId = bookId, UserId = reader.Id, ReservationDate = DateTime.UtcNow });
            db.ReviewItems.Add(new() { BookId = bookId, UserId = reader.Id, ReviewHeader = "Good", ReviewText = "Liked it" });
            await db.SaveChangesAsync(Ct);
        });
        return bookId;
    }

    private Task<BookStatusEnum> StatusOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct));

    /// <summary>The start of a real EPUB (what the API checks), as in EpubUploadTests.</summary>
    private static MultipartFormDataContent EbookForm(string title)
    {
        var name = Encoding.ASCII.GetBytes("mimetype");
        var mime = Encoding.ASCII.GetBytes("application/epub+zip");
        var header = new byte[30];
        header[0] = 0x50; header[1] = 0x4B; header[2] = 0x03; header[3] = 0x04;
        BitConverter.GetBytes((uint)mime.Length).CopyTo(header, 18);
        BitConverter.GetBytes((uint)mime.Length).CopyTo(header, 22);
        BitConverter.GetBytes((ushort)name.Length).CopyTo(header, 26);
        var file = new ByteArrayContent([.. header, .. name, .. mime, .. Encoding.ASCII.GetBytes("rest of the book")]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");

        return new MultipartFormDataContent
        {
            { file, "file", "new.epub" },
            { new StringContent(title), "title" },
            { new StringContent("Test Author"), "author" },
            { new StringContent("2020"), "publicationYear" },
            { new StringContent(((int)GenreEnums.Fiction).ToString()), "genre" },
            { new StringContent(((int)LanguageEnums.English).ToString()), "language" },
        };
    }
}
