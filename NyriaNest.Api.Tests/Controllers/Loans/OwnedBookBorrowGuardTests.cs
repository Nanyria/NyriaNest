using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #83 step 4b: the old borrow flow (checkout and reservations) stays for the shared library
/// until friend loans (#85), but refuses books someone owns, even for the owner: an owner
/// lends through api/my/books. Return and unreserve aren't guarded: they only end what
/// someone already has, and blocking them would trap leftover data.
/// </summary>
public class OwnedBookBorrowGuardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)] // another user
    [InlineData(true)]  // the owner themselves
    public async Task Borrow_ABookSomeoneOwns_Returns409_AndChangesNothing(bool asTheOwner)
    {
        var (client, me) = await factory.LoginAsAsync();
        var ownerId = asTheOwner ? me.Id : (await factory.CreateUserAsync()).Id;
        var bookId = await AddOwnedBookAsync(ownerId);

        var response = await client.PutAsync($"api/UserActions/borrow/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.CheckOutItems.AnyAsync(l => l.BookId == bookId, Ct))).Should().BeFalse();
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
        (await factory.WithDbAsync(db => db.StatusHistoryItems.AnyAsync(h => h.BookId == bookId, Ct))).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserve_ABookSomeoneOwns_Returns409_AndChangesNothing(bool asTheOwner)
    {
        var (client, me) = await factory.LoginAsAsync();
        var ownerId = asTheOwner ? me.Id : (await factory.CreateUserAsync()).Id;
        var bookId = await AddOwnedBookAsync(ownerId);

        var response = await client.PutAsync($"api/UserActions/reserve/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.ReservationItems.AnyAsync(r => r.BookId == bookId, Ct))).Should().BeFalse();
        (await StatusOf(bookId)).Should().Be(BookStatusEnum.Available);
    }

    // ---- Helpers ----

    private Task<int> AddOwnedBookAsync(string ownerId) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book("Someone's own book");
        book.OwnerId = ownerId;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<BookStatusEnum> StatusOf(int bookId) => factory.WithDbAsync(db =>
        db.Books.Where(b => b.BookId == bookId).Select(b => b.BookStatus).SingleAsync(Ct));
}
