using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Pins down how reservations behave today, through GET api/UserActions/reserved,
/// PUT reserve/{bookId} and PUT unreserve/{bookId}, so #116 can move the code into
/// ReservationService without changing it. The queue rules for borrowing are in
/// <see cref="LoanRulesTests"/>.
/// </summary>
public class ReservationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ListUrl = "api/UserActions/reserved";
    private static string ReserveUrl(int bookId) => $"api/UserActions/reserve/{bookId}";
    private static string UnreserveUrl(int bookId) => $"api/UserActions/unreserve/{bookId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Reserving ----

    [Fact]
    public async Task Reserve_AnAvailableBook_MakesItReserved_AndICanHaveItToday()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Reserved);
        (await ReservationOf(bookId, me.Id))!.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await AvailabilityDateOf(bookId)).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Reserve_ABookOnLoan_KeepsItCheckedOut_AndIWaitUntilTheDueDate()
    {
        var holder = await factory.CreateUserAsync();
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, holder.Id, dueInDays: 10);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.CheckedOut);
        var dueDate = DateTime.UtcNow.AddDays(10);
        (await ReservationOf(bookId, me.Id))!.AvailabilityDate.Should().BeCloseTo(dueDate, TimeSpan.FromMinutes(1));
        (await AvailabilityDateOf(bookId)).Should().BeCloseTo(dueDate, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Reserve_BehindSomeoneElse_IWaitOneLoanPeriodLonger()
    {
        var first = await factory.CreateUserAsync();
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, first.Id, daysAgo: 1);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationOf(bookId, first.Id))!.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await ReservationOf(bookId, me.Id))!.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
        (await AvailabilityDateOf(bookId)).Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Reserve_TheSameBookTwice_Returns409_AndKeepsOneReservation()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();
        await client.PutAsync(ReserveUrl(bookId), null, Ct);

        var again = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync(Ct)).Should().Contain("Book already reserved by user.");
        (await ReservationCountOf(bookId, me.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Reserve_ABookIHave_Returns409()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, me.Id);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Book already checked out by user.");
        (await ReservationCountOf(bookId, me.Id)).Should().Be(0);
    }

    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public async Task Reserve_ABookMarkedLostOrRemoved_Returns409(BookStatusEnum adminStatus)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(adminStatus, adminStatus);

        var response = await client.PutAsync(ReserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("This book isn't available.");
        (await ReservationCountOf(bookId, me.Id)).Should().Be(0);
        (await StatusOf(bookId)).Should().Be(adminStatus);
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
    public async Task Unreserve_MovesTheNextInLineUp_SoTheyCanHaveItToday()
    {
        var (client, me) = await factory.LoginAsAsync();
        var next = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, me.Id, daysAgo: 2, availableInDays: 0);
        await AddReservationAsync(bookId, next.Id, daysAgo: 1, availableInDays: 30);

        var response = await client.PutAsync(UnreserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationOf(bookId, next.Id))!.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Unreserve_ABookINeverReserved_Returns409()
    {
        var (client, _) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PutAsync(UnreserveUrl(bookId), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Book not reserved by user.");
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
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
        var (client, me) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();
        var older = await AddBookAsync(title: "Older");
        var newer = await AddBookAsync(title: "Newer");
        var theirs = await AddBookAsync(title: "Theirs");
        await AddReservationAsync(older, me.Id, daysAgo: 3);
        await AddReservationAsync(newer, me.Id, daysAgo: 1);
        await AddReservationAsync(theirs, other.Id, daysAgo: 2);

        var response = await client.GetAsync(ListUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await ResultOf(response)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("bookId").GetInt32()).Should().Equal(newer, older);
        items.Select(i => i.GetProperty("book").GetProperty("title").GetString()).Should().Equal("Newer", "Older");
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

    private Task<int> AddBookAsync(BookStatusEnum status = BookStatusEnum.Available, BookStatusEnum? adminStatus = null, string title = "Test book") =>
        factory.WithDbAsync(async db =>
        {
            var book = TestData.Book(title: title, status: status);
            book.AdminStatus = adminStatus;
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });

    private Task AddLoanAsync(int bookId, string userId, int dueInDays = 14) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new() { BookId = bookId, UserId = userId, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(dueInDays) });
        await db.SaveChangesAsync(Ct);
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
