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
/// #83 step 3b: admins editing and deleting books that belong to someone, through api/Book.
/// The rule goes by actor, not role: whenever the actor isn't the owner, a reason is required
/// and recorded in the book's history. An admin delete tells the owner, with the reason
/// (the history goes with the book). Shared-library books work as before: no reason, no history.
/// </summary>
public class AdminOwnedBookTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Edit ----

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AdminEdit_SomeoneElsesBook_WithoutAReason_Returns400_AndChangesNothing(string? reason)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "Original title");

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", EditBody("Changed", reason), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BookAsync(bookId)).Title.Should().Be("Original title");
        (await HistoryOf(bookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminEdit_SomeoneElsesBook_WithAReason_Saves_AndRecordsItInTheHistory()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "Original title", BookStatusEnum.Available);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", EditBody("Fixed title", "Wrong or missing details"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var book = await BookAsync(bookId);
        book.Title.Should().Be("Fixed title");
        book.OwnerId.Should().Be(owner.Id);
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].UserId.Should().Be(adminUser.Id);
        history[0].BookStatus.Should().Be(BookStatusEnum.Available); // the book's current status
        history[0].Notes.Should().Contain("Wrong or missing details");
    }

    [Fact]
    public async Task AdminEdit_ASharedLibraryBook_NeedsNoReason_AndRecordsNothing()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(ownerId: null, "Shared book");

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", EditBody("Renamed shared book", reason: null), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookAsync(bookId)).Title.Should().Be("Renamed shared book");
        (await HistoryOf(bookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminEdit_TheirOwnBook_NeedsNoReason_AndRecordsNothing()
    {
        // By actor, not role: an admin who owns the book is just its owner
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(adminUser.Id, "The admin's own book");

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}", EditBody("Renamed", reason: null), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await HistoryOf(bookId)).Should().BeEmpty();
    }

    // ---- Delete ----

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AdminDelete_SomeoneElsesBook_WithoutAReason_Returns400_KeepsIt_AndTellsNoOne(string? reason)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "Kept");

        var response = await DeleteAsync(admin, bookId, new { reason });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BookExistsAsync(bookId)).Should().BeTrue();
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminDelete_SomeoneElsesBook_WithAReason_DeletesIt_AndTellsTheOwnerWhy()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bystander = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "The Duplicate Book");

        var response = await DeleteAsync(admin, bookId, new { reason = "Duplicate" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookExistsAsync(bookId)).Should().BeFalse();
        var messages = await MessagesOf(owner.Id);
        messages.Should().ContainSingle();
        messages[0].Should().Contain("The Duplicate Book").And.Contain("Duplicate");
        (await MessagesOf(bystander.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminDelete_SomeoneElsesLentBook_WithoutConfirming_Returns409_AndKeepsTheBookAndTheLoan()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddLentBookAsync(owner.Id, borrowerName: "Ruth (neighbour)");

        var response = await DeleteAsync(admin, bookId, new { reason = "Duplicate" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Ruth (neighbour)");
        (await BookExistsAsync(bookId)).Should().BeTrue();
        (await LoanCountOf(bookId)).Should().Be(1);
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminDelete_SomeoneElsesLentBook_WhenConfirmed_RemovesTheBookAndTheLoan_AndTellsTheOwner()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddLentBookAsync(owner.Id, borrowerName: "Ruth (neighbour)");

        var response = await DeleteAsync(admin, bookId, new { reason = "Duplicate", confirmLent = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookExistsAsync(bookId)).Should().BeFalse();
        (await LoanCountOf(bookId)).Should().Be(0);
        (await MessagesOf(owner.Id)).Should().ContainSingle();
    }

    // ---- Helpers ----

    /// <summary>What the admin edit page sends: every field, plus the reason.</summary>
    private static object EditBody(string title, string? reason) => new
    {
        title,
        author = "Test Author",
        bookDescription = "A description",
        publicationYear = 2020,
        coverImagePath = (string?)null,
        genre = GenreEnums.Fiction,
        bookType = BookTypeEnums.Paperback,
        language = LanguageEnums.English,
        reason,
    };

    private Task<HttpResponseMessage> DeleteAsync(HttpClient client, int bookId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/Book/{bookId}")
        {
            Content = JsonContent.Create(body),
        };
        return client.SendAsync(request, Ct);
    }

    private Task<int> AddBookAsync(string? ownerId, string title, BookStatusEnum status = BookStatusEnum.Available) =>
        factory.WithDbAsync(async db =>
        {
            var book = TestData.Book(title, status: status);
            book.OwnerId = ownerId;
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });

    private async Task<int> AddLentBookAsync(string ownerId, string borrowerName)
    {
        var bookId = await AddBookAsync(ownerId, "Lent out", BookStatusEnum.CheckedOut);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = bookId,
                BorrowerName = borrowerName,
                CheckOutDate = DateTime.UtcNow,
                ReturnDate = DateTime.UtcNow.AddDays(14),
            });
            await db.SaveChangesAsync(Ct);
        });
        return bookId;
    }

    private Task<Book> BookAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AsNoTracking().SingleAsync(b => b.BookId == bookId, Ct));

    private Task<bool> BookExistsAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AnyAsync(b => b.BookId == bookId, Ct));

    private Task<int> LoanCountOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.CountAsync(l => l.BookId == bookId, Ct));

    private Task<List<StatusHistoryItem>> HistoryOf(int bookId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.AsNoTracking().Where(h => h.BookId == bookId).ToListAsync(Ct));

    private Task<List<string>> MessagesOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId).Select(n => n.Message).ToListAsync(Ct));
}
