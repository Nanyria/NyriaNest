using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.ReadingList;

/// <summary>
/// Pins down how the reading list ("favorites") behaves today, through
/// GET api/UserActions/favorites, PUT favorite/{bookId} and DELETE unfavorite/{bookId},
/// so #116 can move the code into its own service without changing it.
/// </summary>
public class ReadingListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ListUrl = "api/UserActions/favorites";
    private static string AddUrl(int bookId) => $"api/UserActions/favorite/{bookId}";
    private static string RemoveUrl(int bookId) => $"api/UserActions/unfavorite/{bookId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Add_PutsTheBookOnMyList_AndReturns201()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync("Dragon Tales");

        var response = await client.PutAsync(AddUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ResultOf(response)).GetProperty("bookId").GetInt32().Should().Be(bookId);
        (await BooksOnListOf(me.Id)).Should().Equal(bookId);
    }

    [Fact]
    public async Task Add_TheSameBookTwice_Returns409_AndKeepsOneEntry()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();
        await client.PutAsync(AddUrl(bookId), null, Ct);

        var again = await client.PutAsync(AddUrl(bookId), null, Ct);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BooksOnListOf(me.Id)).Should().Equal(bookId);
    }

    [Fact]
    public async Task Add_ABookThatDoesntExist_Returns404()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsync(AddUrl(999999), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BooksOnListOf(me.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_ShowsMyBooks_WithTheirTitles_AndOnlyMine()
    {
        var (client, _) = await factory.LoginAsAsync();
        var (otherClient, _) = await factory.LoginAsAsync();
        var mine = await AddBookAsync("Mine");
        var theirs = await AddBookAsync("Theirs");
        await client.PutAsync(AddUrl(mine), null, Ct);
        await otherClient.PutAsync(AddUrl(theirs), null, Ct);

        var response = await client.GetAsync(ListUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await ResultOf(response)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("bookId").GetInt32()).Should().Equal(mine);
        items.Single().GetProperty("book").GetProperty("title").GetString().Should().Be("Mine");
    }

    // Today an empty list is a 404, not an empty 200. Odd, but pinned here so the move
    // doesn't change it by accident; changing it is a separate decision.
    [Fact]
    public async Task List_WhenEmpty_Returns404_NoFavoritesFound()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.GetAsync(ListUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("No favorites found for this user.");
    }

    [Fact]
    public async Task Remove_TakesTheBookOffMyList_AndReturns200()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();
        await client.PutAsync(AddUrl(bookId), null, Ct);

        var response = await client.DeleteAsync(RemoveUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BooksOnListOf(me.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_ABookNotOnMyList_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.DeleteAsync(RemoveUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Remove_OnlyTouchesMyList_NotSomeoneElses()
    {
        var (client, _) = await factory.LoginAsAsync();
        var (otherClient, other) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();
        await otherClient.PutAsync(AddUrl(bookId), null, Ct);

        var response = await client.DeleteAsync(RemoveUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BooksOnListOf(other.Id)).Should().Equal(bookId);
    }

    // ---- helpers ----

    private Task<int> AddBookAsync(string title = "Reading list book") => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<List<int>> BooksOnListOf(string userId) => factory.WithDbAsync(db =>
        db.FavoriteItems.Where(f => f.UserId == userId).Select(f => f.BookId).ToListAsync(Ct));

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("result").Clone();
    }
}
