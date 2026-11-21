using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #116: characterization tests for the BookService / EpubService endpoints that had none,
/// so the move to Books/ and the controller split keep them working as they do today.
/// - GET api/Book/catalogue (admin catalogue: search, filters, sorting, paging)
/// - GET api/Book/{id}/epub (download)
/// - DELETE api/Book/{id}/epub
/// The server is shared by the tests in this class, so each test tags its books
/// with a unique word and searches for it.
/// </summary>
public class BookCatalogueAndEpubTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- GET api/Book/catalogue ----

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.User)]
    public async Task Catalogue_IsForAdminsOnly(string? role)
    {
        var client = role is null ? factory.CreateClient() : (await factory.LoginAsAsync(role)).Client;

        var response = await client.GetAsync("api/Book/catalogue", Ct);

        response.StatusCode.Should().Be(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Catalogue_SearchesTitleAndAuthor_IgnoringCase_SortedByTitle_InPages()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var tag = Tag();
        await AddBooksAsync(
            Book($"C {tag}"),
            Book($"A {tag}"),
            Book("B by author", author: $"Author {tag}"),
            Book("Something else"));

        var page1 = await CatalogueAsync(admin, $"q={tag.ToUpperInvariant()}&pageSize=2");
        var page2 = await CatalogueAsync(admin, $"q={tag}&pageSize=2&page=2");

        Titles(page1).Should().Equal($"A {tag}", "B by author");
        Titles(page2).Should().Equal($"C {tag}");
        ((int)page2["totalCount"]!).Should().Be(3);
        ((int)page2["page"]!).Should().Be(2);
        ((int)page2["pageSize"]!).Should().Be(2);
    }

    [Fact]
    public async Task Catalogue_PageSize_IsKeptBetween1And50()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var tooBig = await CatalogueAsync(admin, "pageSize=500&page=0");
        var tooSmall = await CatalogueAsync(admin, "pageSize=0");

        ((int)tooBig["pageSize"]!).Should().Be(50);
        ((int)tooBig["page"]!).Should().Be(1);
        ((int)tooSmall["pageSize"]!).Should().Be(1);
    }

    [Fact]
    public async Task Catalogue_SortYearDesc_PutsTheNewestFirst()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var tag = Tag();
        await AddBooksAsync(Book($"Old {tag}", year: 1990), Book($"New {tag}", year: 2010), Book($"Mid {tag}", year: 2000));

        var result = await CatalogueAsync(admin, $"q={tag}&sort=YearDesc");

        Titles(result).Should().Equal($"New {tag}", $"Mid {tag}", $"Old {tag}");
    }

    [Fact]
    public async Task Catalogue_StatusFilter_IsWorkedOutFromLoansAndReservations()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var tag = Tag();
        var free = Book($"Free {tag}");
        var lent = Book($"Lent {tag}");
        var reserved = Book($"Reserved {tag}");
        await AddBooksAsync(free, lent, reserved);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = lent.BookId, UserId = reader.Id, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            db.ReservationItems.Add(new() { BookId = reserved.BookId, UserId = reader.Id, ReservationDate = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        });

        Titles(await CatalogueAsync(admin, $"q={tag}&status=available")).Should().Equal($"Free {tag}");
        Titles(await CatalogueAsync(admin, $"q={tag}&status=checkedOut")).Should().Equal($"Lent {tag}");
        Titles(await CatalogueAsync(admin, $"q={tag}&status=reserved")).Should().Equal($"Reserved {tag}");
        Titles(await CatalogueAsync(admin, $"q={tag}&status=overdue")).Should().BeEmpty();
    }

    [Fact]
    public async Task Catalogue_ShowsTheBorrower_DueDate_AndNextInLine()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var borrower = await factory.CreateUserAsync();
        var waiting = await factory.CreateUserAsync();
        var tag = Tag();
        var book = Book($"Busy {tag}");
        await AddBooksAsync(book);
        var due = DateTime.UtcNow.AddDays(14);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = book.BookId, UserId = borrower.Id, CheckOutDate = DateTime.UtcNow, ReturnDate = due });
            db.ReservationItems.Add(new() { BookId = book.BookId, UserId = waiting.Id, ReservationDate = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        });

        var item = (await CatalogueAsync(admin, $"q={tag}"))["items"]![0]!;

        ((int)item["bookId"]!).Should().Be(book.BookId);
        ((string)item["borrowerName"]!).Should().Be(borrower.UserName);
        ((DateTime)item["dueDate"]!).Should().BeCloseTo(due, TimeSpan.FromSeconds(1));
        ((bool)item["isOverdue"]!).Should().BeFalse();
        ((string)item["nextInLineName"]!).Should().Be(waiting.UserName);
        ((bool)item["hasEpub"]!).Should().BeFalse();
    }

    // #83 step 8a: an owner lends to a name (someone without an account)
    [Fact]
    public async Task Catalogue_ShowsTheBorrowerName_OfALoanByName()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var tag = Tag();
        var book = Book($"Lent {tag}");
        book.OwnerId = owner.Id;
        await AddBooksAsync(book);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = book.BookId, BorrowerName = "Ruth (neighbour)", CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            await db.SaveChangesAsync(Ct);
        });

        var item = (await CatalogueAsync(admin, $"q={tag}"))["items"]![0]!;

        ((string)item["borrowerName"]!).Should().Be("Ruth (neighbour)");
    }

    // #83 step 8a: whose book it is; no owner = the shared library
    [Fact]
    public async Task Catalogue_ShowsTheOwner_AndNoOwnerForSharedBooks()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var tag = Tag();
        var owned = Book($"A owned {tag}");
        owned.OwnerId = owner.Id;
        var shared = Book($"B shared {tag}", type: BookTypeEnums.EBook);
        await AddBooksAsync(owned, shared);

        var items = (await CatalogueAsync(admin, $"q={tag}"))["items"]!.AsArray();

        items.Select(i => (int)i!["bookId"]!).Should().Equal(owned.BookId, shared.BookId);
        ((string?)items[0]!["ownerId"]).Should().Be(owner.Id);
        ((string?)items[0]!["ownerName"]).Should().Be(owner.UserName);
        ((string?)items[1]!["ownerId"]).Should().BeNull();
        ((string?)items[1]!["ownerName"]).Should().BeNull();
    }

    [Fact]
    public async Task Catalogue_StatusFilter_TellsBooksOnTheShelfFromEbooksWithAndWithoutAFile()
    {
        // #83: Available is for physical books; an e-book is Ready (has an EPUB) or
        // NoFile. Like Overdue, neither is stored. A taken-down book is in none of them.
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var tag = Tag();
        var lent = Book($"Lent {tag}");
        var withFile = Book($"With file {tag}", type: BookTypeEnums.EBook);
        withFile.EpubFileKey = $"{tag}/book.epub";
        var removed = Book($"Removed {tag}", type: BookTypeEnums.EBook);
        removed.EpubFileKey = $"{tag}/removed.epub";
        removed.AdminStatus = BookStatusEnum.Removed;
        await AddBooksAsync(Book($"Paper {tag}"), lent, withFile, Book($"Without file {tag}", type: BookTypeEnums.EBook), removed);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = lent.BookId, BorrowerName = "Ruth (neighbour)", CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            await db.SaveChangesAsync(Ct);
        });

        Titles(await CatalogueAsync(admin, $"q={tag}&status=available")).Should().Equal($"Paper {tag}");
        Titles(await CatalogueAsync(admin, $"q={tag}&status=ready")).Should().Equal($"With file {tag}");
        Titles(await CatalogueAsync(admin, $"q={tag}&status=noFile")).Should().Equal($"Without file {tag}");
    }

    [Theory]
    [InlineData("returned")]
    [InlineData("added")]
    public async Task Catalogue_FilteringByReturnedOrAdded_Returns400(string status)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"api/Book/catalogue?status={status}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Can't filter by");
    }

    // ---- GET api/Book/{bookId}/epub ----

    // #83: a logged-in reader can read any e-book they can see (BookVisibility), no loan needed.
    // A book they can't see is a 404, so its existence is never confirmed.

    [Fact]
    public async Task Download_ByAReader_OfASharedEbook_ReturnsTheFile_NamedAfterTheTitle()
    {
        var (reader, _) = await factory.LoginAsAsync(Roles.User);
        var tag = Tag();
        var book = await AddEbookWithFileAsync($"{tag}/Part");
        var content = StoreFile(book.EpubFileKey!);

        var response = await reader.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "no loan is needed to read a shared-library e-book");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/epub+zip");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be($"{tag}_Part.epub",
            "characters that aren't allowed in file names become _");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(content);
    }

    [Fact]
    public async Task Download_ByAReader_OfTheirOwnEbook_ReturnsTheFile()
    {
        // Owners can't add e-books through the API yet, so the book is arranged in the DB
        var (reader, user) = await factory.LoginAsAsync(Roles.User);
        var book = await AddEbookWithFileAsync(Tag(), b => b.OwnerId = user.Id);
        var content = StoreFile(book.EpubFileKey!);

        var response = await reader.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(content);
    }

    [Fact]
    public async Task Download_ByAReader_OfSomeoneElsesEbook_Returns404()
    {
        var (reader, _) = await factory.LoginAsAsync(Roles.User);
        var owner = await factory.CreateUserAsync();
        var book = await AddEbookWithFileAsync(Tag(), b => b.OwnerId = owner.Id);
        StoreFile(book.EpubFileKey!);

        var response = await reader.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public async Task Download_ByAReader_OfAHiddenSharedEbook_Returns404(BookStatusEnum adminStatus)
    {
        var (reader, _) = await factory.LoginAsAsync(Roles.User);
        var book = await AddEbookWithFileAsync(Tag(), b => b.AdminStatus = adminStatus);
        StoreFile(book.EpubFileKey!);

        var response = await reader.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_ByAnAdmin_OfARemovedEbook_ReturnsTheFile()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var book = await AddEbookWithFileAsync(Tag(), b => b.AdminStatus = BookStatusEnum.Removed);
        var content = StoreFile(book.EpubFileKey!);

        var response = await admin.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "admins can read every book, e.g. to check an uploaded file");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(content);
    }

    [Fact]
    public async Task Download_ByAnAdmin_WorksWithoutALoan()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var book = await AddEbookWithFileAsync(Tag());
        var content = StoreFile(book.EpubFileKey!);

        var response = await admin.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(content);
    }

    [Fact]
    public async Task Download_WithoutLoggingIn_Returns401()
    {
        var book = await AddEbookWithFileAsync(Tag());

        var response = await factory.CreateClient().GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Download_OfABookWithoutAnEpub_OrAnUnknownBook_Returns404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var book = Book(Tag(), type: BookTypeEnums.EBook);
        await AddBooksAsync(book);

        (await admin.GetAsync($"api/Book/{book.BookId}/epub", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync("api/Book/999999/epub", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_WhenTheFileIsMissingFromStorage_Returns404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var book = await AddEbookWithFileAsync(Tag());
        factory.FileStorage
            .Setup(s => s.OpenReadAsync(book.EpubFileKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        var response = await admin.GetAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- DELETE api/Book/{bookId}/epub ----

    [Fact]
    public async Task Delete_RemovesTheFile_AndClearsTheBooksEpubFields()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var book = await AddEbookWithFileAsync(Tag());
        var key = book.EpubFileKey!;

        var response = await admin.DeleteAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ((bool)(await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!).Should().BeTrue();
        factory.FileStorage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        var saved = await factory.WithDbAsync(db => db.Books.SingleAsync(b => b.BookId == book.BookId, Ct));
        saved.EpubFileKey.Should().BeNull();
        saved.EpubFileName.Should().BeNull();
        saved.EpubFileSize.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ABookWithoutAnEpub_OrAnUnknownBook_Returns404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var book = Book(Tag(), type: BookTypeEnums.EBook);
        await AddBooksAsync(book);

        var noFile = await admin.DeleteAsync($"api/Book/{book.BookId}/epub", Ct);
        var unknown = await admin.DeleteAsync("api/Book/999999/epub", Ct);

        noFile.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await noFile.Content.ReadAsStringAsync(Ct)).Should().Contain("This book has no EPUB file.");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync(Ct)).Should().Contain("Book 999999 was not found.");
    }

    [Fact]
    public async Task Delete_IsForAdminsOnly()
    {
        var (reader, _) = await factory.LoginAsAsync(Roles.User);
        var book = await AddEbookWithFileAsync(Tag());

        var response = await reader.DeleteAsync($"api/Book/{book.BookId}/epub", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var saved = await factory.WithDbAsync(db => db.Books.SingleAsync(b => b.BookId == book.BookId, Ct));
        saved.EpubFileKey.Should().Be(book.EpubFileKey);
    }

    // ---- Helpers ----

    private static string Tag() => "t" + Guid.NewGuid().ToString("N")[..10];

    private static Book Book(string title, string author = "Test Author", int year = 2020,
        BookTypeEnums type = BookTypeEnums.Paperback)
    {
        var book = TestData.Book(title, type);
        book.Author = author;
        book.PublicationYear = year;
        return book;
    }

    private Task AddBooksAsync(params Book[] books) => factory.WithDbAsync(async db =>
    {
        db.Books.AddRange(books);
        await db.SaveChangesAsync(Ct);
    });

    private async Task<Book> AddEbookWithFileAsync(string title, Action<Book>? arrange = null)
    {
        var book = Book(title, type: BookTypeEnums.EBook);
        book.EpubFileKey = $"{Guid.NewGuid():N}/book.epub";
        book.EpubFileName = "book.epub";
        book.EpubFileSize = 3;
        arrange?.Invoke(book);
        await AddBooksAsync(book);
        return book;
    }

    /// <summary>Makes the storage mock return some bytes for <paramref name="key"/>, a new stream each time.</summary>
    private byte[] StoreFile(string key)
    {
        byte[] content = [1, 2, 3];
        factory.FileStorage
            .Setup(s => s.OpenReadAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(content));
        return content;
    }

    private async Task<JsonNode> CatalogueAsync(HttpClient admin, string query)
    {
        var response = await admin.GetAsync($"api/Book/catalogue?{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
    }

    private static List<string> Titles(JsonNode page) =>
        page["items"]!.AsArray().Select(i => (string)i!["title"]!).ToList();
}
