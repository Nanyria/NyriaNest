using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// Pins down what a book's status, availability date and status history look like
/// after each action that changes them (borrow, return, reserve, cancel, and an admin
/// deleting a user), so #116 can move UpdateBookStatusAsync and
/// RecalculateAvailabilityDates into AvailabilityService without changing them.
/// </summary>
public class AvailabilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Borrowing ----

    [Fact]
    public async Task Borrow_AnAvailableBook_ChecksItOut_ForOneLoanPeriod()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PutAsync($"api/UserActions/borrow/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
        var dueDate = await DueDateOf(bookId);
        dueDate.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
        (await AvailabilityDateOf(bookId)).Should().BeCloseTo(dueDate, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public async Task Borrow_ABookMarkedLostOrRemoved_Returns409_AndWritesNoHistory(BookStatusEnum adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(adminStatus, adminStatus);

        var response = await client.PutAsync($"api/UserActions/borrow/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("This book isn't available.");
        (await StatusOf(bookId)).Should().Be(adminStatus);
        (await HistoryOf(bookId, me.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("borrow")]
    [InlineData("return")]
    public async Task BorrowOrReturn_ABookThatDoesntExist_Returns404(string action)
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.PutAsync($"api/UserActions/{action}/999999", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("User or Book not found.");
    }

    // ---- Returning ----

    [Fact]
    public async Task Return_WithNoQueue_MakesTheBookAvailableToday()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, me.Id, dueInDays: 20);

        var response = await client.PutAsync($"api/UserActions/return/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
        (await AvailabilityDateOf(bookId)).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ---- The status history each action writes ----

    [Fact]
    public async Task Borrow_WritesACheckedOutEntry_InTheBorrowersName()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        await client.PutAsync($"api/UserActions/borrow/{bookId}", null, Ct);

        (await HistoryOf(bookId, me.Id)).Should().Equal((BookStatusEnum.CheckedOut, $"Checked out by {me.UserName}"));
    }

    [Fact]
    public async Task Return_WritesAReturnedEntry_InTheBorrowersName()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, me.Id);

        await client.PutAsync($"api/UserActions/return/{bookId}", null, Ct);

        (await HistoryOf(bookId, me.Id)).Should().Equal((BookStatusEnum.Returned, $"Returned by {me.UserName}"));
    }

    [Fact]
    public async Task Reserve_WritesAReservedEntry_InTheReadersName()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        await client.PutAsync($"api/UserActions/reserve/{bookId}", null, Ct);

        (await HistoryOf(bookId, me.Id)).Should().Equal((BookStatusEnum.Reserved, $"Book reserved by {me.UserName}"));
    }

    // The entry for a cancelled reservation says Returned: that's the status the
    // service passes in to mean "recalculate", and it's what lands in the history
    [Fact]
    public async Task CancelReservation_WritesAReturnedEntry_SayingTheReservationWasCancelled()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, me.Id);

        await client.PutAsync($"api/UserActions/unreserve/{bookId}", null, Ct);

        (await HistoryOf(bookId, me.Id)).Should().Equal((BookStatusEnum.Returned, $"Reservation cancelled by {me.UserName}"));
    }

    // ---- An admin deleting a user ----

    [Fact]
    public async Task DeletingTheBorrower_MakesTheBookAvailable()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var borrower = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, borrower.Id);

        var response = await admin.DeleteAsync($"api/User/{borrower.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
    }

    [Fact]
    public async Task DeletingTheBorrower_WithAQueue_MakesTheBookReserved()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var borrower = await factory.CreateUserAsync();
        var next = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, borrower.Id);
        await AddReservationAsync(bookId, next.Id);

        var response = await admin.DeleteAsync($"api/User/{borrower.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Reserved);
    }

    [Fact]
    public async Task DeletingTheOnlyReader_InTheQueue_MakesTheBookAvailable_AndWritesNoHistory()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, reader.Id);

        var response = await admin.DeleteAsync($"api/User/{reader.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
        (await HistoryOf(bookId, adminUser.Id)).Should().BeEmpty("the status update after a deletion has no user, so it writes no entry");
    }

    [Fact]
    public async Task DeletingTheFirstReader_InTheQueue_MovesTheNextReaderUp()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var first = await factory.CreateUserAsync();
        var next = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, first.Id, daysAgo: 2, availableInDays: 0);
        await AddReservationAsync(bookId, next.Id, daysAgo: 1, availableInDays: 30);

        var response = await admin.DeleteAsync($"api/User/{first.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Reserved);
        (await ReservationDateOf(bookId, next.Id)).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task DeletingTheBorrower_TheNextReaderCanHaveItToday()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var borrower = await factory.CreateUserAsync();
        var next = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, borrower.Id, dueInDays: 20);
        await AddReservationAsync(bookId, next.Id, availableInDays: 20);

        var response = await admin.DeleteAsync($"api/User/{borrower.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationDateOf(bookId, next.Id)).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(BookStatusEnum status = BookStatusEnum.Available, BookStatusEnum? adminStatus = null) =>
        factory.WithDbAsync(async db =>
        {
            var book = TestData.Book(status: status);
            book.AdminStatus = adminStatus;
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });

    private Task AddLoanAsync(int bookId, string userId, int dueInDays = 14) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new() { BookId = bookId, UserId = userId, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(dueInDays) });
        await db.SaveChangesAsync(Ct);
    });

    private Task AddReservationAsync(int bookId, string userId, int daysAgo = 1, int? availableInDays = null) => factory.WithDbAsync(async db =>
    {
        db.ReservationItems.Add(new()
        {
            BookId = bookId,
            UserId = userId,
            ReservationDate = DateTime.UtcNow.AddDays(-daysAgo),
            AvailabilityDate = availableInDays is int d ? DateTime.UtcNow.AddDays(d) : null,
        });
        await db.SaveChangesAsync(Ct);
    });

    private Task<DateTime?> ReservationDateOf(int bookId, string userId) => factory.WithDbAsync(db =>
        db.ReservationItems.Where(r => r.BookId == bookId && r.UserId == userId).Select(r => r.AvailabilityDate).SingleAsync(Ct));

    private Task<BookStatusEnum> StatusOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct));

    private Task<DateTime> AvailabilityDateOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.AvailabilityDate).SingleAsync(Ct));

    private Task<DateTime> DueDateOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.Where(c => c.BookId == bookId).Select(c => c.ReturnDate).SingleAsync(Ct));

    private Task<List<(BookStatusEnum Status, string? Notes)>> HistoryOf(int bookId, string userId) => factory.WithDbAsync(async db =>
        (await db.StatusHistoryItems
            .Where(h => h.BookId == bookId && h.UserId == userId)
            .Select(h => new { h.BookStatus, h.Notes })
            .ToListAsync(Ct))
        .Select(h => (h.BookStatus, h.Notes))
        .ToList());
}
