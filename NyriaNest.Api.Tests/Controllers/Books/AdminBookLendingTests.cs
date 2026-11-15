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
/// #83 step 4a: admins lend and return someone's book through api/Book/{id}/lend and /return,
/// to put things right when the page got it wrong. Same rule as admin edit/delete (by actor,
/// not role): when the admin isn't the owner, a reason is required, the history row shows the
/// admin and the reason, and the owner is told (so they can see the problem was fixed).
/// An admin's own book is just their own: no reason, nobody told. Shared-library books
/// aren't lent by name.
/// </summary>
public class AdminBookLendingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Lend ----

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AdminLend_SomeoneElsesBook_WithoutAReason_Returns400_LendsNothing_AndTellsNoOne(string? reason)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "Their book");

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/lend", new { borrowerName = "Ruth", reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanCountOf(bookId)).Should().Be(0);
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminLend_SomeoneElsesBook_WithAReason_Lends_RecordsTheAdmin_AndTellsTheOwnerWhy()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bystander = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "The Hobbit");

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/lend",
            new { borrowerName = "Ruth", reason = "The page showed it as available" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanOf(bookId)).BorrowerName.Should().Be("Ruth");
        (await BookAsync(bookId)).BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        history[0].UserId.Should().Be(adminUser.Id);
        history[0].Notes.Should().Contain("Ruth").And.Contain("The page showed it as available");
        var messages = await MessagesOf(owner.Id);
        messages.Should().ContainSingle();
        messages[0].Should().Contain("The Hobbit").And.Contain("Ruth").And.Contain("The page showed it as available");
        (await MessagesOf(bystander.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminLend_TheirOwnBook_NeedsNoReason_AndTellsNoOne()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(adminUser.Id, "Admin's own book");

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/lend", new { borrowerName = "Ruth" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanCountOf(bookId)).Should().Be(1);
        (await MessagesOf(adminUser.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminLend_ASharedLibraryBook_Returns400_AndLendsNothing()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(ownerId: null, "Shared book");

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/lend",
            new { borrowerName = "Ruth", reason = "Testing" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanCountOf(bookId)).Should().Be(0);
    }

    [Fact]
    public async Task AdminLend_AnUnknownBook_Returns404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync("api/Book/999999/lend",
            new { borrowerName = "Ruth", reason = "Testing" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Return ----

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AdminReturn_SomeoneElsesBook_WithoutAReason_Returns400_KeepsTheLoan_AndTellsNoOne(string? reason)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddLentBookAsync(owner.Id, "Ruth");

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/return", new { reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanCountOf(bookId)).Should().Be(1);
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminReturn_SomeoneElsesBook_WithAReason_Returns_RecordsTheAdmin_AndTellsTheOwnerWhy()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bystander = await factory.CreateUserAsync();
        var bookId = await AddLentBookAsync(owner.Id, "Ruth", title: "Rebecca");

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/return",
            new { reason = "Return button didn't work" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanCountOf(bookId)).Should().Be(0);
        (await BookAsync(bookId)).BookStatus.Should().Be(BookStatusEnum.Available);
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].BookStatus.Should().Be(BookStatusEnum.Returned);
        history[0].UserId.Should().Be(adminUser.Id);
        history[0].Notes.Should().Contain("Ruth").And.Contain("Return button didn't work");
        var messages = await MessagesOf(owner.Id);
        messages.Should().ContainSingle();
        messages[0].Should().Contain("Rebecca").And.Contain("Return button didn't work");
        (await MessagesOf(bystander.Id)).Should().BeEmpty();
    }

    // ---- Not an admin ----

    [Theory]
    [InlineData("lend")]
    [InlineData("return")]
    public async Task LendOrReturn_OnTheAdminRoute_AsAUser_Returns403(string action)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id, "My book");

        var response = await client.PostAsJsonAsync($"api/Book/{bookId}/{action}", new { borrowerName = "Ruth" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(string? ownerId, string title, BookStatusEnum status = BookStatusEnum.Available) =>
        factory.WithDbAsync(async db =>
        {
            var book = TestData.Book(title, status: status);
            book.OwnerId = ownerId;
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });

    private async Task<int> AddLentBookAsync(string ownerId, string borrowerName, string title = "Lent out")
    {
        var bookId = await AddBookAsync(ownerId, title, BookStatusEnum.CheckedOut);
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

    private Task<CheckedOutItem> LoanOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.AsNoTracking().SingleAsync(l => l.BookId == bookId, Ct));

    private Task<int> LoanCountOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.CountAsync(l => l.BookId == bookId, Ct));

    private Task<List<StatusHistoryItem>> HistoryOf(int bookId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.AsNoTracking().Where(h => h.BookId == bookId).ToListAsync(Ct));

    private Task<List<string>> MessagesOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId).Select(n => n.Message).ToListAsync(Ct));
}
