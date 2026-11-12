using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #75: when an admin deletes a book with reservations, everyone in the queue
/// gets one notification that names the book. Nobody else is told, and a
/// delete that is refused tells no one.
/// </summary>
public class BookRemovedNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_ABookWithReservations_NotifiesEachReaderOnce_WithTheTitle()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var first = await factory.CreateUserAsync();
        var second = await factory.CreateUserAsync();
        var bystander = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("The Wicked King", BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, first.Id, daysAgo: 2);
        await AddReservationAsync(bookId, second.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        foreach (var reader in new[] { first.Id, second.Id })
        {
            var messages = await MessagesOf(reader);
            messages.Should().ContainSingle();
            messages[0].Should().Contain("The Wicked King");
        }
        (await MessagesOf(bystander.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ACheckedOutBook_Returns409_AndNotifiesNoOne()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var holder = await factory.CreateUserAsync();
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("A Book On Loan", BookStatusEnum.CheckedOut);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = bookId, UserId = holder.Id, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            await db.SaveChangesAsync(Ct);
        });
        await AddReservationAsync(bookId, reader.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MessagesOf(holder.Id)).Should().BeEmpty();
        (await MessagesOf(reader.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_AReaderWithTwoReservationsOnTheBook_GetsOneNotification()
    {
        // Nothing in the DB stops a double reservation, so the service has to de-duplicate
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Reserved Twice", BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, reader.Id, daysAgo: 2);
        await AddReservationAsync(bookId, reader.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(reader.Id)).Should().ContainSingle()
            .Which.Should().Contain("Reserved Twice");
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(string title, BookStatusEnum status) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title: title, status: status);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddReservationAsync(int bookId, string userId, int daysAgo) => factory.WithDbAsync(async db =>
    {
        db.ReservationItems.Add(new() { BookId = bookId, UserId = userId, ReservationDate = DateTime.UtcNow.AddDays(-daysAgo) });
        await db.SaveChangesAsync(Ct);
    });

    private Task<List<string>> MessagesOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId).Select(n => n.Message).ToListAsync(Ct));
}
