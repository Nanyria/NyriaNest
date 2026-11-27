using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #39 part A: the rules for reservations, through PUT api/UserActions/unreserve/{bookId}.
/// The queue is first come, first served: the oldest reservation is next in line. The borrow
/// and return rules went with the old self-service flow (removed in #85); friends' loans
/// are in BorrowRequestServiceTests and FriendReturnTests.
/// </summary>
public class LoanRulesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Borrowing ----

    // ---- Returning ----

    // ---- Cancelling a reservation ----

    [Fact]
    public async Task CancelReservation_WithOthersStillQueued_KeepsTheBookReserved()
    {
        var (reader, me) = await factory.LoginAsAsync(Roles.User);
        var other = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, me.Id, daysAgo: 2);
        await AddReservationAsync(bookId, other.Id, daysAgo: 1);

        var response = await reader.PutAsync($"api/UserActions/unreserve/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationOf(bookId, me.Id)).Should().BeNull();
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Reserved);
    }

    [Fact]
    public async Task CancelReservation_TheLastOne_MakesTheBookAvailable()
    {
        var (reader, me) = await factory.LoginAsAsync(Roles.User);
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, me.Id, daysAgo: 1);

        var response = await reader.PutAsync($"api/UserActions/unreserve/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
    }

    [Fact]
    public async Task CancelReservation_WhileSomeoneHasTheBook_KeepsItCheckedOut()
    {
        // Not in #39's list: cancelling sends "Returned" to GetBookStatus, which
        // only looks at the queue, so the book might show as free while on loan
        var holder = await factory.CreateUserAsync();
        var (reader, me) = await factory.LoginAsAsync(Roles.User);
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, holder.Id);
        await AddReservationAsync(bookId, me.Id, daysAgo: 1);

        var response = await reader.PutAsync($"api/UserActions/unreserve/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BorrowerOf(bookId)).Should().Be(holder.Id);
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(BookStatusEnum status) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(status: status);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddLoanAsync(int bookId, string userId, int dueInDays = 14) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new() { BookId = bookId, UserId = userId, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(dueInDays) });
        await db.SaveChangesAsync(Ct);
    });

    private Task AddReservationAsync(int bookId, string userId, int daysAgo, int? availableInDays = null) => factory.WithDbAsync(async db =>
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

    private Task<string?> BorrowerOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.Where(c => c.BookId == bookId).Select(c => c.UserId).SingleOrDefaultAsync(Ct));

    private Task<DateTime> DueDateOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.Where(c => c.BookId == bookId).Select(c => c.ReturnDate).SingleAsync(Ct));

    private Task<ReservationItem?> ReservationOf(int bookId, string userId) => factory.WithDbAsync(db =>
        db.ReservationItems.AsNoTracking().SingleOrDefaultAsync(r => r.BookId == bookId && r.UserId == userId, Ct));

    private Task<BookStatusEnum> StatusOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct));

    private Task<int> HistoryCountOf(int bookId, string userId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.CountAsync(h => h.BookId == bookId && h.UserId == userId, Ct));
}
