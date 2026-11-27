using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #83 step 4b: the old borrow flow (checkout) stays for the shared library, but refuses books
/// someone owns, even for the owner: an owner lends through api/my/books, and a friend asks
/// through api/borrow-requests (#85). Reserving a book someone owns is open to friends since
/// #85 (FriendQueueTests); for anyone else it looks missing, and the owner can't queue for
/// their own book. Return and unreserve aren't guarded: they only end what someone already
/// has, and blocking them would trap leftover data.
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

    // #85: was 409 for both. A book you can't see looks missing (404), as everywhere else;
    // your own book is a plain no (400). Friends who are shared with may reserve (FriendQueueTests).
    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]   // someone who isn't shared with
    [InlineData(true, HttpStatusCode.BadRequest)]  // the owner themselves
    public async Task Reserve_ABookSomeoneOwns_IsRefused_AndChangesNothing(bool asTheOwner, HttpStatusCode expected)
    {
        var (client, me) = await factory.LoginAsAsync();
        var ownerId = asTheOwner ? me.Id : (await factory.CreateUserAsync()).Id;
        var bookId = await AddOwnedBookAsync(ownerId);

        var response = await client.PutAsync($"api/UserActions/reserve/{bookId}", null, Ct);

        response.StatusCode.Should().Be(expected);
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
