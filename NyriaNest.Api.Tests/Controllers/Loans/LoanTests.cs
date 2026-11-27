using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// Security finding 1 (#48): listing loans acts only as the logged-in user (taken from the
/// token, not the URL or body). The self-service borrow went with the shared library's
/// physical books (ADR 0004, removed in #85); friends ask through api/borrow-requests.
/// </summary>
public class LoanTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CheckedOut_ListsOnlyMyLoans()
    {
        var (client, me) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();
        var myBook = await AddBookAsync();
        var theirBook = await AddBookAsync();
        await AddLoanAsync(myBook, me.Id);
        await AddLoanAsync(theirBook, other.Id);

        var response = await client.GetAsync("api/UserActions/checkedout", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain($"\"bookId\": {myBook}").And.NotContain($"\"bookId\": {theirBook}");
    }

    // ---- #85 9a: My loans (#65) shows whose book it is and a return waiting for confirmation ----

    [Fact]
    public async Task CheckedOut_shows_the_owner_and_the_reported_return()
    {
        var (client, me) = await factory.LoginAsAsync();
        var anna = await factory.CreateUserAsync();
        var bo = await factory.CreateUserAsync();
        var reported = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        var annasBook = await AddBookAsync(ownerId: anna.Id);
        var bosBook = await AddBookAsync(ownerId: bo.Id);
        await AddLoanAsync(annasBook, me.Id, reportedReturned: reported);
        await AddLoanAsync(bosBook, me.Id);

        var loans = await MyLoansAsync(client);

        loans[annasBook].GetProperty("ownerUserName").GetString().Should().Be(anna.UserName);
        loans[annasBook].GetProperty("reportedReturnedDate").GetDateTime().Should().Be(reported);
        loans[bosBook].GetProperty("ownerUserName").GetString().Should().Be(bo.UserName);
        loans[bosBook].GetProperty("reportedReturnedDate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CheckedOut_has_no_owner_for_a_shared_library_book()
    {
        var (client, me) = await factory.LoginAsAsync();
        var sharedBook = await AddBookAsync();
        await AddLoanAsync(sharedBook, me.Id);

        var loans = await MyLoansAsync(client);

        loans[sharedBook].GetProperty("ownerUserName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<Dictionary<int, JsonElement>> MyLoansAsync(HttpClient client)
    {
        var response = await client.GetAsync("api/UserActions/checkedout", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("result").EnumerateArray()
            .ToDictionary(l => l.GetProperty("bookId").GetInt32(), l => l.Clone());
    }

    private Task<int> AddBookAsync(string? ownerId = null) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book($"Book {Guid.NewGuid():N}"[..13]);
        book.OwnerId = ownerId;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddLoanAsync(int bookId, string userId, DateTime? reportedReturned = null) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new()
        {
            BookId = bookId,
            UserId = userId,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(14),
            ReportedReturnedDate = reportedReturned,
        });
        var book = await db.Books.SingleAsync(b => b.BookId == bookId, Ct);
        book.BookStatus = BookStatusEnum.CheckedOut;
        await db.SaveChangesAsync(Ct);
    });
}
