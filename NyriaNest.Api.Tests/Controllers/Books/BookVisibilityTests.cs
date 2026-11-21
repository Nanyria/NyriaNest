using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #83 step 6a: which books a reader can see. Today that's the shared library and their
/// own books (not ones an admin marked Lost or Removed). The Library list, favorites and
/// a book's reviews all follow that one rule. Someone else's book looks missing (404).
/// Friends' books join the rule later (#84/#85) and then show under the shared books.
/// </summary>
public class BookVisibilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LibraryUrl = "api/Library";
    private const string NewReviewUrl = "api/UserActions/newReview";
    private static string FavoriteUrl(int bookId) => $"api/UserActions/favorite/{bookId}";
    private static string BookReviewsUrl(int bookId) => $"api/Library/review/{bookId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public enum Whose { Shared, Mine, SomeoneElses }

    // ---- The Library list ----

    [Fact]
    public async Task Library_ForAVisitor_HasSharedBooks_ButNobodysOwnBooks()
    {
        var owner = await factory.CreateUserAsync();
        var shared = await AddBookAsync(ownerId: null, "Visitor sees: shared");
        var owned = await AddBookAsync(owner.Id, "Visitor sees: owned");
        using var visitor = factory.CreateClient();

        var books = await LibraryAsync(visitor);

        books.Should().ContainKey(shared).WhoseValue.Should().BeFalse("a visitor owns nothing");
        books.Should().NotContainKey(owned);
    }

    [Fact]
    public async Task Library_ForAUser_HasSharedBooksAndTheirOwn_MarkedMine_ButNotOtherUsersBooks()
    {
        var (client, me) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        var shared = await AddBookAsync(ownerId: null, "User sees: shared");
        var mine = await AddBookAsync(me.Id, "User sees: mine");
        var theirs = await AddBookAsync(someoneElse.Id, "User sees: theirs");

        var books = await LibraryAsync(client);

        books.Should().ContainKey(shared).WhoseValue.Should().BeFalse();
        books.Should().ContainKey(mine).WhoseValue.Should().BeTrue();
        books.Should().NotContainKey(theirs);
    }

    [Theory]
    [InlineData(Whose.Shared, BookStatusEnum.Removed)]
    [InlineData(Whose.Mine, BookStatusEnum.Removed)]
    [InlineData(Whose.Mine, BookStatusEnum.Lost)]
    public async Task Library_HidesBooksAnAdminMarkedLostOrRemoved_EvenFromTheirOwner(Whose whose, BookStatusEnum adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(whose == Whose.Mine ? me.Id : null, "Taken down", adminStatus);

        var books = await LibraryAsync(client);

        books.Should().NotContainKey(bookId);
    }

    // ---- Favorites ----

    [Theory]
    [InlineData(Whose.Shared)]
    [InlineData(Whose.Mine)]
    public async Task Favorite_ABookICanSee_Works(Whose whose)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(await OwnerForAsync(whose, me.Id), "Favorite me");

        var response = await client.PutAsync(FavoriteUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        (await FavoritesAsync(me.Id, bookId)).Should().Be(1);
    }

    [Theory]
    [InlineData(Whose.SomeoneElses, null)]
    [InlineData(Whose.Shared, BookStatusEnum.Removed)]
    [InlineData(Whose.Mine, BookStatusEnum.Removed)]
    public async Task Favorite_ABookICantSee_Is404_AndSavesNothing(Whose whose, BookStatusEnum? adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(await OwnerForAsync(whose, me.Id), "Can't favorite", adminStatus);

        var response = await client.PutAsync(FavoriteUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await FavoritesAsync(me.Id, bookId)).Should().Be(0);
    }

    // ---- A book's reviews ----

    [Theory]
    [InlineData(Whose.Shared)]
    [InlineData(Whose.Mine)]
    public async Task BookReviews_OfABookICanSee_Work(Whose whose)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(await OwnerForAsync(whose, me.Id), "Read my reviews");

        var response = await client.GetAsync(BookReviewsUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(Whose.SomeoneElses, null)]
    [InlineData(Whose.Shared, BookStatusEnum.Removed)]
    [InlineData(Whose.Mine, BookStatusEnum.Removed)]
    public async Task BookReviews_OfABookICantSee_Are404(Whose whose, BookStatusEnum? adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(await OwnerForAsync(whose, me.Id), "Hidden reviews", adminStatus);

        var response = await client.GetAsync(BookReviewsUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BookReviews_OfSomeonesOwnBook_Are404_ForAVisitor()
    {
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(owner.Id, "Private shelf");
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(BookReviewsUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(Whose.Shared)]
    [InlineData(Whose.Mine)]
    public async Task NewReview_OnABookICanSee_Works(Whose whose)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(await OwnerForAsync(whose, me.Id), "Review me");

        var response = await client.PostAsJsonAsync(NewReviewUrl, ReviewBody(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        (await ReviewsAsync(me.Id, bookId)).Should().Be(1);
    }

    [Theory]
    [InlineData(Whose.SomeoneElses, null)]
    [InlineData(Whose.Shared, BookStatusEnum.Removed)]
    [InlineData(Whose.Mine, BookStatusEnum.Removed)]
    public async Task NewReview_OnABookICantSee_Is404_AndSavesNothing(Whose whose, BookStatusEnum? adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(await OwnerForAsync(whose, me.Id), "Can't review", adminStatus);

        var response = await client.PostAsJsonAsync(NewReviewUrl, ReviewBody(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReviewsAsync(me.Id, bookId)).Should().Be(0);
    }

    // ---- Helpers ----

    // The API replaces userId with the logged-in user (see ReviewOwnershipTests)
    private static object ReviewBody(int bookId) =>
        new { userId = "replaced-by-the-api", bookId, reviewHeader = "Header", reviewText = "Text", ratingItem = new { rating = 7 } };

    private async Task<string?> OwnerForAsync(Whose whose, string myId) => whose switch
    {
        Whose.Shared => null,
        Whose.Mine => myId,
        _ => (await factory.CreateUserAsync()).Id,
    };

    private Task<int> AddBookAsync(string? ownerId, string title, BookStatusEnum? adminStatus = null) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title, ownerId == null ? BookTypeEnums.EBook : BookTypeEnums.Paperback);
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

    // Every book in the list: its id and its isMine flag
    private static async Task<Dictionary<int, bool>> LibraryAsync(HttpClient client)
    {
        var response = await client.GetAsync(LibraryUrl, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("result").EnumerateArray().ToDictionary(
            b => b.GetProperty("bookId").GetInt32(),
            b => b.TryGetProperty("isMine", out var mine) && mine.GetBoolean());
    }

    private Task<int> FavoritesAsync(string userId, int bookId) => factory.WithDbAsync(db =>
        db.FavoriteItems.CountAsync(f => f.UserId == userId && f.BookId == bookId, Ct));

    private Task<int> ReviewsAsync(string userId, int bookId) => factory.WithDbAsync(db =>
        db.ReviewItems.CountAsync(r => r.UserId == userId && r.BookId == bookId, Ct));
}
