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

    // ---- 9a: the owner's incoming requests ----

    [Fact]
    public async Task Incoming_without_a_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync($"{Url}/incoming", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Incoming_lists_the_open_requests_for_my_books_longest_waiting_first()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var anna = await factory.CreateUserAsync();
        var bo = await factory.CreateUserAsync();
        var otherOwner = await factory.CreateUserAsync();
        var now = DateTime.UtcNow;

        var mineId = await factory.WithDbAsync(async db =>
        {
            var mine = TestData.Book("My paperback");
            mine.OwnerId = owner.Id;
            mine.CoverImagePath = "covers/mine.jpg";
            var others = TestData.Book("Someone else's paperback");
            others.OwnerId = otherOwner.Id;
            db.Books.AddRange(mine, others);
            await db.SaveChangesAsync(Ct);

            db.BorrowRequests.AddRange(
                // Bo asked first, but only got their turn when the queue opened: still waiting longest
                Request(mine.BookId, anna.Id, BorrowRequestStatus.Requested, requestedAt: now.AddDays(-2)),
                Request(mine.BookId, bo.Id, BorrowRequestStatus.Requested, requestedAt: now.AddDays(-10), availableAt: now.AddDays(-1)),
                // Finished requests aren't incoming
                Request(mine.BookId, anna.Id, BorrowRequestStatus.Declined, requestedAt: now.AddDays(-20)),
                Request(mine.BookId, bo.Id, BorrowRequestStatus.Expired, requestedAt: now.AddDays(-40)),
                // Nor is a request for someone else's book
                Request(others.BookId, anna.Id, BorrowRequestStatus.Requested, requestedAt: now.AddDays(-30)));
            await db.SaveChangesAsync(Ct);
            return mine.BookId;
        });

        var response = await ownerClient.GetAsync($"{Url}/incoming", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var requests = (await ResultOf(response)).EnumerateArray().ToList();
        requests.Select(r => r.GetProperty("borrowerUserName").GetString())
            .Should().Equal(bo.UserName, anna.UserName);
        requests.Should().OnlyContain(r => r.GetProperty("bookId").GetInt32() == mineId);
        requests.Should().OnlyContain(r => r.GetProperty("status").GetInt32() == (int)BorrowRequestStatus.Requested);

        var first = requests[0];
        first.GetProperty("bookTitle").GetString().Should().Be("My paperback");
        first.GetProperty("coverImagePath").GetString().Should().Be("covers/mine.jpg");
        first.GetProperty("ownerUserName").GetString().Should().Be(owner.UserName);
        first.GetProperty("requestedAt").GetDateTime().Should().BeCloseTo(now.AddDays(-10), TimeSpan.FromSeconds(1));
        first.GetProperty("availableAt").GetDateTime().Should().BeCloseTo(now.AddDays(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Incoming_leaves_out_the_requests_I_made_myself()
    {
        var (_, owner) = await factory.LoginAsAsync();
        var (friendClient, friend) = await factory.LoginAsAsync();
        var bookId = await SharedBookAsync(owner, friend);
        (await friendClient.PostAsJsonAsync(Url, new { bookId }, Ct)).EnsureSuccessStatusCode();

        // The friend asked, but it's not their book: nothing for them to answer
        var response = await friendClient.GetAsync($"{Url}/incoming", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ResultOf(response)).GetArrayLength().Should().Be(0);
    }

    // ---- 9a: the friend's own requests ----

    [Fact]
    public async Task Mine_without_a_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync($"{Url}/mine", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mine_lists_my_open_requests_with_the_owner_longest_waiting_first()
    {
        var (friendClient, friend) = await factory.LoginAsAsync();
        var anna = await factory.CreateUserAsync();
        var bo = await factory.CreateUserAsync();
        var someoneElse = await factory.CreateUserAsync();
        var now = DateTime.UtcNow;

        await factory.WithDbAsync(async db =>
        {
            var annas = TestData.Book("Anna's paperback");
            annas.OwnerId = anna.Id;
            annas.CoverImagePath = "covers/annas.jpg";
            var bos = TestData.Book("Bo's hardcover", BookTypeEnums.Hardcover);
            bos.OwnerId = bo.Id;
            db.Books.AddRange(annas, bos);
            await db.SaveChangesAsync(Ct);

            db.BorrowRequests.AddRange(
                Request(annas.BookId, friend.Id, BorrowRequestStatus.Requested, requestedAt: now.AddDays(-2)),
                Request(bos.BookId, friend.Id, BorrowRequestStatus.Requested, requestedAt: now.AddDays(-9), availableAt: now.AddDays(-1)),
                // Finished ones have their own place (My loans, My reservations, notifications)
                Request(annas.BookId, friend.Id, BorrowRequestStatus.Declined, requestedAt: now.AddDays(-20)),
                Request(bos.BookId, friend.Id, BorrowRequestStatus.Queued, requestedAt: now.AddDays(-30)),
                // Someone else's request for the same book isn't mine
                Request(annas.BookId, someoneElse.Id, BorrowRequestStatus.Requested, requestedAt: now.AddDays(-40)));
            await db.SaveChangesAsync(Ct);
        });

        var response = await friendClient.GetAsync($"{Url}/mine", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var requests = (await ResultOf(response)).EnumerateArray().ToList();
        requests.Select(r => r.GetProperty("bookTitle").GetString())
            .Should().Equal("Bo's hardcover", "Anna's paperback");
        requests.Select(r => r.GetProperty("ownerUserName").GetString())
            .Should().Equal(bo.UserName, anna.UserName);
        requests.Should().OnlyContain(r => r.GetProperty("borrowerId").GetString() == friend.Id);
        requests.Should().OnlyContain(r => r.GetProperty("status").GetInt32() == (int)BorrowRequestStatus.Requested);
        requests[1].GetProperty("coverImagePath").GetString().Should().Be("covers/annas.jpg");
        requests[0].GetProperty("requestedAt").GetDateTime().Should().BeCloseTo(now.AddDays(-9), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Mine_leaves_out_requests_for_my_own_books()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var (friendClient, friend) = await factory.LoginAsAsync();
        var bookId = await SharedBookAsync(owner, friend);
        (await friendClient.PostAsJsonAsync(Url, new { bookId }, Ct)).EnsureSuccessStatusCode();

        // The owner was asked, but didn't ask: nothing of theirs is waiting
        var response = await ownerClient.GetAsync($"{Url}/mine", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ResultOf(response)).GetArrayLength().Should().Be(0);
    }

    // ---- Helpers ----

    private static BorrowRequest Request(int bookId, string borrowerId, BorrowRequestStatus status,
        DateTime requestedAt, DateTime? availableAt = null) => new()
    {
        BookId = bookId,
        BorrowerId = borrowerId,
        Status = status,
        RequestedAt = requestedAt,
        AvailableAt = availableAt ?? requestedAt,
        AnsweredAt = status == BorrowRequestStatus.Requested ? null : requestedAt.AddDays(1),
    };

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
