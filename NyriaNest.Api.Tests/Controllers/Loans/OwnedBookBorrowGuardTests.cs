using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Loans;

/// <summary>
/// #83 step 4b, #85: reserving a book someone owns. Friends who are shared with may (FriendQueueTests);
/// for anyone else it looks missing, and the owner can't queue for their own book. Unreserve
/// isn't guarded: it only ends what someone already has. (The old self-service borrow this
/// file also guarded was removed in #85: an owner lends through api/my/books, and a friend asks
/// through api/borrow-requests.)
/// </summary>
public class OwnedBookBorrowGuardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
