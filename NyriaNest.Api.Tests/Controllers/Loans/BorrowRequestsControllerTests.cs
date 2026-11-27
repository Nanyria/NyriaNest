using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #85 step 3: the api/borrow-requests endpoints. The rules are in BorrowRequestServiceTests;
/// these check the routes, that "me" comes from the token (never the body), and the status codes.
/// </summary>
public class BorrowRequestsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Url = "api/borrow-requests";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Asking_without_a_token_is_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Url, new { bookId = 1 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ask_then_accept_lends_the_book_to_the_friend_from_the_token()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var (friendClient, friend) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        var bookId = await SharedBookAsync(owner, friend);

        // A borrowerId in the body is ignored: the borrower is whoever is logged in
        var asked = await friendClient.PostAsJsonAsync(Url, new { bookId, borrowerId = someoneElse.Id }, Ct);
        asked.StatusCode.Should().Be(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync(Ct));
        var request = await ResultOf(asked);
        request.GetProperty("borrowerUserName").GetString().Should().Be(friend.UserName);
        request.GetProperty("status").GetInt32().Should().Be((int)BorrowRequestStatus.Requested);
        var id = request.GetProperty("id").GetInt32();

        var accepted = await ownerClient.PutAsync($"{Url}/{id}/accept", null, Ct);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
        (await ResultOf(accepted)).GetProperty("status").GetInt32().Should().Be((int)BorrowRequestStatus.Lent);
        var loan = await factory.WithDbAsync(db => db.CheckOutItems.SingleAsync(l => l.BookId == bookId, Ct));
        loan.UserId.Should().Be(friend.Id);
    }

    [Fact]
    public async Task Decline_and_cancel_have_their_own_routes()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var (friendClient, friend) = await factory.LoginAsAsync();
        var bookId = await SharedBookAsync(owner, friend);

        var first = (await ResultOf(await friendClient.PostAsJsonAsync(Url, new { bookId }, Ct))).GetProperty("id").GetInt32();
        var declined = await ownerClient.PutAsync($"{Url}/{first}/decline", null, Ct);
        declined.StatusCode.Should().Be(HttpStatusCode.OK, await declined.Content.ReadAsStringAsync(Ct));

        var second = (await ResultOf(await friendClient.PostAsJsonAsync(Url, new { bookId }, Ct))).GetProperty("id").GetInt32();
        var cancelled = await friendClient.PutAsync($"{Url}/{second}/cancel", null, Ct);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Ct));
        (await ResultOf(cancelled)).GetProperty("status").GetInt32().Should().Be((int)BorrowRequestStatus.Cancelled);
    }

    [Fact]
    public async Task Answering_someone_elses_request_is_404()
    {
        var (_, owner) = await factory.LoginAsAsync();
        var (friendClient, friend) = await factory.LoginAsAsync();
        var bookId = await SharedBookAsync(owner, friend);
        var id = (await ResultOf(await friendClient.PostAsJsonAsync(Url, new { bookId }, Ct))).GetProperty("id").GetInt32();

        // The friend can't accept their own request
        var response = await friendClient.PutAsync($"{Url}/{id}/accept", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Helpers ----

    /// <summary>A paperback of <paramref name="owner"/>'s, shared with <paramref name="friend"/>.</summary>
    private Task<int> SharedBookAsync(User owner, User friend) => factory.WithDbAsync(async db =>
    {
        db.Friendships.Add(new Friendship
        {
            RequesterId = owner.Id,
            AddresseeId = friend.Id,
            Status = FriendshipStatus.Accepted,
            AcceptedAt = DateTime.UtcNow,
            RequesterSharesPhysicalLibrary = true,
        });
        var book = TestData.Book("A friend's paperback");
        book.OwnerId = owner.Id;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("result").Clone();
    }
}
