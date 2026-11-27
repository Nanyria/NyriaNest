using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #85 step 8: what an admin can do to help when lending between friends goes wrong (#50).
/// Each needs a reason, is recorded in the book's history (the admin and the reason) and is told
/// to both the owner and the friend:
/// - confirm a friend's return on the owner's behalf (api/Book/{id}/return, as for any owned book);
/// - cancel a request (api/borrow-requests/{id}/admin-cancel);
/// - see every request for a book, to find the one to help with (api/Book/{id}/borrow-requests).
/// </summary>
public class AdminLendingSupportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Title = "Ingrid's Hobbit";
    private const string Reason = "The friend couldn't reach the owner";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- Confirming a return for the owner ----

    [Fact]
    public async Task An_admin_confirms_a_reported_return_with_the_friends_date_records_it_and_tells_both()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var bookId = await BookOfAsync(owner);
        var reported = Today.AddDays(-3);
        await LendAsync(bookId, friend, reportedReturnedOn: reported);

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/return", new { reason = Reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await factory.WithDbAsync(db => db.CheckOutItems.AnyAsync(l => l.BookId == bookId, Ct))).Should().BeFalse();
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].UserId.Should().Be(adminUser.Id);
        history[0].BookStatus.Should().Be(BookStatusEnum.Returned);
        history[0].Timestamp.Should().Be(Midnight(reported));
        history[0].Notes.Should().Contain(friend.UserName).And.Contain("by an admin").And.Contain(Reason);

        (await MessagesOf(owner.Id)).Should().ContainSingle().Which.Should().Contain(Title).And.Contain(Reason);
        (await NotificationsOf(friend.Id)).Should().ContainSingle().Which.Type.Should().Be(NotificationType.ReturnConfirmed);
    }

    [Fact]
    public async Task Confirming_a_friends_return_without_a_reason_is_refused()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var bookId = await BookOfAsync(owner);
        await LendAsync(bookId, friend, reportedReturnedOn: Today);

        var response = await admin.PostAsJsonAsync($"api/Book/{bookId}/return", new { reason = " " }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await factory.WithDbAsync(db => db.CheckOutItems.AnyAsync(l => l.BookId == bookId, Ct))).Should().BeTrue();
        (await MessagesOf(friend.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_admins_confirmed_return_opens_the_queue_as_the_owners_would()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var waiting = await factory.CreateUserAsync();
        var bookId = await BookOfAsync(owner);
        await LendAsync(bookId, friend, reportedReturnedOn: Today);
        await factory.WithDbAsync(async db =>
        {
            db.ReservationItems.Add(new ReservationItem { BookId = bookId, UserId = waiting.Id, ReservationDate = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync(Ct);
        });

        await admin.PostAsJsonAsync($"api/Book/{bookId}/return", new { reason = Reason }, Ct);

        var request = await factory.WithDbAsync(db => db.BorrowRequests.SingleAsync(r => r.BookId == bookId, Ct));
        request.BorrowerId.Should().Be(waiting.Id);
        request.Status.Should().Be(BorrowRequestStatus.Requested);
    }

    // ---- Cancelling a request ----

    [Fact]
    public async Task An_admin_cancels_a_request_records_it_in_the_history_and_tells_both_why()
    {
        var (admin, adminUser) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var bookId = await BookOfAsync(owner);
        var requestId = await OpenRequestAsync(bookId, friend);

        var response = await admin.PutAsJsonAsync($"api/borrow-requests/{requestId}/admin-cancel", new { reason = Reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var request = await factory.WithDbAsync(db => db.BorrowRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, Ct));
        request.Status.Should().Be(BorrowRequestStatus.Cancelled);
        request.AnsweredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // The book's history, as for the admin's other actions on a book; its status doesn't change
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].UserId.Should().Be(adminUser.Id);
        history[0].BookStatus.Should().Be(BookStatusEnum.Available);
        history[0].Notes.Should().Contain(friend.UserName).And.Contain("cancelled by an admin").And.Contain(Reason);

        var friendTold = await NotificationsOf(friend.Id);
        friendTold.Should().ContainSingle();
        friendTold[0].Type.Should().Be(NotificationType.LoanRequestAnswered);
        friendTold[0].Message.Should().Contain(Title).And.Contain(Reason);
        var ownerTold = await NotificationsOf(owner.Id);
        ownerTold.Should().ContainSingle();
        ownerTold[0].Type.Should().Be(NotificationType.BookLoanChangedByAdmin);
        ownerTold[0].Message.Should().Contain(friend.UserName).And.Contain(Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Cancelling_without_a_reason_is_refused_and_changes_nothing(string? reason)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var requestId = await OpenRequestAsync(await BookOfAsync(owner), friend);

        var response = await admin.PutAsJsonAsync($"api/borrow-requests/{requestId}/admin-cancel", new { reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusOf(requestId)).Should().Be(BorrowRequestStatus.Requested);
        (await MessagesOf(friend.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_request_that_is_already_answered_is_refused()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var requestId = await OpenRequestAsync(await BookOfAsync(owner), friend, BorrowRequestStatus.Declined);

        var response = await admin.PutAsJsonAsync($"api/borrow-requests/{requestId}/admin-cancel", new { reason = Reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StatusOf(requestId)).Should().Be(BorrowRequestStatus.Declined);
    }

    [Fact]
    public async Task Cancelling_a_request_that_doesnt_exist_is_404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.PutAsJsonAsync("api/borrow-requests/999999/admin-cancel", new { reason = Reason }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_admins_can_cancel_for_support_the_owner_and_the_friend_have_their_own_routes()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var (friendClient, friend) = await factory.LoginAsAsync();
        var requestId = await OpenRequestAsync(await BookOfAsync(owner), friend);

        var asOwner = await ownerClient.PutAsJsonAsync($"api/borrow-requests/{requestId}/admin-cancel", new { reason = Reason }, Ct);
        var asFriend = await friendClient.PutAsJsonAsync($"api/borrow-requests/{requestId}/admin-cancel", new { reason = Reason }, Ct);

        asOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        asFriend.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusOf(requestId)).Should().Be(BorrowRequestStatus.Requested);
    }

    // ---- Seeing a book's requests ----

    [Fact]
    public async Task An_admin_sees_every_request_for_a_book_newest_first_with_its_status()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (owner, friend) = await OwnerAndFriendAsync();
        var other = await factory.CreateUserAsync();
        var bookId = await BookOfAsync(owner);
        var older = await OpenRequestAsync(bookId, friend, BorrowRequestStatus.Declined, daysAgo: 5);
        var newer = await OpenRequestAsync(bookId, other, daysAgo: 1);
        await OpenRequestAsync(await BookOfAsync(owner), friend); // another book's

        var response = await admin.GetAsync($"api/Book/{bookId}/borrow-requests", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var items = (await ResultOf(response)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetInt32()).Should().Equal(newer, older);
        items[0].GetProperty("borrowerUserName").GetString().Should().Be(other.UserName);
        items[0].GetProperty("status").GetInt32().Should().Be((int)BorrowRequestStatus.Requested);
        items[1].GetProperty("status").GetInt32().Should().Be((int)BorrowRequestStatus.Declined);
    }

    [Fact]
    public async Task A_books_requests_are_for_admins_only_and_an_unknown_book_is_404()
    {
        var (user, owner) = await factory.LoginAsAsync();
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await BookOfAsync(owner);

        (await user.GetAsync($"api/Book/{bookId}/borrow-requests", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("api/Book/999999/borrow-requests", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Helpers ----

    private async Task<(User Owner, User Friend)> OwnerAndFriendAsync() =>
        (await factory.CreateUserAsync(), await factory.CreateUserAsync());

    private Task<int> BookOfAsync(User owner) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(Title);
        book.OwnerId = owner.Id;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task LendAsync(int bookId, User friend, DateOnly? reportedReturnedOn) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new CheckedOutItem
        {
            BookId = bookId,
            UserId = friend.Id,
            CheckOutDate = DateTime.UtcNow.AddDays(-10),
            ReturnDate = DateTime.UtcNow.AddDays(20),
            ReportedReturnedDate = reportedReturnedOn is { } d ? Midnight(d) : null,
        });
        (await db.Books.SingleAsync(b => b.BookId == bookId, Ct)).BookStatus = BookStatusEnum.CheckedOut;
        await db.SaveChangesAsync(Ct);
    });

    private Task<int> OpenRequestAsync(int bookId, User friend,
        BorrowRequestStatus status = BorrowRequestStatus.Requested, int daysAgo = 1) => factory.WithDbAsync(async db =>
    {
        var request = new BorrowRequest
        {
            BookId = bookId,
            BorrowerId = friend.Id,
            Status = status,
            RequestedAt = DateTime.UtcNow.AddDays(-daysAgo),
            AvailableAt = DateTime.UtcNow.AddDays(-daysAgo),
            AnsweredAt = status == BorrowRequestStatus.Requested ? null : DateTime.UtcNow,
        };
        db.BorrowRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return request.Id;
    });

    private Task<BorrowRequestStatus> StatusOf(int requestId) => factory.WithDbAsync(db =>
        db.BorrowRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync(Ct));

    private Task<List<StatusHistoryItem>> HistoryOf(int bookId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.AsNoTracking().Where(h => h.BookId == bookId).ToListAsync(Ct));

    private Task<List<NotificationItem>> NotificationsOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(Ct));

    private async Task<List<string>> MessagesOf(string userId) =>
        (await NotificationsOf(userId)).Select(n => n.Message).ToList();

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("result").Clone();
    }
}
