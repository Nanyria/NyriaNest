using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #85 step 2: friends' books in the Library. A friend who shares their physical library with
/// me shows their physical books, tagged with their name. Only the setting that covers a kind of
/// book counts, and only between the two of us: not a pending or declined request, not the other
/// direction, not a friend's friend. E-books wait for #87. The same rule (BookVisibility) decides
/// the list and a single book (favorites, reviews), so both are checked here.
/// </summary>
public class FriendsBooksVisibilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LibraryUrl = "api/Library";
    private const string NewReviewUrl = "api/UserActions/newReview";
    private static string FavoriteUrl(int bookId) => $"api/UserActions/favorite/{bookId}";
    private static string BookReviewsUrl(int bookId) => $"api/Library/review/{bookId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The Library list ----

    [Fact]
    public async Task AFriendWhoSharesTheirPhysicalLibrary_TheirPhysicalBooksShow_WithTheirName_NotMarkedMine()
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true);
        var bookId = await AddBookAsync(friend.Id, "Friend shares: paperback");

        var books = await LibraryAsync(client);

        books.Should().ContainKey(bookId);
        var book = books[bookId];
        book.GetProperty("isMine").GetBoolean().Should().BeFalse();
        book.GetProperty("ownerName").GetString().Should().Be(friend.UserName);
    }

    [Fact]
    public async Task SharingWorks_WhicheverOfUsSentTheFriendRequest()
    {
        // The setting sits on the requester's or the addressee's side of the row
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(requester: me, addressee: friend, addresseeShares: true);
        var bookId = await AddBookAsync(friend.Id, "Friend shares: I asked");

        var books = await LibraryAsync(client);

        books.Should().ContainKey(bookId);
    }

    [Fact]
    public async Task AFriendWhoDoesntShare_TheirBooksDontShow()
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: false);
        var bookId = await AddBookAsync(friend.Id, "Friend doesn't share");

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(bookId);
    }

    [Theory]
    [InlineData(FriendshipStatus.Pending)]
    [InlineData(FriendshipStatus.Declined)]
    public async Task ARequestThatIsntAccepted_SharesNothing_EvenWithTheSettingOn(FriendshipStatus status)
    {
        // The UI can't turn a setting on before accepting, but the rule mustn't rely on that
        var (client, me) = await factory.LoginAsAsync();
        var notYet = await factory.CreateUserAsync();
        await FriendsAsync(notYet, me, friendShares: true, status);
        var bookId = await AddBookAsync(notYet.Id, $"Not a friend: {status}");

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(bookId);
    }

    [Fact]
    public async Task SharingIsOneWay_WhatIShareDoesntShowMeTheirBooks()
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(requester: me, addressee: friend, requesterShares: true);
        var bookId = await AddBookAsync(friend.Id, "Only I share");

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(bookId);
    }

    [Fact]
    public async Task AFriendsFriend_TheirBooksDontShow()
    {
        // Erik shares with me, Anna shares with Erik: Anna's books are Erik's to see, not mine
        var (client, me) = await factory.LoginAsAsync();
        var erik = await factory.CreateUserAsync();
        var anna = await factory.CreateUserAsync();
        await FriendsAsync(erik, me, friendShares: true);
        await FriendsAsync(anna, erik, friendShares: true);
        var annasBook = await AddBookAsync(anna.Id, "A friend's friend");

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(annasBook);
    }

    [Fact]
    public async Task AVisitor_DoesntSeeAnyonesBooks_ThoughTheyShareWithFriends()
    {
        var me = await factory.CreateUserAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true);
        var bookId = await AddBookAsync(friend.Id, "Friends only, not visitors");
        using var visitor = factory.CreateClient();

        var books = await LibraryAsync(visitor);

        books.Should().NotContainKey(bookId);
    }

    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public async Task AFriendsBookAnAdminTookDown_DoesntShow(BookStatusEnum adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true);
        var bookId = await AddBookAsync(friend.Id, $"Friend's book, {adminStatus}", adminStatus: adminStatus);

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(bookId);
    }

    [Fact]
    public async Task AFriendWhoSharesEverything_TheirEbooksDontShowYet()
    {
        // #87 turns this on: the one place is the e-book line in BookVisibility. Flip this test then.
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true, friendSharesEbooks: true);
        var ebookId = await AddBookAsync(friend.Id, "Friend's e-book", BookTypeEnums.EBook);

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(ebookId);
    }

    [Theory]
    [InlineData(BookTypeEnums.Audiobook)]
    [InlineData(BookTypeEnums.Undefined)]
    public async Task OnlyPaperbacksAndHardcovers_ArePhysical_SoOtherTypesDontShow(BookTypeEnums type)
    {
        // Own books can only be paperbacks or hardcovers today (BookService), but the rule
        // mustn't rely on that: an audiobook isn't physical, and an unknown type isn't either
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true, friendSharesEbooks: true);
        var bookId = await AddBookAsync(friend.Id, $"Friend's {type}", type);

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(bookId);
    }

    [Theory]
    [InlineData(BookTypeEnums.Paperback)]
    [InlineData(BookTypeEnums.Hardcover)]
    public async Task PaperbacksAndHardcovers_Show(BookTypeEnums type)
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true);
        var bookId = await AddBookAsync(friend.Id, $"Friend's {type}", type);

        var books = await LibraryAsync(client);

        books.Should().ContainKey(bookId);
    }

    [Fact]
    public async Task MyOwnAndSharedBooks_StillShow_NextToAFriendsBooks()
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true);
        var mine = await AddBookAsync(me.Id, "Mine, next to a friend's");
        var shared = await AddBookAsync(null, "Shared, next to a friend's", BookTypeEnums.EBook);
        var friends = await AddBookAsync(friend.Id, "A friend's, next to mine");

        var books = await LibraryAsync(client);

        books[mine].GetProperty("isMine").GetBoolean().Should().BeTrue();
        books[shared].GetProperty("isMine").GetBoolean().Should().BeFalse();
        books.Should().ContainKey(friends);
    }

    // ---- A single book (the same rule): favorites and reviews ----

    [Fact]
    public async Task ASharedFriendsBook_CanBeFavoritedAndReviewed()
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: true);
        var bookId = await AddBookAsync(friend.Id, "Friend's book to favorite");

        var favorite = await client.PutAsync(FavoriteUrl(bookId), null, Ct);
        var reviews = await client.GetAsync(BookReviewsUrl(bookId), Ct);
        var review = await client.PostAsJsonAsync(NewReviewUrl, ReviewBody(bookId), Ct);

        favorite.StatusCode.Should().Be(HttpStatusCode.Created, await favorite.Content.ReadAsStringAsync(Ct));
        reviews.StatusCode.Should().Be(HttpStatusCode.OK, await reviews.Content.ReadAsStringAsync(Ct));
        review.StatusCode.Should().Be(HttpStatusCode.Created, await review.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ANotSharedFriendsBook_LooksMissing()
    {
        var (client, me) = await factory.LoginAsAsync();
        var friend = await factory.CreateUserAsync();
        await FriendsAsync(friend, me, friendShares: false);
        var bookId = await AddBookAsync(friend.Id, "Friend's book, not shared");

        var favorite = await client.PutAsync(FavoriteUrl(bookId), null, Ct);
        var reviews = await client.GetAsync(BookReviewsUrl(bookId), Ct);

        favorite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reviews.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Helpers ----

    /// <summary>
    /// A friendship row written straight to the database. <paramref name="friendShares"/> is the
    /// requester sharing their physical library with the addressee (the usual "friend → me" case).
    /// </summary>
    private Task FriendsAsync(User friend, User me, bool friendShares,
        FriendshipStatus status = FriendshipStatus.Accepted, bool friendSharesEbooks = false) =>
        FriendsAsync(requester: friend, addressee: me, requesterShares: friendShares,
            status: status, requesterSharesEbooks: friendSharesEbooks);

    private Task FriendsAsync(User requester, User addressee, bool requesterShares = false,
        bool addresseeShares = false, FriendshipStatus status = FriendshipStatus.Accepted,
        bool requesterSharesEbooks = false) => factory.WithDbAsync(async db =>
    {
        db.Friendships.Add(new Friendship
        {
            RequesterId = requester.Id,
            AddresseeId = addressee.Id,
            Status = status,
            AcceptedAt = status == FriendshipStatus.Accepted ? DateTime.UtcNow : null,
            RequesterSharesPhysicalLibrary = requesterShares,
            RequesterSharesEbooks = requesterSharesEbooks,
            AddresseeSharesPhysicalLibrary = addresseeShares,
        });
        await db.SaveChangesAsync(Ct);
    });

    private Task<int> AddBookAsync(string? ownerId, string title,
        BookTypeEnums type = BookTypeEnums.Paperback, BookStatusEnum? adminStatus = null) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title, type);
        book.OwnerId = ownerId;
        if (adminStatus != null)
        {
            book.AdminStatus = adminStatus;
            book.AdminStatusSetAt = DateTime.UtcNow;
        }
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    // The API replaces userId with the logged-in user (see ReviewOwnershipTests)
    private static object ReviewBody(int bookId) =>
        new { userId = "replaced-by-the-api", bookId, reviewHeader = "Header", reviewText = "Text", ratingItem = new { rating = 7 } };

    // Every book in the list, by id
    private static async Task<Dictionary<int, JsonElement>> LibraryAsync(HttpClient client)
    {
        var response = await client.GetAsync(LibraryUrl, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("result").EnumerateArray().ToDictionary(
            b => b.GetProperty("bookId").GetInt32(),
            b => b.Clone());
    }
}
