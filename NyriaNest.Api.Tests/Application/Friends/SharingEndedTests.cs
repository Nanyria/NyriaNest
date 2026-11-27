using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Friends;
using NyriaNest.Application.Friends;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using NyriaNest.Infrastructure.Data;
using NyriaNest.Infrastructure.Repositories;

namespace NyriaNest.Api.Tests.Application.Friends;

/// <summary>
/// #85 step 4b: when a friend can no longer see my physical books (I turn off "Share my physical
/// library" for them, or either of us ends the friendship), their open requests for my books are
/// cancelled and their reservations removed. Only that direction: what they share with me is
/// untouched unless the friendship ends. A loan they already have stays until it's returned.
/// The friend is told once; I'm not, since I made the change.
/// </summary>
public class SharingEndedTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private readonly User _me;
    private readonly User _anna;
    private readonly User _erik;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SharingEndedTests()
    {
        using var db = _db.CreateContext();
        _me = TestData.User("me");
        _anna = TestData.User("anna");
        _erik = TestData.User("erik");
        db.Users.AddRange(_me, _anna, _erik);
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Turning_off_my_physical_library_cancels_the_friends_requests_and_reservations_for_my_books()
    {
        var withAnna = await FriendsAsync(_me, _anna, meShares: true);
        await FriendsAsync(_me, _erik, meShares: true);
        var freeBook = await BookAsync(_me, "My free book");
        var lentBook = await BookAsync(_me, "My lent book", lentToName: true);
        await OpenRequestAsync(freeBook, _anna);
        await ReserveAsync(lentBook, _anna, daysAgo: 5);
        await ReserveAsync(lentBook, _erik, daysAgo: 2);

        var result = await SetPhysicalSharingAsync(_me, withAnna, on: false);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        using var db = _db.CreateContext();
        var request = await db.BorrowRequests.SingleAsync(r => r.BorrowerId == _anna.Id, Ct);
        request.Status.Should().Be(BorrowRequestStatus.Cancelled);
        request.AnsweredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Anna leaves the queue; Erik moves up and now expects it when the loan is due
        var queue = await db.ReservationItems.Where(r => r.BookId == lentBook).ToListAsync(Ct);
        queue.Should().ContainSingle().Which.UserId.Should().Be(_erik.Id);
        var due = await db.CheckOutItems.Where(l => l.BookId == lentBook).Select(l => l.ReturnDate).SingleAsync(Ct);
        queue[0].AvailabilityDate.Should().Be(due);

        // Told once, whatever was cancelled; nobody else is told
        var told = await db.NotificationItems.Where(n => n.UserId == _anna.Id).ToListAsync(Ct);
        told.Should().ContainSingle().Which.Type.Should().Be(NotificationType.SharingEnded);
        told[0].Message.Should().Contain(_me.UserName);
        (await db.NotificationItems.AnyAsync(n => n.UserId != _anna.Id, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Only_my_books_are_affected_what_the_friend_shares_with_me_is_untouched()
    {
        var withAnna = await FriendsAsync(_me, _anna, meShares: true, friendShares: true);
        var annasBook = await BookAsync(_anna, "Anna's book");
        await OpenRequestAsync(annasBook, _me);

        await SetPhysicalSharingAsync(_me, withAnna, on: false);

        using var db = _db.CreateContext();
        (await db.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Requested);
        (await db.NotificationItems.AnyAsync(Ct)).Should().BeFalse("nothing of Anna's was cancelled");
    }

    [Fact]
    public async Task Turning_off_ebook_sharing_cancels_nothing()
    {
        // Requests and reservations are for physical books; e-books are #87
        var withAnna = await FriendsAsync(_me, _anna, meShares: true);
        await SetSharingAsync(_me, withAnna, SharingSetting.Ebooks, on: true);
        var myBook = await BookAsync(_me, "My book");
        await OpenRequestAsync(myBook, _anna);

        await SetSharingAsync(_me, withAnna, SharingSetting.Ebooks, on: false);

        using var db = _db.CreateContext();
        (await db.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Requested);
    }

    [Fact]
    public async Task Turning_it_off_when_nothing_is_open_cancels_nothing_and_tells_nobody()
    {
        var withAnna = await FriendsAsync(_me, _anna, meShares: true);

        await SetPhysicalSharingAsync(_me, withAnna, on: false);

        using var db = _db.CreateContext();
        (await db.NotificationItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task A_loan_the_friend_already_has_stays_until_it_is_returned()
    {
        var withAnna = await FriendsAsync(_me, _anna, meShares: true);
        var myBook = await BookAsync(_me, "Lent to Anna");
        using (var db = _db.CreateContext())
        {
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = myBook,
                UserId = _anna.Id,
                CheckOutDate = DateTime.UtcNow.AddDays(-3),
                ReturnDate = DateTime.UtcNow.AddDays(27),
            });
            await db.SaveChangesAsync(Ct);
        }

        await SetPhysicalSharingAsync(_me, withAnna, on: false);

        using var assert = _db.CreateContext();
        (await assert.CheckOutItems.SingleAsync(l => l.BookId == myBook, Ct)).UserId.Should().Be(_anna.Id);
    }

    [Fact]
    public async Task Ending_the_friendship_cancels_both_directions_and_tells_each_once()
    {
        var withAnna = await FriendsAsync(_me, _anna, meShares: true, friendShares: true);
        var myBook = await BookAsync(_me, "My book");
        var annasLentBook = await BookAsync(_anna, "Anna's lent book", lentToName: true);
        await OpenRequestAsync(myBook, _anna);
        await ReserveAsync(annasLentBook, _me, daysAgo: 1);

        // Anna ends it; it doesn't matter which of us does
        using (var db = _db.CreateContext())
        {
            var removed = await Service(db).RemoveAsync(_anna.Id, withAnna);
            removed.IsSuccess.Should().BeTrue(string.Join(", ", removed.ErrorMessages));
        }

        using var assert = _db.CreateContext();
        (await assert.BorrowRequests.SingleAsync(Ct)).Status.Should().Be(BorrowRequestStatus.Cancelled);
        (await assert.ReservationItems.AnyAsync(Ct)).Should().BeFalse();
        (await assert.NotificationItems.Where(n => n.UserId == _anna.Id).Select(n => n.Type).ToListAsync(Ct))
            .Should().Equal(NotificationType.SharingEnded);
        (await assert.NotificationItems.Where(n => n.UserId == _me.Id).Select(n => n.Type).ToListAsync(Ct))
            .Should().Equal(NotificationType.SharingEnded);
    }

    [Fact]
    public async Task Ending_a_friendship_with_nothing_open_tells_nobody()
    {
        var withAnna = await FriendsAsync(_me, _anna, meShares: true, friendShares: true);

        using (var db = _db.CreateContext())
            await Service(db).RemoveAsync(_me.Id, withAnna);

        using var assert = _db.CreateContext();
        (await assert.NotificationItems.AnyAsync(Ct)).Should().BeFalse();
    }

    // ---- Helpers ----

    private FriendshipService Service(AppDbContext db) =>
        new(db, new UserRepo(db), new NotificationService(db),
            Options.Create(new SharingLimitsOptions()),
            new AvailabilityService(new UserRepo(db), new BookRepo(db), db));

    private Task<APIResponse<FriendshipDto>> SetPhysicalSharingAsync(User who, int friendshipId, bool on) =>
        SetSharingAsync(who, friendshipId, SharingSetting.PhysicalLibrary, on);

    private async Task<APIResponse<FriendshipDto>> SetSharingAsync(User who, int friendshipId, SharingSetting setting, bool on)
    {
        using var db = _db.CreateContext();
        return await Service(db).SetSharingAsync(who.Id, friendshipId, setting, on);
    }

    /// <summary>An accepted friendship between me (the requester) and the friend, with the sharing given.</summary>
    private async Task<int> FriendsAsync(User me, User friend, bool meShares = false, bool friendShares = false)
    {
        using var db = _db.CreateContext();
        var row = new Friendship
        {
            RequesterId = me.Id,
            AddresseeId = friend.Id,
            Status = FriendshipStatus.Accepted,
            AcceptedAt = DateTime.UtcNow.AddDays(-30),
            RequesterSharesPhysicalLibrary = meShares,
            AddresseeSharesPhysicalLibrary = friendShares,
        };
        db.Friendships.Add(row);
        await db.SaveChangesAsync(Ct);
        return row.Id;
    }

    private async Task<int> BookAsync(User owner, string title, bool lentToName = false)
    {
        using var db = _db.CreateContext();
        var book = TestData.Book(title, status: lentToName ? BookStatusEnum.CheckedOut : BookStatusEnum.Available);
        book.OwnerId = owner.Id;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        if (lentToName)
        {
            var lentAt = DateTime.UtcNow.AddDays(-3);
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = book.BookId,
                BorrowerName = "Grandma",
                CheckOutDate = lentAt,
                ReturnDate = lentAt.AddDays(14),
            });
            await db.SaveChangesAsync(Ct);
        }
        return book.BookId;
    }

    private async Task OpenRequestAsync(int bookId, User friend)
    {
        using var db = _db.CreateContext();
        db.BorrowRequests.Add(new BorrowRequest
        {
            BookId = bookId,
            BorrowerId = friend.Id,
            Status = BorrowRequestStatus.Requested,
            RequestedAt = DateTime.UtcNow.AddDays(-1),
            AvailableAt = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task ReserveAsync(int bookId, User friend, int daysAgo)
    {
        using var db = _db.CreateContext();
        db.ReservationItems.Add(new ReservationItem
        {
            BookId = bookId,
            UserId = friend.Id,
            ReservationDate = DateTime.UtcNow.AddDays(-daysAgo),
        });
        await db.SaveChangesAsync(Ct);
    }
}
