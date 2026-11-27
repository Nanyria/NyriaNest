using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #85 step 7: a friend's book goes away. A book lent to a friend can be deleted after a confirm,
/// as one lent to a name (#83): the first try is a 409 naming the friend, and confirmLent deletes it
/// with its loan, requests and reservations. Everyone who loses something is told once, naming the
/// biggest thing they lose: the loan, then a reservation, then an open request, then a favorite.
/// Finished requests don't count, and the owner isn't told about their own delete. An admin taking
/// the book down (Lost or Removed) cancels the open requests too.
/// </summary>
public class FriendBookDeleteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Title = "Signe's Moomins";
    private static string OwnerDeleteUrl(int bookId) => $"api/my/books/{bookId}";
    private static string AdminDeleteUrl(int bookId) => $"api/Book/{bookId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The owner deletes a book a friend has ----

    [Fact]
    public async Task Without_confirming_it_asks_first_naming_the_friend_and_changes_nothing()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var borrower = await factory.CreateUserAsync();
        var bookId = await BookLentToAsync(owner, borrower);

        var response = await DeleteAsync(ownerClient, OwnerDeleteUrl(bookId), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain(borrower.UserName).And.Contain("Delete it anyway?");
        (await BookExistsAsync(bookId)).Should().BeTrue();
        (await factory.WithDbAsync(db => db.CheckOutItems.AnyAsync(l => l.BookId == bookId, Ct))).Should().BeTrue();
        (await MessagesOf(borrower.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task When_confirmed_the_book_goes_with_its_loan_requests_and_reservations()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var borrower = await factory.CreateUserAsync();
        var waiting = await factory.CreateUserAsync();
        var bookId = await BookLentToAsync(owner, borrower);
        await ReserveAsync(bookId, waiting);
        await RequestAsync(bookId, waiting, BorrowRequestStatus.Queued); // the record of an earlier round

        var response = await DeleteAsync(ownerClient, OwnerDeleteUrl(bookId), new { confirmLent = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookExistsAsync(bookId)).Should().BeFalse();
        await factory.WithDbAsync(async db =>
        {
            (await db.CheckOutItems.AnyAsync(l => l.BookId == bookId, Ct)).Should().BeFalse();
            (await db.ReservationItems.AnyAsync(r => r.BookId == bookId, Ct)).Should().BeFalse();
            (await db.BorrowRequests.AnyAsync(r => r.BookId == bookId, Ct)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task When_confirmed_the_friend_who_has_it_is_told_their_loan_ended()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var borrower = await factory.CreateUserAsync();
        var bookId = await BookLentToAsync(owner, borrower);

        await DeleteAsync(ownerClient, OwnerDeleteUrl(bookId), new { confirmLent = true });

        (await MessagesOf(borrower.Id)).Should().Equal(
            $"\"{Title}\" was removed by its owner, so your loan of it ended.");
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Everyone_who_loses_something_is_told_once_naming_the_biggest_loss()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var borrower = await factory.CreateUserAsync();   // has it, and favorited it
        var reserver = await factory.CreateUserAsync();   // in the queue, and favorited it
        var favoriter = await factory.CreateUserAsync();  // only favorited it
        var bookId = await BookLentToAsync(owner, borrower);
        await ReserveAsync(bookId, reserver);
        foreach (var user in new[] { borrower, reserver, favoriter, owner })
            await FavoriteAsync(bookId, user);

        await DeleteAsync(ownerClient, OwnerDeleteUrl(bookId), new { confirmLent = true });

        (await MessagesOf(borrower.Id)).Should().Equal($"\"{Title}\" was removed by its owner, so your loan of it ended.");
        (await MessagesOf(reserver.Id)).Should().Equal($"\"{Title}\" was removed by its owner, so your reservation was cancelled.");
        (await MessagesOf(favoriter.Id)).Should().Equal($"\"{Title}\" was removed by its owner, so it's no longer in your favorites.");
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Open_requests_are_told_but_finished_ones_arent()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var asking = await factory.CreateUserAsync();     // an open request, and favorited it
        var declined = await factory.CreateUserAsync();   // asked earlier and was declined
        var bookId = await FreeBookOfAsync(owner);
        await RequestAsync(bookId, asking, BorrowRequestStatus.Requested);
        await FavoriteAsync(bookId, asking);
        await RequestAsync(bookId, declined, BorrowRequestStatus.Declined);

        var response = await DeleteAsync(ownerClient, OwnerDeleteUrl(bookId), new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(asking.Id)).Should().Equal(
            $"\"{Title}\" was removed by its owner, so your request to borrow it was cancelled.");
        (await MessagesOf(declined.Id)).Should().BeEmpty();
    }

    // ---- An admin deletes it ----

    [Fact]
    public async Task An_admin_deleting_it_tells_the_owner_why_and_the_friend_their_loan_ended()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var borrower = await factory.CreateUserAsync();
        var asking = await factory.CreateUserAsync();
        var bookId = await BookLentToAsync(owner, borrower);
        await RequestAsync(bookId, asking, BorrowRequestStatus.Requested); // left over from before it was lent

        var refused = await DeleteAsync(admin, AdminDeleteUrl(bookId), new { reason = "Duplicate entry" });
        var response = await DeleteAsync(admin, AdminDeleteUrl(bookId), new { reason = "Duplicate entry", confirmLent = true });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(owner.Id)).Should().Equal($"An admin removed your book \"{Title}\". Reason: Duplicate entry");
        (await MessagesOf(borrower.Id)).Should().Equal($"\"{Title}\" was removed, so your loan of it ended.");
        (await MessagesOf(asking.Id)).Should().Equal($"\"{Title}\" was removed, so your request to borrow it was cancelled.");
    }

    // ---- An admin takes it down ----

    [Fact]
    public async Task An_admin_taking_it_down_cancels_the_open_requests_and_tells_them()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var asking = await factory.CreateUserAsync();
        var bookId = await FreeBookOfAsync(owner);
        var requestId = await RequestAsync(bookId, asking, BorrowRequestStatus.Requested);

        var response = await admin.PutAsJsonAsync($"api/Book/{bookId}/admin-status",
            new { status = BookStatusEnum.Removed, notes = "Reported as offensive" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var request = await factory.WithDbAsync(db => db.BorrowRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, Ct));
        request.Status.Should().Be(BorrowRequestStatus.Cancelled);
        request.AnsweredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await MessagesOf(asking.Id)).Should().Equal($"\"{Title}\" was removed, so your request to borrow it was cancelled.");
    }

    // ---- Helpers ----

    private Task<int> FreeBookOfAsync(User owner) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(Title);
        book.OwnerId = owner.Id;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    /// <summary>The owner's paperback, lent to a friend as an accepted request would.</summary>
    private async Task<int> BookLentToAsync(User owner, User borrower)
    {
        var bookId = await FreeBookOfAsync(owner);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = bookId,
                UserId = borrower.Id,
                CheckOutDate = DateTime.UtcNow.AddDays(-3),
                ReturnDate = DateTime.UtcNow.AddDays(27),
            });
            (await db.Books.SingleAsync(b => b.BookId == bookId, Ct)).BookStatus = BookStatusEnum.CheckedOut;
            db.BorrowRequests.Add(new BorrowRequest
            {
                BookId = bookId,
                BorrowerId = borrower.Id,
                Status = BorrowRequestStatus.Lent,
                RequestedAt = DateTime.UtcNow.AddDays(-4),
                AvailableAt = DateTime.UtcNow.AddDays(-4),
                AnsweredAt = DateTime.UtcNow.AddDays(-3),
            });
            await db.SaveChangesAsync(Ct);
        });
        return bookId;
    }

    private Task<int> RequestAsync(int bookId, User friend, BorrowRequestStatus status) => factory.WithDbAsync(async db =>
    {
        var request = new BorrowRequest
        {
            BookId = bookId,
            BorrowerId = friend.Id,
            Status = status,
            RequestedAt = DateTime.UtcNow.AddDays(-2),
            AvailableAt = DateTime.UtcNow.AddDays(-2),
            AnsweredAt = status == BorrowRequestStatus.Requested ? null : DateTime.UtcNow.AddDays(-1),
        };
        db.BorrowRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return request.Id;
    });

    private Task ReserveAsync(int bookId, User friend) => factory.WithDbAsync(async db =>
    {
        db.ReservationItems.Add(new ReservationItem { BookId = bookId, UserId = friend.Id, ReservationDate = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync(Ct);
    });

    private Task FavoriteAsync(int bookId, User user) => factory.WithDbAsync(async db =>
    {
        db.FavoriteItems.Add(new FavoriteItem { BookId = bookId, UserId = user.Id });
        await db.SaveChangesAsync(Ct);
    });

    private Task<HttpResponseMessage> DeleteAsync(HttpClient client, string url, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) }, Ct);

    private Task<bool> BookExistsAsync(int bookId) => factory.WithDbAsync(db => db.Books.AnyAsync(b => b.BookId == bookId, Ct));

    private Task<List<string>> MessagesOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId).Select(n => n.Message).ToListAsync(Ct));
}
