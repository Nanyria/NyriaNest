using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// How reservations behave, through GET api/UserActions/reserved, PUT reserve/{bookId} and
/// PUT unreserve/{bookId} (#116 moved the code into ReservationService). Since #85 the queue is for
/// friends' books that are lent out: a free book is asked for instead, and the shared library's
/// e-books are read, not reserved. Who may reserve a friend's book, and what happens when it comes
/// back, are in FriendQueueTests.
/// </summary>
public class ReservationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ListUrl = "api/UserActions/reserved";
    private static string ReserveUrl(int bookId) => $"api/UserActions/reserve/{bookId}";
    private static string UnreserveUrl(int bookId) => $"api/UserActions/unreserve/{bookId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Reserving ----

    [Fact]
    public async Task Reserve_BehindSomeoneElse_IWaitOneLoanPeriodLonger()
    {
        var owner = await factory.CreateUserAsync();
        var first = await FriendOfAsync(owner);
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var bookId = await LentBookAsync(owner, dueInDays: 10);
        await AddReservationAsync(bookId, first.Id, daysAgo: 1);
        var due = DateTime.UtcNow.AddDays(10);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
        (await ReservationOf(bookId, first.Id))!.AvailabilityDate.Should().BeCloseTo(due, TimeSpan.FromMinutes(1));
        (await ReservationOf(bookId, me.Id))!.AvailabilityDate.Should().BeCloseTo(due.AddMonths(1), TimeSpan.FromMinutes(1));
        (await AvailabilityDateOf(bookId)).Should().BeCloseTo(due.AddMonths(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Reserve_TheSameBookTwice_Returns409_AndKeepsOneReservation()
    {
        var owner = await factory.CreateUserAsync();
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var bookId = await LentBookAsync(owner);
        await client.PutAsync(ReserveUrl(bookId), null, Ct);

        var again = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync(Ct)).Should().Contain("Book already reserved by user.");
        (await ReservationCountOf(bookId, me.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Reserve_ABookIHave_Returns409()
    {
        var owner = await factory.CreateUserAsync();
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var bookId = await LentBookAsync(owner, toUserId: me.Id);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Book already checked out by user.");
        (await ReservationCountOf(bookId, me.Id)).Should().Be(0);
    }

    // #85: was 409 ("This book isn't available."). A friend's book an admin took down can't be
    // seen any more, so it looks missing, as in the Library.
    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public async Task Reserve_AFriendsBookMarkedLostOrRemoved_Returns404(BookStatusEnum adminStatus)
    {
        var owner = await factory.CreateUserAsync();
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var bookId = await LentBookAsync(owner, adminStatus: adminStatus);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReservationCountOf(bookId, me.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Reserve_ASharedLibraryBook_Returns400_TheyAreReadNotReserved()
    {
        // #85: the shared library holds e-books (ADR 0004), and an e-book is read without waiting
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await SharedEbookAsync();

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("read, not reserved");
        (await ReservationCountOf(bookId, me.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Reserve_ABookThatDoesntExist_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.PutAsync(ReserveUrl(999999), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("User or Book not found.");
    }

    // ---- Cancelling ----

    [Fact]
    public async Task Unreserve_MovesTheNextInLineUp_SoTheyExpectItWhenTheLoanIsDue()
    {
        var owner = await factory.CreateUserAsync();
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var next = await FriendOfAsync(owner);
        var bookId = await LentBookAsync(owner, dueInDays: 10);
        await AddReservationAsync(bookId, me.Id, daysAgo: 2, availableInDays: 10);
        await AddReservationAsync(bookId, next.Id, daysAgo: 1, availableInDays: 40);

        var response = await client.PutAsync(UnreserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationOf(bookId, next.Id))!.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow.AddDays(10), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Unreserve_WhileAFriendHasTheBook_KeepsItCheckedOut_AndTheRestOfTheQueue()
    {
        // From LoanRulesTests (#39): cancelling sends "Returned" to GetBookStatus, which only looks
        // at the queue, so the book might show as free while on loan
        var owner = await factory.CreateUserAsync();
        var holder = await FriendOfAsync(owner);
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var other = await FriendOfAsync(owner);
        var bookId = await LentBookAsync(owner, toUserId: holder.Id);
        await AddReservationAsync(bookId, me.Id, daysAgo: 2);
        await AddReservationAsync(bookId, other.Id, daysAgo: 1);

        var response = await client.PutAsync(UnreserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationOf(bookId, me.Id)).Should().BeNull();
        (await ReservationOf(bookId, other.Id)).Should().NotBeNull();
        (await factory.WithDbAsync(db => db.CheckOutItems.Where(c => c.BookId == bookId).Select(c => c.UserId).SingleAsync(Ct)))
            .Should().Be(holder.Id);
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
    }

    [Fact]
    public async Task Unreserve_ABookINeverReserved_Returns409()
    {
        var owner = await factory.CreateUserAsync();
        var (client, _) = await LoggedInFriendOfAsync(owner);
        var bookId = await LentBookAsync(owner);

        var response = await client.PutAsync(UnreserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Book not reserved by user.");
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
    }

    [Fact]
    public async Task Unreserve_ABookThatDoesntExist_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.PutAsync(UnreserveUrl(999999), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("User or Book not found.");
    }

    // ---- My reservations ----

    [Fact]
    public async Task List_ShowsOnlyMyReservations_NewestFirst_WithTheBooks()
    {
        var owner = await factory.CreateUserAsync();
        var (client, me) = await LoggedInFriendOfAsync(owner);
        var other = await FriendOfAsync(owner);
        var older = await LentBookAsync(owner, title: "Older");
        var newer = await LentBookAsync(owner, title: "Newer");
        var theirs = await LentBookAsync(owner, title: "Theirs");
        await AddReservationAsync(older, me.Id, daysAgo: 3);
        await AddReservationAsync(newer, me.Id, daysAgo: 1);
        await AddReservationAsync(theirs, other.Id, daysAgo: 2);

        var response = await client.GetAsync(ListUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await ResultOf(response)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("bookId").GetInt32()).Should().Equal(newer, older);
        items.Select(i => i.GetProperty("book").GetProperty("title").GetString()).Should().Equal("Newer", "Older");
    }

    // #85 9a: My reservations (#66) shows whose book it is and my place in its queue
    [Fact]
    public async Task List_ShowsTheOwner_AndMyPlaceInTheQueue()
    {
        var anna = await factory.CreateUserAsync();
        var bo = await factory.CreateUserAsync();
        var (client, me) = await LoggedInFriendOfAsync(anna);
        await ShareWithAsync(bo, me);
        var ahead = await FriendOfAsync(anna);
        var behind = await FriendOfAsync(anna);
        var annasBook = await LentBookAsync(anna, title: "Anna's");
        var bosBook = await LentBookAsync(bo, title: "Bo's");
        // On Anna's book I'm second: one reserved before me, one after
        await AddReservationAsync(annasBook, ahead.Id, daysAgo: 5);
        await AddReservationAsync(annasBook, me.Id, daysAgo: 3);
        await AddReservationAsync(annasBook, behind.Id, daysAgo: 1);
        // On Bo's book I'm the only one
        await AddReservationAsync(bosBook, me.Id, daysAgo: 2);

        var response = await client.GetAsync(ListUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var items = (await ResultOf(response)).EnumerateArray()
            .ToDictionary(i => i.GetProperty("bookId").GetInt32());
        items.Keys.Should().BeEquivalentTo([annasBook, bosBook]);
        items[annasBook].GetProperty("ownerUserName").GetString().Should().Be(anna.UserName);
        items[annasBook].GetProperty("queuePosition").GetInt32().Should().Be(2);
        items[bosBook].GetProperty("ownerUserName").GetString().Should().Be(bo.UserName);
        items[bosBook].GetProperty("queuePosition").GetInt32().Should().Be(1);
    }

    // Unlike the reading list, an empty list here is an empty 200
    [Fact]
    public async Task List_WhenEmpty_IsAnEmpty200()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.GetAsync(ListUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ResultOf(response)).GetArrayLength().Should().Be(0);
    }

    // ---- Helpers ----

    /// <summary>A user the owner shares their physical library with.</summary>
    private async Task<User> FriendOfAsync(User owner)
    {
        var friend = await factory.CreateUserAsync();
        await ShareWithAsync(owner, friend);
        return friend;
    }

    private async Task<(HttpClient, User)> LoggedInFriendOfAsync(User owner)
    {
        var (client, friend) = await factory.LoginAsAsync();
        await ShareWithAsync(owner, friend);
        return (client, friend);
    }

    private Task ShareWithAsync(User owner, User friend) => factory.WithDbAsync(async db =>
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

    /// <summary>The owner's paperback, lent out (to a name, or to <paramref name="toUserId"/>) and due in some days.</summary>
    private Task<int> LentBookAsync(User owner, int dueInDays = 14, string? toUserId = null,
        BookStatusEnum? adminStatus = null, string title = "A friend's paperback") => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title: title, status: adminStatus ?? BookStatusEnum.CheckedOut);
        book.OwnerId = owner.Id;
        book.AdminStatus = adminStatus;
        book.AvailabilityDate = DateTime.UtcNow.AddDays(dueInDays);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        db.CheckOutItems.Add(new CheckedOutItem
        {
            BookId = book.BookId,
            UserId = toUserId,
            BorrowerName = toUserId == null ? "Grandma" : null,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(dueInDays),
        });
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<int> SharedEbookAsync() => factory.WithDbAsync(async db =>
    {
        var book = TestData.Ebook("A shared e-book");
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
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

    private Task<ReservationItem?> ReservationOf(int bookId, string userId) => factory.WithDbAsync(db =>
        db.ReservationItems.AsNoTracking().SingleOrDefaultAsync(r => r.BookId == bookId && r.UserId == userId, Ct));

    private Task<int> ReservationCountOf(int bookId, string userId) => factory.WithDbAsync(db =>
        db.ReservationItems.CountAsync(r => r.BookId == bookId && r.UserId == userId, Ct));

    private Task<BookStatusEnum> StatusOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct));

    private Task<DateTime> AvailabilityDateOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.AvailabilityDate).SingleAsync(Ct));

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("result").Clone();
    }
}
