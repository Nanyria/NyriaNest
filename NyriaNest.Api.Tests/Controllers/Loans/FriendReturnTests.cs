using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #85 step 5: returning a friend's book. The friend reports it returned, with the date they gave
/// it back (api/UserActions/report-returned/{bookId}, PUT); that isn't a return yet, the book is still lent.
/// The owner confirms (api/my/books/{id}/return, as for any of their loans), with or without the
/// friend's report, and may change the date. Only then is it Returned: the confirmed date goes in
/// the history, and the queue opens from the moment the owner confirms.
/// </summary>
public class FriendReturnTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string ReportUrl(int bookId) => $"api/UserActions/report-returned/{bookId}";
    private static string ConfirmUrl(int bookId) => $"api/my/books/{bookId}/return";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    // ---- The friend reports it returned ----

    [Fact]
    public async Task The_friend_reports_it_returned_the_book_stays_lent_and_the_owner_is_told()
    {
        var (owner, _, friend, friendClient, bookId) = await LentToAFriendAsync();
        var yesterday = Today.AddDays(-1);

        var response = await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = yesterday }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var loan = await LoanOf(bookId);
        loan.Should().NotBeNull("a reported return isn't a return until the owner confirms");
        loan!.UserId.Should().Be(friend.Id);
        loan.ReportedReturnedDate.Should().Be(Midnight(yesterday));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
        (await HistoryOf(bookId)).Should().BeEmpty("nothing is recorded until it's confirmed");

        var told = await NotificationsOf(owner.Id, bookId);
        told.Should().ContainSingle().Which.Type.Should().Be(NotificationType.ReturnMarked);
        told[0].Message.Should().Contain(friend.UserName);
    }

    [Fact]
    public async Task Without_a_date_it_was_returned_today()
    {
        var (_, _, _, friendClient, bookId) = await LentToAFriendAsync();

        var response = await friendClient.PutAsync(ReportUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanOf(bookId))!.ReportedReturnedDate.Should().Be(Midnight(Today));
    }

    [Fact]
    public async Task Reporting_again_changes_the_date()
    {
        var (_, _, _, friendClient, bookId) = await LentToAFriendAsync();
        await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = Today }, Ct);

        var again = await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = Today.AddDays(-2) }, Ct);

        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync(Ct));
        (await LoanOf(bookId))!.ReportedReturnedDate.Should().Be(Midnight(Today.AddDays(-2)));
    }

    [Fact]
    public async Task Only_the_friend_who_has_it_can_report_it_for_anyone_else_it_looks_missing()
    {
        var (owner, ownerClient, _, _, bookId) = await LentToAFriendAsync();
        var (otherFriendClient, _) = await FriendOfAsync(owner);
        var (strangerClient, _) = await factory.LoginAsAsync();

        foreach (var client in new[] { ownerClient, otherFriendClient, strangerClient })
        {
            var response = await client.PutAsync(ReportUrl(bookId), null, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await LoanOf(bookId))!.ReportedReturnedDate.Should().BeNull();
    }

    [Fact]
    public async Task A_book_lent_to_a_name_cant_be_reported_by_a_member()
    {
        var (owner, _) = await OwnerAsync();
        var (friendClient, _) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner);
        await LendAsync(bookId, borrowerName: "Grandma");

        var response = await friendClient.PutAsync(ReportUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_date_in_the_future_or_before_the_loan_is_refused()
    {
        var (_, _, _, friendClient, bookId) = await LentToAFriendAsync(lentDaysAgo: 10);

        var future = await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = Today.AddDays(1) }, Ct);
        var beforeLoan = await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = Today.AddDays(-11) }, Ct);

        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        beforeLoan.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanOf(bookId))!.ReportedReturnedDate.Should().BeNull();
    }

    [Fact]
    public async Task Reporting_without_a_token_is_401()
    {
        var response = await factory.CreateClient().PutAsync(ReportUrl(1), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- The owner confirms ----

    [Fact]
    public async Task Confirming_a_reported_return_uses_the_friends_date_and_tells_the_friend()
    {
        var (owner, ownerClient, friend, friendClient, bookId) = await LentToAFriendAsync();
        var reported = Today.AddDays(-3);
        await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = reported }, Ct);

        var response = await ownerClient.PostAsync(ConfirmUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanOf(bookId)).Should().BeNull();
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);

        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].BookStatus.Should().Be(BookStatusEnum.Returned);
        history[0].UserId.Should().Be(owner.Id);
        history[0].Timestamp.Should().Be(Midnight(reported), "the confirmed date is the one recorded");
        history[0].Notes.Should().Contain(friend.UserName);

        (await NotificationsOf(friend.Id, bookId)).Should().ContainSingle()
            .Which.Type.Should().Be(NotificationType.ReturnConfirmed);
    }

    [Fact]
    public async Task The_owner_can_change_the_date_when_confirming()
    {
        var (_, ownerClient, _, friendClient, bookId) = await LentToAFriendAsync();
        await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = Today.AddDays(-3) }, Ct);

        var response = await ownerClient.PostAsJsonAsync(ConfirmUrl(bookId), new { returnedOn = Today.AddDays(-1) }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await HistoryOf(bookId)).Single().Timestamp.Should().Be(Midnight(Today.AddDays(-1)));
    }

    [Fact]
    public async Task The_owner_can_mark_it_returned_without_the_friends_report_today_by_default()
    {
        var (_, ownerClient, friend, _, bookId) = await LentToAFriendAsync();

        var response = await ownerClient.PostAsync(ConfirmUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanOf(bookId)).Should().BeNull();
        (await HistoryOf(bookId)).Single().Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await NotificationsOf(friend.Id, bookId)).Should().ContainSingle(n => n.Type == NotificationType.ReturnConfirmed);
    }

    [Fact]
    public async Task Confirming_opens_the_queue_from_now_not_from_the_reported_date()
    {
        // The reminders and expiry start once the owner knows it's back
        var (owner, ownerClient, _, friendClient, bookId) = await LentToAFriendAsync();
        var (waitingClient, waiting) = await FriendOfAsync(owner);
        (await waitingClient.PutAsync($"api/UserActions/reserve/{bookId}", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reservedAt = await factory.WithDbAsync(db =>
            db.ReservationItems.Where(r => r.BookId == bookId).Select(r => r.ReservationDate).SingleAsync(Ct));
        await friendClient.PutAsJsonAsync(ReportUrl(bookId), new { returnedOn = Today.AddDays(-5) }, Ct);

        await ownerClient.PostAsync(ConfirmUrl(bookId), null, Ct);

        var request = await factory.WithDbAsync(db => db.BorrowRequests.SingleAsync(r => r.BookId == bookId, Ct));
        request.BorrowerId.Should().Be(waiting.Id);
        request.Status.Should().Be(BorrowRequestStatus.Requested);
        request.RequestedAt.Should().Be(reservedAt);
        request.AvailableAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ---- Helpers ----

    private async Task<(User, HttpClient)> OwnerAsync()
    {
        var (client, user) = await factory.LoginAsAsync();
        return (user, client);
    }

    private async Task<(HttpClient, User)> FriendOfAsync(User owner)
    {
        var (client, friend) = await factory.LoginAsAsync();
        await factory.WithDbAsync(async db =>
        {
            db.Friendships.Add(new Friendship
            {
                RequesterId = owner.Id,
                AddresseeId = friend.Id,
                Status = FriendshipStatus.Accepted,
                AcceptedAt = DateTime.UtcNow,
                RequesterSharesPhysicalLibrary = true,
            });
            await db.SaveChangesAsync(Ct);
        });
        return (client, friend);
    }

    /// <summary>The owner's paperback, lent to a friend (as an accepted request would) some days ago.</summary>
    private async Task<(User Owner, HttpClient OwnerClient, User Friend, HttpClient FriendClient, int BookId)> LentToAFriendAsync(int lentDaysAgo = 7)
    {
        var (owner, ownerClient) = await OwnerAsync();
        var (friendClient, friend) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner);
        await LendAsync(bookId, userId: friend.Id, lentDaysAgo: lentDaysAgo);
        return (owner, ownerClient, friend, friendClient, bookId);
    }

    private Task<int> BookOfAsync(User owner) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book("A friend's paperback");
        book.OwnerId = owner.Id;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task LendAsync(int bookId, string? userId = null, string? borrowerName = null, int lentDaysAgo = 7) =>
        factory.WithDbAsync(async db =>
        {
            var lentAt = DateTime.UtcNow.AddDays(-lentDaysAgo);
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = bookId,
                UserId = userId,
                BorrowerName = borrowerName,
                CheckOutDate = lentAt,
                ReturnDate = lentAt.AddMonths(1),
            });
            var book = await db.Books.SingleAsync(b => b.BookId == bookId, Ct);
            book.BookStatus = BookStatusEnum.CheckedOut;
            book.AvailabilityDate = lentAt.AddMonths(1);
            await db.SaveChangesAsync(Ct);
        });

    private Task<CheckedOutItem?> LoanOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.AsNoTracking().SingleOrDefaultAsync(l => l.BookId == bookId, Ct));

    private Task<BookStatusEnum> StatusOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct));

    private Task<List<StatusHistoryItem>> HistoryOf(int bookId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.AsNoTracking().Where(h => h.BookId == bookId).ToListAsync(Ct));

    private Task<List<NotificationItem>> NotificationsOf(string userId, int bookId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId && n.BookId == bookId).ToListAsync(Ct));
}
