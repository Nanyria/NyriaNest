using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #85 step 4: the queue for a friend's book. A friend who is shared with reserves through the
/// same endpoints as before (api/UserActions/reserve and unreserve), but only while the book is
/// lent out; a free book is asked for instead. When the book comes back, everyone in the queue
/// gets an open request that keeps the date they started waiting, and the owner chooses, as for
/// any requests (BorrowRequestServiceTests). Nothing is handed on after a decline: all are open.
/// </summary>
public class FriendQueueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string ReserveUrl(int bookId) => $"api/UserActions/reserve/{bookId}";
    private static string UnreserveUrl(int bookId) => $"api/UserActions/unreserve/{bookId}";
    private static string ReturnUrl(int bookId) => $"api/my/books/{bookId}/return";
    private const string AskUrl = "api/borrow-requests";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Reserving ----

    [Fact]
    public async Task A_friend_can_reserve_a_lent_out_book_and_is_expected_to_get_it_when_the_loan_is_due()
    {
        var (owner, ownerClient) = await OwnerAsync();
        var (friend, friendClient) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner, lentToName: true);

        var response = await friendClient.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var reservation = await factory.WithDbAsync(db => db.ReservationItems.SingleAsync(r => r.BookId == bookId, Ct));
        reservation.UserId.Should().Be(friend.Id);
        var due = await factory.WithDbAsync(db => db.CheckOutItems.Where(l => l.BookId == bookId).Select(l => l.ReturnDate).SingleAsync(Ct));
        reservation.AvailabilityDate.Should().Be(due);
    }

    [Fact]
    public async Task A_free_book_cant_be_reserved_it_is_asked_for_instead()
    {
        var (owner, _) = await OwnerAsync();
        var (_, friendClient) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner);

        var response = await friendClient.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("ask");
        (await ReservationsOf(bookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Someone_the_owner_doesnt_share_with_cant_reserve_it_looks_missing()
    {
        var (owner, _) = await OwnerAsync();
        var (_, notSharedClient) = await FriendOfAsync(owner, ownerShares: false);
        var (strangerClient, _) = await factory.LoginAsAsync();
        var bookId = await BookOfAsync(owner, lentToName: true);

        (await notSharedClient.PutAsync(ReserveUrl(bookId), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await strangerClient.PutAsync(ReserveUrl(bookId), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReservationsOf(bookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task You_cant_reserve_a_book_you_have_already_asked_for()
    {
        // Lent to a name after the friend asked: asking and reserving are one place in line, not two
        var (owner, _) = await OwnerAsync();
        var (friend, friendClient) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner);
        (await friendClient.PostAsJsonAsync(AskUrl, new { bookId }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await LendToNameAsync(bookId);

        var response = await friendClient.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReservationsOf(bookId)).Should().BeEmpty();
    }

    // ---- The book comes back: everyone in the queue is asked for, and the owner chooses ----

    [Fact]
    public async Task When_the_book_comes_back_everyone_in_the_queue_gets_a_request_waiting_since_they_reserved()
    {
        var (owner, ownerClient) = await OwnerAsync();
        var (first, firstClient) = await FriendOfAsync(owner);
        var (second, secondClient) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner, lentToName: true);
        (await firstClient.PutAsync(ReserveUrl(bookId), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondClient.PutAsync(ReserveUrl(bookId), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reservedAt = (await ReservationsOf(bookId)).ToDictionary(r => r.UserId, r => r.ReservationDate);

        var returned = await ownerClient.PostAsync(ReturnUrl(bookId), null, Ct);

        returned.StatusCode.Should().Be(HttpStatusCode.OK, await returned.Content.ReadAsStringAsync(Ct));
        var requests = await factory.WithDbAsync(db => db.BorrowRequests.Where(r => r.BookId == bookId).ToListAsync(Ct));
        requests.Select(r => r.BorrowerId).Should().BeEquivalentTo([first.Id, second.Id]);
        requests.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(BorrowRequestStatus.Requested);
            r.RequestedAt.Should().Be(reservedAt[r.BorrowerId], "the owner sees how long each has waited");
            r.AvailableAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        });

        // The reservations became the requests; the book is free for the owner to lend
        (await ReservationsOf(bookId)).Should().BeEmpty();
        (await factory.WithDbAsync(db => db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct)))
            .Should().Be(BookStatusEnum.Available);

        // One message to the owner naming everyone waiting, one to each friend
        var ownerTold = await NotificationsOf(owner.Id, bookId);
        ownerTold.Should().ContainSingle(n => n.Type == NotificationType.LoanRequested)
            .Which.Message.Should().Contain(first.UserName).And.Contain(second.UserName);
        (await NotificationsOf(first.Id, bookId)).Should().ContainSingle(n => n.Type == NotificationType.ReservationAvailable);
        (await NotificationsOf(second.Id, bookId)).Should().ContainSingle(n => n.Type == NotificationType.ReservationAvailable);
    }

    [Fact]
    public async Task After_it_came_back_others_ask_alongside_the_queue_and_cant_reserve()
    {
        var (owner, ownerClient) = await OwnerAsync();
        var (_, firstClient) = await FriendOfAsync(owner);
        var (_, lateClient) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner, lentToName: true);
        await firstClient.PutAsync(ReserveUrl(bookId), null, Ct);
        await ownerClient.PostAsync(ReturnUrl(bookId), null, Ct);

        var reserved = await lateClient.PutAsync(ReserveUrl(bookId), null, Ct);
        var asked = await lateClient.PostAsJsonAsync(AskUrl, new { bookId }, Ct);

        reserved.StatusCode.Should().Be(HttpStatusCode.Conflict);
        asked.StatusCode.Should().Be(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync(Ct));
        (await factory.WithDbAsync(db => db.BorrowRequests.CountAsync(r => r.BookId == bookId && r.Status == BorrowRequestStatus.Requested, Ct)))
            .Should().Be(2);
    }

    [Fact]
    public async Task When_the_book_comes_back_with_no_queue_nobody_is_asked_for()
    {
        var (owner, ownerClient) = await OwnerAsync();
        var bookId = await BookOfAsync(owner, lentToName: true);

        await ownerClient.PostAsync(ReturnUrl(bookId), null, Ct);

        (await factory.WithDbAsync(db => db.BorrowRequests.AnyAsync(r => r.BookId == bookId, Ct))).Should().BeFalse();
        (await factory.WithDbAsync(db => db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct)))
            .Should().Be(BookStatusEnum.Available);
    }

    // ---- Leaving the queue ----

    [Fact]
    public async Task Leaving_the_queue_while_the_book_is_lent_just_leaves_it()
    {
        var (owner, _) = await OwnerAsync();
        var (_, friendClient) = await FriendOfAsync(owner);
        var bookId = await BookOfAsync(owner, lentToName: true);
        await friendClient.PutAsync(ReserveUrl(bookId), null, Ct);

        var left = await friendClient.PutAsync(UnreserveUrl(bookId), null, Ct);

        left.StatusCode.Should().Be(HttpStatusCode.OK, await left.Content.ReadAsStringAsync(Ct));
        (await ReservationsOf(bookId)).Should().BeEmpty();
        (await factory.WithDbAsync(db => db.BorrowRequests.AnyAsync(r => r.BookId == bookId, Ct))).Should().BeFalse();
    }

    // ---- Helpers ----

    private async Task<(User, HttpClient)> OwnerAsync()
    {
        var (client, user) = await factory.LoginAsAsync();
        return (user, client);
    }

    /// <summary>A logged-in friend of <paramref name="owner"/>, who shares their physical library with them unless told not to.</summary>
    private async Task<(User, HttpClient)> FriendOfAsync(User owner, bool ownerShares = true)
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
                RequesterSharesPhysicalLibrary = ownerShares,
            });
            await db.SaveChangesAsync(Ct);
        });
        return (friend, client);
    }

    private Task<int> BookOfAsync(User owner, bool lentToName = false) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book("A friend's paperback", status: lentToName ? BookStatusEnum.CheckedOut : BookStatusEnum.Available);
        book.OwnerId = owner.Id;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        if (lentToName)
            await LendToNameAsync(db, book.BookId);
        return book.BookId;
    });

    private Task LendToNameAsync(int bookId) => factory.WithDbAsync(db => LendToNameAsync(db, bookId));

    private static async Task LendToNameAsync(NyriaNest.Infrastructure.Data.AppDbContext db, int bookId)
    {
        var lentAt = DateTime.UtcNow.AddDays(-3);
        db.CheckOutItems.Add(new CheckedOutItem
        {
            BookId = bookId,
            BorrowerName = "Grandma",
            CheckOutDate = lentAt,
            ReturnDate = lentAt.AddDays(14),
        });
        var book = await db.Books.SingleAsync(b => b.BookId == bookId, Ct);
        book.BookStatus = BookStatusEnum.CheckedOut;
        book.AvailabilityDate = lentAt.AddDays(14);
        await db.SaveChangesAsync(Ct);
    }

    private Task<List<ReservationItem>> ReservationsOf(int bookId) => factory.WithDbAsync(db =>
        db.ReservationItems.Where(r => r.BookId == bookId).OrderBy(r => r.ReservationDate).ToListAsync(Ct));

    private Task<List<NotificationItem>> NotificationsOf(string userId, int bookId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId && n.BookId == bookId).ToListAsync(Ct));
}
