using System.Net;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Loans;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using NyriaNest.Infrastructure.Data;
using NyriaNest.Infrastructure.Repositories;

namespace NyriaNest.Api.Tests.Application.Loans;

/// <summary>
/// #85 step 3: a friend asks to borrow a physical book, and the owner chooses. Only a friend the
/// owner shares their physical library with may ask, and only while the book isn't lent out
/// (then they reserve instead). Several friends may ask at once, one open request each. Accepting
/// one lends the book (a normal 1-month loan); the other open requests join the queue in the order
/// they asked. A request that isn't yours to answer or withdraw looks missing (404).
/// The routes and "me from the token" are in BorrowRequestsControllerTests.
/// </summary>
public class BorrowRequestServiceTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly User _owner;
    private readonly User _friend;      // the owner shares their physical library with them
    private readonly User _otherFriend; // shared with too
    private readonly User _notShared;   // a friend, but the owner doesn't share with them
    private readonly User _stranger;    // not a friend
    private readonly int _bookId;       // the owner's paperback

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public BorrowRequestServiceTests()
    {
        using var db = _db.CreateContext();
        _owner = TestData.User("owner");
        _friend = TestData.User("friend");
        _otherFriend = TestData.User("otherfriend");
        _notShared = TestData.User("notshared");
        _stranger = TestData.User("stranger");
        db.Users.AddRange(_owner, _friend, _otherFriend, _notShared, _stranger);
        db.Friendships.AddRange(
            Friends(_owner, _friend, ownerShares: true),
            Friends(_owner, _otherFriend, ownerShares: true),
            Friends(_owner, _notShared, ownerShares: false));
        var book = OwnedBook(_owner, "The owner's paperback");
        db.Books.Add(book);
        db.SaveChanges();
        _bookId = book.BookId;
    }

    public void Dispose() => _db.Dispose();

    // ---- Asking ----

    [Fact]
    public async Task A_friend_who_is_shared_with_can_ask_and_the_owner_is_told()
    {
        var result = await RequestAsync(_friend, _bookId);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        var request = await db.BorrowRequests.SingleAsync(Ct);
        request.Id.Should().Be(result.Result.Id);
        request.BookId.Should().Be(_bookId);
        request.BorrowerId.Should().Be(_friend.Id);
        request.Status.Should().Be(BorrowRequestStatus.Requested);
        request.RequestedAt.Should().Be(FakeClock.Start);
        request.AvailableAt.Should().Be(FakeClock.Start, "a free book is available from the moment they ask");

        var told = await db.NotificationItems.SingleAsync(n => n.UserId == _owner.Id, Ct);
        told.Type.Should().Be(NotificationType.LoanRequested);
        told.BookId.Should().Be(_bookId);
        told.Message.Should().Contain(_friend.UserName).And.Contain("The owner's paperback");
    }

    public enum Asker { NotShared, Stranger }

    [Theory]
    [InlineData(Asker.NotShared)]
    [InlineData(Asker.Stranger)]
    public async Task Someone_the_owner_doesnt_share_with_gets_a_404_and_nothing_is_saved(Asker who)
    {
        // They can't see the book, so it looks missing, as everywhere else
        var asker = who == Asker.NotShared ? _notShared : _stranger;

        var result = await RequestAsync(asker, _bookId);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await NothingSavedAsync();
    }

    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public async Task A_book_an_admin_took_down_gets_a_404(BookStatusEnum adminStatus)
    {
        using (var db = _db.CreateContext())
        {
            var book = await db.Books.SingleAsync(b => b.BookId == _bookId, Ct);
            book.AdminStatus = adminStatus;
            book.AdminStatusSetAt = FakeClock.Start;
            await db.SaveChangesAsync(Ct);
        }

        var result = await RequestAsync(_friend, _bookId);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await NothingSavedAsync();
    }

    [Fact]
    public async Task Asking_for_your_own_book_is_refused()
    {
        var result = await RequestAsync(_owner, _bookId);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NothingSavedAsync();
    }

    [Fact]
    public async Task Asking_for_a_shared_library_book_is_refused()
    {
        // The shared library's e-books are read, not lent (the seeded e-book, ADR 0004)
        int sharedId;
        using (var db = _db.CreateContext())
            sharedId = await db.Books.Where(b => b.OwnerId == null).Select(b => b.BookId).FirstAsync(Ct);

        var result = await RequestAsync(_friend, sharedId);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NothingSavedAsync();
    }

    [Fact]
    public async Task A_missing_book_gets_a_404()
    {
        var result = await RequestAsync(_friend, 999_999);

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public enum LentTo { AName, AFriend }

    [Theory]
    [InlineData(LentTo.AName)]
    [InlineData(LentTo.AFriend)]
    public async Task A_book_that_is_lent_out_is_refused_with_a_hint_to_reserve(LentTo lentTo)
    {
        using (var db = _db.CreateContext())
        {
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = _bookId,
                UserId = lentTo == LentTo.AFriend ? _otherFriend.Id : null,
                BorrowerName = lentTo == LentTo.AName ? "Grandma" : null,
                CheckOutDate = FakeClock.Start,
                ReturnDate = FakeClock.Start.AddMonths(1),
            });
            await db.SaveChangesAsync(Ct);
        }

        var result = await RequestAsync(_friend, _bookId);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        result.ErrorMessages.Should().ContainSingle().Which.Should().Contain("reserve");
        await NothingSavedAsync();
    }

    [Fact]
    public async Task Asking_twice_is_refused_the_first_request_is_still_open()
    {
        await RequestAsync(_friend, _bookId);

        var second = await RequestAsync(_friend, _bookId);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var db = _db.CreateContext();
        (await db.BorrowRequests.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Several_friends_can_ask_for_the_same_book_and_the_owner_chooses()
    {
        (await RequestAsync(_friend, _bookId)).IsSuccess.Should().BeTrue();
        (await RequestAsync(_otherFriend, _bookId)).IsSuccess.Should().BeTrue();

        using var db = _db.CreateContext();
        (await db.BorrowRequests.CountAsync(r => r.Status == BorrowRequestStatus.Requested, Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Asking_again_after_a_decline_works()
    {
        var first = await RequestAsync(_friend, _bookId);
        await DeclineAsync(_owner, first.Result.Id);

        var again = await RequestAsync(_friend, _bookId);

        again.IsSuccess.Should().BeTrue(string.Join(", ", again.ErrorMessages));
    }

    // ---- The owner accepts ----

    [Fact]
    public async Task Accepting_lends_the_book_for_a_month_and_tells_the_friend()
    {
        var asked = await RequestAsync(_friend, _bookId);
        _clock.Advance(TimeSpan.FromDays(2));
        var acceptedAt = _clock.UtcNow;

        var result = await AcceptAsync(_owner, asked.Result.Id);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        var request = await db.BorrowRequests.SingleAsync(Ct);
        request.Status.Should().Be(BorrowRequestStatus.Lent);
        request.AnsweredAt.Should().Be(acceptedAt);

        // A normal loan, as the rest of the loan system knows it
        var loan = await db.CheckOutItems.SingleAsync(l => l.BookId == _bookId, Ct);
        loan.UserId.Should().Be(_friend.Id);
        loan.BorrowerName.Should().BeNull();
        loan.CheckOutDate.Should().Be(acceptedAt);
        loan.ReturnDate.Should().Be(acceptedAt.AddMonths(1));

        var book = await db.Books.SingleAsync(b => b.BookId == _bookId, Ct);
        book.BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        book.AvailabilityDate.Should().Be(acceptedAt.AddMonths(1));

        var history = await db.StatusHistoryItems.SingleAsync(h => h.BookId == _bookId, Ct);
        history.UserId.Should().Be(_owner.Id);
        history.BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        history.Notes.Should().Contain(_friend.UserName);
        history.Timestamp.Should().Be(acceptedAt);

        var told = await db.NotificationItems.SingleAsync(n => n.UserId == _friend.Id, Ct);
        told.Type.Should().Be(NotificationType.LoanRequestAnswered);
        told.BookId.Should().Be(_bookId);
    }

    [Fact]
    public async Task Accepting_one_puts_the_other_open_requests_in_the_queue_in_the_order_they_asked()
    {
        // otherFriend asks first, then a third friend; the owner lends it to friend, who asked last
        var third = TestData.User("third");
        using (var db = _db.CreateContext())
        {
            db.Users.Add(third);
            db.Friendships.Add(Friends(_owner, third, ownerShares: true));
            await db.SaveChangesAsync(Ct);
        }
        await RequestAsync(_otherFriend, _bookId);
        _clock.Advance(TimeSpan.FromHours(1));
        await RequestAsync(third, _bookId);
        _clock.Advance(TimeSpan.FromHours(1));
        var chosen = await RequestAsync(_friend, _bookId);

        var result = await AcceptAsync(_owner, chosen.Result.Id);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var assert = _db.CreateContext();
        var others = await assert.BorrowRequests.Where(r => r.BorrowerId != _friend.Id).ToListAsync(Ct);
        others.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(BorrowRequestStatus.Queued);
            r.AnsweredAt.Should().Be(_clock.UtcNow);
        });

        // Their place in the queue keeps the time they asked; the first gets it when the loan is due
        var queue = await assert.ReservationItems.Where(r => r.BookId == _bookId)
            .OrderBy(r => r.ReservationDate).ToListAsync(Ct);
        queue.Select(r => r.UserId).Should().Equal(_otherFriend.Id, third.Id);
        queue[0].ReservationDate.Should().Be(FakeClock.Start);
        queue[0].AvailabilityDate.Should().Be(_clock.UtcNow.AddMonths(1));
        queue[1].AvailabilityDate.Should().Be(_clock.UtcNow.AddMonths(2));

        // Each is told it went to someone else and that they're in the queue
        foreach (var queued in new[] { _otherFriend.Id, third.Id })
        {
            var told = await assert.NotificationItems
                .Where(n => n.UserId == queued && n.Type == NotificationType.LoanRequestAnswered)
                .SingleAsync(Ct);
            told.Message.Should().Contain("queue");
        }
    }

    [Fact]
    public async Task A_book_lent_by_name_in_the_meantime_cant_be_accepted()
    {
        var asked = await RequestAsync(_friend, _bookId);
        using (var db = _db.CreateContext())
        {
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = _bookId,
                BorrowerName = "Grandma",
                CheckOutDate = FakeClock.Start,
                ReturnDate = FakeClock.Start.AddDays(14),
            });
            await db.SaveChangesAsync(Ct);
        }

        var result = await AcceptAsync(_owner, asked.Result.Id);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var assert = _db.CreateContext();
        (await assert.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Requested);
        (await assert.CheckOutItems.SingleAsync(Ct)).BorrowerName.Should().Be("Grandma");
    }

    // ---- The owner declines ----

    [Fact]
    public async Task Declining_leaves_the_book_as_it_was_and_tells_the_friend()
    {
        var asked = await RequestAsync(_friend, _bookId);
        _clock.Advance(TimeSpan.FromDays(1));

        var result = await DeclineAsync(_owner, asked.Result.Id);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        var request = await db.BorrowRequests.SingleAsync(Ct);
        request.Status.Should().Be(BorrowRequestStatus.Declined);
        request.AnsweredAt.Should().Be(_clock.UtcNow);
        (await db.CheckOutItems.AnyAsync(Ct)).Should().BeFalse();
        (await db.Books.SingleAsync(b => b.BookId == _bookId, Ct)).BookStatus.Should().Be(BookStatusEnum.Available);

        var told = await db.NotificationItems.SingleAsync(n => n.UserId == _friend.Id, Ct);
        told.Type.Should().Be(NotificationType.LoanRequestAnswered);
    }

    // ---- The friend withdraws ----

    [Fact]
    public async Task The_friend_can_withdraw_their_request()
    {
        var asked = await RequestAsync(_friend, _bookId);
        _clock.Advance(TimeSpan.FromHours(3));

        var result = await CancelAsync(_friend, asked.Result.Id);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        var request = await db.BorrowRequests.SingleAsync(Ct);
        request.Status.Should().Be(BorrowRequestStatus.Cancelled);
        request.AnsweredAt.Should().Be(_clock.UtcNow);
    }

    // ---- Requests the queue opened (#85 step 4) ----
    // When a lent book comes back, everyone in the queue gets an open request, keeping the date
    // they started waiting, and the owner chooses, as for any other requests.

    [Fact]
    public async Task Declining_one_of_the_queues_requests_leaves_the_others_open_and_tells_nobody_else()
    {
        var third = await ThirdFriendAsync();
        var (first, second) = await OpenedByTheQueueAsync(_otherFriend, third);
        _clock.Advance(TimeSpan.FromDays(3));

        var result = await DeclineAsync(_owner, first);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        (await db.BorrowRequests.SingleAsync(r => r.Id == first, Ct)).Status.Should().Be(BorrowRequestStatus.Declined);
        var other = await db.BorrowRequests.SingleAsync(r => r.Id == second, Ct);
        other.Status.Should().Be(BorrowRequestStatus.Requested);
        other.AvailableAt.Should().Be(FakeClock.Start, "nothing is handed on: it was already open");
        (await db.NotificationItems.AnyAsync(n => n.UserId == third.Id, Ct)).Should().BeFalse();
        (await db.Books.SingleAsync(b => b.BookId == _bookId, Ct)).BookStatus.Should().Be(BookStatusEnum.Available);
    }

    [Fact]
    public async Task Accepting_one_of_the_queues_requests_puts_the_others_back_in_the_queue_with_the_date_they_started_waiting()
    {
        // The owner picks third, who started waiting after otherFriend
        var third = await ThirdFriendAsync();
        var (firstId, thirdsRequest) = await OpenedByTheQueueAsync(_otherFriend, third);

        var result = await AcceptAsync(_owner, thirdsRequest);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var assert = _db.CreateContext();
        (await assert.CheckOutItems.SingleAsync(l => l.BookId == _bookId, Ct)).UserId.Should().Be(third.Id);
        (await assert.BorrowRequests.SingleAsync(r => r.Id == firstId, Ct)).Status.Should().Be(BorrowRequestStatus.Queued);
        var queue = await assert.ReservationItems.Where(r => r.BookId == _bookId).ToListAsync(Ct);
        queue.Should().ContainSingle().Which.UserId.Should().Be(_otherFriend.Id);
        queue[0].ReservationDate.Should().Be(WaitingSince(0), "they keep the date they first reserved");
        queue[0].AvailabilityDate.Should().Be(_clock.UtcNow.AddMonths(1));
    }

    [Fact]
    public async Task A_friend_can_ask_for_a_free_book_while_the_queues_requests_are_open()
    {
        // Nothing to jump ahead of: the owner sees everyone's "waiting since" and chooses
        await OpenedByTheQueueAsync(_otherFriend);

        var result = await RequestAsync(_friend, _bookId);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        (await db.BorrowRequests.CountAsync(r => r.Status == BorrowRequestStatus.Requested, Ct)).Should().Be(2);
    }

    // ---- Only the right person, only an open request ----

    public enum Answer { Accept, Decline }

    [Theory]
    [InlineData(Answer.Accept)]
    [InlineData(Answer.Decline)]
    public async Task Only_the_owner_can_answer_for_anyone_else_it_looks_missing(Answer answer)
    {
        var asked = await RequestAsync(_friend, _bookId);

        foreach (var notOwner in new[] { _friend, _otherFriend, _stranger })
        {
            var result = answer == Answer.Accept
                ? await AcceptAsync(notOwner, asked.Result.Id)
                : await DeclineAsync(notOwner, asked.Result.Id);
            result.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{notOwner.UserName} isn't the owner");
        }

        using var db = _db.CreateContext();
        (await db.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Requested);
        (await db.CheckOutItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_friend_who_asked_can_withdraw_for_anyone_else_it_looks_missing()
    {
        var asked = await RequestAsync(_friend, _bookId);

        foreach (var notAsker in new[] { _owner, _otherFriend, _stranger })
        {
            var result = await CancelAsync(notAsker, asked.Result.Id);
            result.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{notAsker.UserName} didn't ask");
        }

        using var db = _db.CreateContext();
        (await db.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Requested);
    }

    public enum Act { Accept, Decline, Cancel }

    [Theory]
    [InlineData(Act.Accept)]
    [InlineData(Act.Decline)]
    [InlineData(Act.Cancel)]
    public async Task A_request_that_is_already_answered_cant_be_answered_again(Act act)
    {
        var asked = await RequestAsync(_friend, _bookId);
        await DeclineAsync(_owner, asked.Result.Id);

        var result = act switch
        {
            Act.Accept => await AcceptAsync(_owner, asked.Result.Id),
            Act.Decline => await DeclineAsync(_owner, asked.Result.Id),
            _ => await CancelAsync(_friend, asked.Result.Id),
        };

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var db = _db.CreateContext();
        (await db.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Declined);
    }

    // ---- Helpers ----

    private BorrowRequestService Service(AppDbContext db)
    {
        var users = new UserRepo(db);
        var notifications = new NotificationService(db);
        var availability = new AvailabilityService(users, new BookRepo(db), db);
        return new(db, users, notifications, availability, _clock, NullLogger<BorrowRequestService>.Instance);
    }

    private async Task<APIResponse<BorrowRequestDto>> RequestAsync(User who, int bookId)
    {
        using var db = _db.CreateContext();
        return await Service(db).RequestAsync(who.Id, bookId);
    }

    private async Task<APIResponse<BorrowRequestDto>> AcceptAsync(User who, int requestId)
    {
        using var db = _db.CreateContext();
        return await Service(db).AcceptAsync(who.Id, requestId);
    }

    private async Task<APIResponse<BorrowRequestDto>> DeclineAsync(User who, int requestId)
    {
        using var db = _db.CreateContext();
        return await Service(db).DeclineAsync(who.Id, requestId);
    }

    private async Task<APIResponse<BorrowRequestDto>> CancelAsync(User who, int requestId)
    {
        using var db = _db.CreateContext();
        return await Service(db).CancelAsync(who.Id, requestId);
    }

    private async Task<User> ThirdFriendAsync()
    {
        var third = TestData.User("third");
        using var db = _db.CreateContext();
        db.Users.Add(third);
        db.Friendships.Add(Friends(_owner, third, ownerShares: true));
        await db.SaveChangesAsync(Ct);
        return third;
    }

    /// <summary>When the n:th in the queue (0 = first) started waiting: days before the book came back.</summary>
    private static DateTime WaitingSince(int place) => FakeClock.Start.AddDays(-20 + place);

    /// <summary>
    /// The (free) book as it looks right after it came back to a queue of these friends, in this
    /// order: each has an open request, waiting since they reserved, available since now (Start).
    /// No reservations are left. Returns the first two requests' ids (0 when there's only one).
    /// </summary>
    private async Task<(int First, int Second)> OpenedByTheQueueAsync(params User[] inLine)
    {
        using var db = _db.CreateContext();
        var requests = inLine.Select((friend, place) => new BorrowRequest
        {
            BookId = _bookId,
            BorrowerId = friend.Id,
            Status = BorrowRequestStatus.Requested,
            RequestedAt = WaitingSince(place),
            AvailableAt = FakeClock.Start,
        }).ToList();
        db.BorrowRequests.AddRange(requests);
        await db.SaveChangesAsync(Ct);
        return (requests[0].Id, requests.Count > 1 ? requests[1].Id : 0);
    }

    private async Task NothingSavedAsync()
    {
        using var db = _db.CreateContext();
        (await db.BorrowRequests.AnyAsync(Ct)).Should().BeFalse();
        (await db.NotificationItems.AnyAsync(Ct)).Should().BeFalse();
    }

    private static Friendship Friends(User owner, User friend, bool ownerShares) => new()
    {
        RequesterId = owner.Id,
        AddresseeId = friend.Id,
        Status = FriendshipStatus.Accepted,
        AcceptedAt = FakeClock.Start.AddDays(-30),
        RequesterSharesPhysicalLibrary = ownerShares,
    };

    private static Book OwnedBook(User owner, string title)
    {
        var book = TestData.Book(title);
        book.OwnerId = owner.Id;
        return book;
    }
}
