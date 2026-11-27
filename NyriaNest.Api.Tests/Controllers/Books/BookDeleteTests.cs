using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #39 part B: DELETE api/Book/{bookId}. A book someone has borrowed is deleted only after a
/// confirm (#85); a book with only reservations can be, and its reservations go with it
/// (decided in #39). Who is told, and what they're told, is tested in
/// BookRemovedNotificationTests (#75, #83).
/// </summary>
public class BookDeleteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_ABookWithReservationsButNoLoan_RemovesTheBookAndItsReservations()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var first = await factory.CreateUserAsync();
        var second = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, first.Id, daysAgo: 2);
        await AddReservationAsync(bookId, second.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await factory.WithDbAsync(db => db.Books.AnyAsync(b => b.BookId == bookId, Ct))).Should().BeFalse();
        (await ReservationCountOf(bookId)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_OnlyRemovesThatBooksReservations()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var deleted = await AddBookAsync(BookStatusEnum.Reserved);
        var kept = await AddBookAsync(BookStatusEnum.Reserved);
        await AddReservationAsync(deleted, reader.Id, daysAgo: 1);
        await AddReservationAsync(kept, reader.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{deleted}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await ReservationCountOf(kept)).Should().Be(1);
    }

    [Fact]
    public async Task Delete_ACheckedOutBook_WithoutConfirming_Returns409_AndKeepsEverything()
    {
        // Deleting it would end someone's loan, so it asks first (#85: was "Return this book before
        // deleting it."). Deleting after the confirm is in FriendBookDeleteTests.
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var holder = await factory.CreateUserAsync();
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(BookStatusEnum.CheckedOut);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = bookId, UserId = holder.Id, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            await db.SaveChangesAsync(Ct);
        });
        await AddReservationAsync(bookId, reader.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(holder.UserName).And.Contain("Delete it anyway?");
        (await factory.WithDbAsync(db => db.Books.AnyAsync(b => b.BookId == bookId, Ct))).Should().BeTrue();
        (await ReservationCountOf(bookId)).Should().Be(1);
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(BookStatusEnum status) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(status: status);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddReservationAsync(int bookId, string userId, int daysAgo) => factory.WithDbAsync(async db =>
    {
        db.ReservationItems.Add(new() { BookId = bookId, UserId = userId, ReservationDate = DateTime.UtcNow.AddDays(-daysAgo) });
        await db.SaveChangesAsync(Ct);
    });

    private Task<int> ReservationCountOf(int bookId) => factory.WithDbAsync(db =>
        db.ReservationItems.CountAsync(r => r.BookId == bookId, Ct));
}
