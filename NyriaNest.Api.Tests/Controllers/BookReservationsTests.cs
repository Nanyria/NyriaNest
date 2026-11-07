using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using NyriaNest.Domain.Entities;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #71: who may see a book's reservation queue.
/// - Readers: only their own place, as MyQueuePosition in the library list.
/// - Admins: the whole queue at GET api/Book/{bookId}/reservations (BookReservationDto).
/// - The public GET api/Library/reservations/{bookId} is gone.
/// </summary>
public class BookReservationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The old public endpoint ----

    [Fact]
    public async Task OldPublicQueueEndpoint_IsGone()
    {
        // It answered anyone, without a login, with every reader's user id
        using var anonymous = factory.CreateClient();
        var (bookId, _, _) = await BookWithAQueueAsync();

        var response = await anonymous.GetAsync($"api/Library/reservations/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Readers: their own place, in the library list ----

    [Fact]
    public async Task LibraryList_ShowsEachReaderTheirOwnPlaceInTheQueue()
    {
        var (bookId, first, second) = await BookWithAQueueAsync();

        (await MyQueuePositionAsync(first, bookId)).Should().Be(1);
        (await MyQueuePositionAsync(second, bookId)).Should().Be(2);
    }

    [Fact]
    public async Task LibraryList_MyQueuePosition_IsNull_ForABookIHaventReserved()
    {
        var (bookId, _, _) = await BookWithAQueueAsync();
        var outsider = await factory.CreateUserAsync(Roles.User);

        (await MyQueuePositionAsync(outsider, bookId)).Should().BeNull();
    }

    [Fact]
    public async Task LibraryList_NeverContainsAnotherReadersId()
    {
        var (_, first, second) = await BookWithAQueueAsync();
        using var client = await ClientForAsync(second);

        var body = await (await client.GetAsync("api/Library", Ct)).Content.ReadAsStringAsync(Ct);

        body.Should().NotContain(first.Id);
    }

    // ---- Admins: the whole queue ----

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.User)]
    public async Task AdminQueue_IsForAdminsOnly(string? role)
    {
        var (bookId, _, _) = await BookWithAQueueAsync();
        using var client = role is null ? factory.CreateClient() : (await factory.LoginAsAsync(role)).Item1;

        var response = await client.GetAsync($"api/Book/{bookId}/reservations", Ct);

        response.StatusCode.Should().Be(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminQueue_ListsTheWholeQueue_OldestFirst_WithTheReadersNames()
    {
        var (bookId, first, second) = await BookWithAQueueAsync();
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"api/Book/{bookId}/reservations", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var queue = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray();
        queue.Select(e => (string?)e!["userId"]).Should().Equal(first.Id, second.Id);
        queue.Select(e => (int?)e!["queuePosition"]).Should().Equal(1, 2);
        ((string?)queue[0]!["userName"]).Should().Be(first.UserName);
        ((string?)queue[0]!["fullName"]).Should().Be($"{first.FirstName} {first.LastName}");
        queue[0]!["reservationId"].Should().NotBeNull();
        queue[0]!["reservationDate"].Should().NotBeNull();
    }

    [Fact]
    public async Task AdminQueue_ReturnsOnlyTheQueueFields()
    {
        // Guards the response: adding a field (an email, the whole book) to
        // BookReservationDto should be a deliberate change made here as well
        var (bookId, _, _) = await BookWithAQueueAsync();
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"api/Book/{bookId}/reservations", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray()[0]!.AsObject();
        entry.Select(p => p.Key).Should().BeEquivalentTo(
            "reservationId", "userId", "userName", "fullName", "reservationDate", "availabilityDate", "queuePosition");
    }

    [Fact]
    public async Task AdminQueue_OfAnUnknownBook_Returns404_SayingTheBookWasNotFound()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync("api/Book/999999/reservations", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Book 999999 was not found.");
    }

    // ---- Helpers ----

    /// <summary>
    /// A checked-out book with two readers queued: <c>first</c> reserved two days
    /// ago, <c>second</c> one day ago, so first is next in line.
    /// </summary>
    private async Task<(int BookId, User First, User Second)> BookWithAQueueAsync()
    {
        var borrower = await factory.CreateUserAsync(Roles.User);
        var first = await factory.CreateUserAsync(Roles.User);
        var second = await factory.CreateUserAsync(Roles.User);

        var bookId = await factory.WithDbAsync(async db =>
        {
            var book = TestData.Book(status: BookStatusEnum.Reserved);
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = bookId, UserId = borrower.Id, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            // Added newest first, so the order in the database isn't the queue order
            db.ReservationItems.Add(new() { BookId = bookId, UserId = second.Id, ReservationDate = DateTime.UtcNow.AddDays(-1) });
            db.ReservationItems.Add(new() { BookId = bookId, UserId = first.Id, ReservationDate = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync(Ct);
        });
        return (bookId, first, second);
    }

    private async Task<HttpClient> ClientForAsync(User user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.GetTokenAsync(user.UserName!));
        return client;
    }

    private async Task<int?> MyQueuePositionAsync(User user, int bookId)
    {
        using var client = await ClientForAsync(user);
        var response = await client.GetAsync("api/Library", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var books = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray();
        var book = books.Single(b => (int)b!["bookId"]! == bookId)!;
        book.AsObject().ContainsKey("myQueuePosition").Should().BeTrue("the library list should say where I am in the queue");
        return (int?)book["myQueuePosition"];
    }
}
