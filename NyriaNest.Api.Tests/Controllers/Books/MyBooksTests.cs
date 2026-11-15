using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #83 step 3a: api/my/books, where any logged-in user adds, edits and deletes
/// their own books. Someone else's book (or a shared-library one) looks missing (404).
/// Own books are physical for now; e-books come later, so the allowed types live in one list.
/// </summary>
public class MyBooksTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Add ----

    [Theory]
    [InlineData(BookTypeEnums.Paperback)]
    [InlineData(BookTypeEnums.Hardcover)]
    public async Task Add_APhysicalBook_MakesMeTheOwner_AndRecordsThatIAddedIt(BookTypeEnums type)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PostAsJsonAsync("api/my/books", Body(title: "My new book", bookType: type), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var bookId = (await ResultAsync(response)).GetInt32();
        var book = await BookAsync(bookId);
        book.OwnerId.Should().Be(me.Id);
        book.Title.Should().Be("My new book");
        book.BookType.Should().Be(type);
        (await factory.WithDbAsync(db => db.StatusHistoryItems.AnyAsync(
            h => h.BookId == bookId && h.UserId == me.Id && h.BookStatus == BookStatusEnum.Added, Ct)))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(BookTypeEnums.EBook)]
    [InlineData(BookTypeEnums.Audiobook)]
    [InlineData(BookTypeEnums.Undefined)]
    public async Task Add_ATypeOwnersCantAddYet_Returns400_AndAddsNothing(BookTypeEnums type)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PostAsJsonAsync("api/my/books", Body(bookType: type), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await factory.WithDbAsync(db => db.Books.AnyAsync(b => b.OwnerId == me.Id, Ct))).Should().BeFalse();
    }

    [Fact]
    public async Task Add_WithoutLogin_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("api/my/books", Body(), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Read ----

    [Fact]
    public async Task List_ShowsOnlyMyBooks()
    {
        var (client, me) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        var mine = await AddBookAsync(me.Id, "Mine");
        await AddBookAsync(someoneElse.Id, "Theirs");
        await AddBookAsync(ownerId: null, "Shared");

        var response = await client.GetAsync("api/my/books", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var ids = (await ResultAsync(response)).EnumerateArray().Select(b => b.GetProperty("bookId").GetInt32());
        ids.Should().Equal(mine);
    }

    [Fact]
    public async Task Get_MyBook_ReturnsIt()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id, "Mine");

        var response = await client.GetAsync($"api/my/books/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ResultAsync(response)).GetProperty("title").GetString().Should().Be("Mine");
    }

    // ---- Edit ----

    [Fact]
    public async Task Edit_MyBook_SavesWhatWasSent()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id, "Old title");

        var response = await client.PutAsJsonAsync($"api/my/books/{bookId}",
            Body(title: "New title", bookType: BookTypeEnums.Hardcover), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.Title.Should().Be("New title");
        book.BookType.Should().Be(BookTypeEnums.Hardcover);
        book.OwnerId.Should().Be(me.Id);
    }

    [Fact]
    public async Task Edit_ToATypeOwnersCantHaveYet_Returns400_AndKeepsTheBook()
    {
        // Add and edit share one type check; the Add theory tries every refused type
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id, "Mine");

        var response = await client.PutAsJsonAsync($"api/my/books/{bookId}", Body(title: "Changed", bookType: BookTypeEnums.EBook), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var book = await BookAsync(bookId);
        book.Title.Should().Be("Mine");
        book.BookType.Should().Be(BookTypeEnums.Paperback);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_MyBook_RemovesIt()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id, "Mine");

        var response = await DeleteAsync(client, bookId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookExistsAsync(bookId)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_MyLentBook_WithoutConfirming_Returns409_AndKeepsTheBookAndTheLoan()
    {
        // The client asks "has it been returned? delete anyway?" and sends confirmLent
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, borrowerName: "Ruth (neighbour)");

        var response = await DeleteAsync(client, bookId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Ruth (neighbour)");
        (await BookExistsAsync(bookId)).Should().BeTrue();
        (await LoanCountOf(bookId)).Should().Be(1);
    }

    [Fact]
    public async Task Delete_MyLentBook_WhenConfirmed_RemovesTheBookAndTheLoan()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, borrowerName: "Ruth (neighbour)");

        var response = await DeleteAsync(client, bookId, new { confirmLent = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookExistsAsync(bookId)).Should().BeFalse();
        (await LoanCountOf(bookId)).Should().Be(0);
    }

    // ---- Not mine: someone else's book or a shared-library book looks missing ----

    public static TheoryData<bool> NotMine => new() { true, false }; // true = another user's, false = shared library

    [Theory]
    [MemberData(nameof(NotMine))]
    public async Task Get_ABookThatIsntMine_Returns404(bool ownedBySomeoneElse)
    {
        var (client, _) = await factory.LoginAsAsync();
        var bookId = await AddNotMineAsync(ownedBySomeoneElse);

        var response = await client.GetAsync($"api/my/books/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(NotMine))]
    public async Task Edit_ABookThatIsntMine_Returns404_AndChangesNothing(bool ownedBySomeoneElse)
    {
        var (client, _) = await factory.LoginAsAsync();
        var bookId = await AddNotMineAsync(ownedBySomeoneElse);

        var response = await client.PutAsJsonAsync($"api/my/books/{bookId}", Body(title: "Hijacked"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BookAsync(bookId)).Title.Should().Be("Not mine");
    }

    [Theory]
    [MemberData(nameof(NotMine))]
    public async Task Delete_ABookThatIsntMine_Returns404_AndKeepsIt(bool ownedBySomeoneElse)
    {
        var (client, _) = await factory.LoginAsAsync();
        var bookId = await AddNotMineAsync(ownedBySomeoneElse);

        var response = await DeleteAsync(client, bookId, new { confirmLent = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BookExistsAsync(bookId)).Should().BeTrue();
    }

    // ---- Helpers ----

    /// <summary>What the add/edit form sends: every field.</summary>
    private static object Body(
        string title = "Test book",
        BookTypeEnums bookType = BookTypeEnums.Paperback) => new
    {
        title,
        author = "Test Author",
        bookDescription = "A description",
        publicationYear = 2020,
        coverImagePath = (string?)null,
        genre = GenreEnums.Fiction,
        bookType,
        language = LanguageEnums.English,
    };

    private Task<HttpResponseMessage> DeleteAsync(HttpClient client, int bookId, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/my/books/{bookId}");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("result").Clone();
    }

    private Task<int> AddBookAsync(string? ownerId, string title) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title);
        book.OwnerId = ownerId;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private async Task<int> AddNotMineAsync(bool ownedBySomeoneElse)
    {
        var owner = ownedBySomeoneElse ? (await factory.CreateUserAsync()).Id : null;
        return await AddBookAsync(owner, "Not mine");
    }

    private Task<int> AddLentBookAsync(string ownerId, string borrowerName) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book("Lent out", status: BookStatusEnum.CheckedOut);
        book.OwnerId = ownerId;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        db.CheckOutItems.Add(new CheckedOutItem
        {
            BookId = book.BookId,
            BorrowerName = borrowerName,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(14),
        });
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<Book> BookAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AsNoTracking().SingleAsync(b => b.BookId == bookId, Ct));

    private Task<bool> BookExistsAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AnyAsync(b => b.BookId == bookId, Ct));

    private Task<int> LoanCountOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.CountAsync(l => l.BookId == bookId, Ct));
}
