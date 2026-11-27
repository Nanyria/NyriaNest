using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// Pins down what a book's status, availability date and status history look like
/// after each action that changes them (reserve, cancel, and an admin deleting a user), so
/// #116 could move UpdateBookStatusAsync and RecalculateAvailabilityDates into AvailabilityService
/// without changing them. The old self-service borrow and return were removed in #85. Reserving
/// needs a friend's lent book since #85; the other tests set up their loans and queues directly.
/// </summary>
public class AvailabilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The status history each action writes ----

    [Fact]
    public async Task Reserve_WritesAReservedEntry_InTheReadersName()
    {
        // #85: the queue is for a friend's book that's lent out
        var (client, me) = await factory.LoginAsAsync();
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await factory.WithDbAsync(async db =>
        {
            (await db.Books.SingleAsync(b => b.BookId == bookId, Ct)).OwnerId = owner.Id;
            db.Friendships.Add(new Friendship
            {
                RequesterId = owner.Id,
                AddresseeId = me.Id,
                Status = FriendshipStatus.Accepted,
                AcceptedAt = DateTime.UtcNow,
                RequesterSharesPhysicalLibrary = true,
            });
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = bookId,
                BorrowerName = "Grandma",
                CheckOutDate = DateTime.UtcNow,
                ReturnDate = DateTime.UtcNow.AddDays(14),
            });
            await db.SaveChangesAsync(Ct);
        });

        var response = await client.PutAsync($"api/UserActions/reserve/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
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
