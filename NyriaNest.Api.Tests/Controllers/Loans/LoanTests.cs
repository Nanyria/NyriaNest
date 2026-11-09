using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// Security finding 1 (#48): borrowing and listing loans act only as the logged-in user
/// (taken from the token, not the URL or body).
/// </summary>
public class LoanTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Borrow_LendsTheBookToTheTokensUser()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PutAsync($"api/UserActions/borrow/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var borrower = await factory.WithDbAsync(db =>
            db.CheckOutItems.Where(c => c.BookId == bookId).Select(c => c.UserId).SingleAsync(Ct));
        borrower.Should().Be(me.Id);
    }

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

    private Task<int> AddBookAsync() => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book($"Book {Guid.NewGuid():N}"[..13]);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddLoanAsync(int bookId, string userId) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new()
        {
            BookId = bookId,
            UserId = userId,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(14),
        });
        var book = await db.Books.SingleAsync(b => b.BookId == bookId, Ct);
        book.BookStatus = BookStatusEnum.CheckedOut;
        await db.SaveChangesAsync(Ct);
    });
}
