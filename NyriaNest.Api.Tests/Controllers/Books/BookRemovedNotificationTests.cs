using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #75, as decided in #83: when a book is deleted, everyone with a current
/// interaction with it (a reservation or a favorite) gets one notification that
/// names the book. A reservation is named first. The owner is never one of them:
/// they get their own message when an admin deleted their book. Past reviews and
/// ratings don't count, and a delete that is refused tells no one.
/// </summary>
public class BookRemovedNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- An admin deletes a shared-library book ----

    [Fact]
    public async Task AdminDelete_SharedBook_TellsWhoReservedIt_AndWhoFavoritedIt_AndNoOneElse()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reserver = await factory.CreateUserAsync();
        var favoriter = await factory.CreateUserAsync();
        var bystander = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("The Wicked King", BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, reserver.Id, daysAgo: 1);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(reserver.Id)).Should().Equal(
            "\"The Wicked King\" was removed from the shared library, so your reservation was cancelled.");
        (await MessagesOf(favoriter.Id)).Should().Equal(
            "\"The Wicked King\" was removed from the shared library, so it's no longer in your favorites.");
        (await MessagesOf(bystander.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminDelete_SharedBook_SomeoneWhoReservedAndFavoritedIt_GetsOneMessage_NamingTheReservation()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Both Ways", BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, reader.Id, daysAgo: 1);
        await AddFavoriteAsync(bookId, reader.Id);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(reader.Id)).Should().Equal(
            "\"Both Ways\" was removed from the shared library, so your reservation was cancelled.");
    }

    [Fact]
    public async Task AdminDelete_SharedBook_SomeoneWithTwoReservations_GetsOneMessage()
    {
        // Nothing in the DB stops a double reservation, so the service has to de-duplicate
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Reserved Twice", BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, reader.Id, daysAgo: 2);
        await AddReservationAsync(bookId, reader.Id, daysAgo: 1);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(reader.Id)).Should().Equal(
            "\"Reserved Twice\" was removed from the shared library, so your reservation was cancelled.");
    }

    [Fact]
    public async Task AdminDelete_SharedBook_SomeoneWhoOnlyReviewedAndRatedIt_IsNotTold()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reviewer = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Reviewed Long Ago", BookStatusEnum.Available);
        await factory.WithDbAsync(async db =>
        {
            var review = new Domain.Entities.ReviewItem { BookId = bookId, UserId = reviewer.Id, ReviewHeader = "Good", ReviewText = "Liked it" };
            db.ReviewItems.Add(review);
            await db.SaveChangesAsync(Ct);
            db.RatingItems.Add(new() { ReviewItemId = review.Id, Rating = 4 });
            await db.SaveChangesAsync(Ct);
        });

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(reviewer.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AdminDelete_SharedBook_TheAdminWhoFavoritedIt_IsToldToo()
    {
        var (admin, me) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync("The Admin's Favorite", BookStatusEnum.Available);
        await AddFavoriteAsync(bookId, me.Id);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(me.Id)).Should().Equal(
            "\"The Admin's Favorite\" was removed from the shared library, so it's no longer in your favorites.");
    }

    [Fact]
    public async Task AdminDelete_ACheckedOutBook_Returns409_AndTellsNoOne()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var holder = await factory.CreateUserAsync();
        var reserver = await factory.CreateUserAsync();
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("A Book On Loan", BookStatusEnum.CheckedOut);
        await factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new() { BookId = bookId, UserId = holder.Id, CheckOutDate = DateTime.UtcNow, ReturnDate = DateTime.UtcNow.AddDays(14) });
            await db.SaveChangesAsync(Ct);
        });
        await AddReservationAsync(bookId, reserver.Id, daysAgo: 1);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MessagesOf(holder.Id)).Should().BeEmpty();
        (await MessagesOf(reserver.Id)).Should().BeEmpty();
        (await MessagesOf(favoriter.Id)).Should().BeEmpty();
    }

    // ---- An admin deletes someone's own book ----

    [Fact]
    public async Task AdminDelete_SomeonesOwnBook_TellsTheOwnerWhyOnce_AndTheOthersWithoutTheReason()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var reserver = await factory.CreateUserAsync();
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Alma's Hobbit", BookStatusEnum.Available, ownerId: owner.Id);
        await AddFavoriteAsync(bookId, owner.Id); // the owner's reading list: no second message
        await AddReservationAsync(bookId, reserver.Id, daysAgo: 1); // left over from before owned books were guarded
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await DeleteWithBodyAsync(admin, $"api/Book/{bookId}", new { reason = "Duplicate entry" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(owner.Id)).Should().Equal(
            "An admin removed your book \"Alma's Hobbit\". Reason: Duplicate entry");
        (await MessagesOf(reserver.Id)).Should().Equal(
            "\"Alma's Hobbit\" was removed, so your reservation was cancelled.");
        (await MessagesOf(favoriter.Id)).Should().Equal(
            "\"Alma's Hobbit\" was removed, so it's no longer in your favorites.");
    }

    // ---- The owner deletes their own book ----

    [Fact]
    public async Task OwnerDelete_TellsWhoFavoritedIt_ButNotTheOwner()
    {
        var (ownerClient, owner) = await factory.LoginAsAsync();
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Boris's Dune", BookStatusEnum.Available, ownerId: owner.Id);
        await AddFavoriteAsync(bookId, owner.Id);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await ownerClient.DeleteAsync($"api/my/books/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(favoriter.Id)).Should().Equal(
            "\"Boris's Dune\" was removed by its owner, so it's no longer in your favorites.");
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(string title, BookStatusEnum status, string? ownerId = null) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title: title, status: status);
        book.OwnerId = ownerId;
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddReservationAsync(int bookId, string userId, int daysAgo) => factory.WithDbAsync(async db =>
    {
        db.ReservationItems.Add(new() { BookId = bookId, UserId = userId, ReservationDate = DateTime.UtcNow.AddDays(-daysAgo) });
        await db.SaveChangesAsync(Ct);
    });

    private Task AddFavoriteAsync(int bookId, string userId) => factory.WithDbAsync(async db =>
    {
        db.FavoriteItems.Add(new() { BookId = bookId, UserId = userId });
        await db.SaveChangesAsync(Ct);
    });

    private Task<HttpResponseMessage> DeleteWithBodyAsync(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) };
        return client.SendAsync(request, Ct);
    }

    private Task<List<string>> MessagesOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId).Select(n => n.Message).ToListAsync(Ct));
}
