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
/// #83 step 5b: an admin marking a book Removed (or Lost) counts as deleting it for
/// readers. Reservations are cancelled, favorites removed, and everyone who had either
/// gets the same message as for a delete. E-books can't be lost. Someone else's book
/// follows the owner rule: notes (the reason) required, owner told, history row. A
/// restore tells only the owner.
/// </summary>
public class AdminStatusNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Shared-library books ----

    [Fact]
    public async Task Removed_SharedBook_TellsWhoReservedAndWhoFavoritedIt_AndCancelsBoth()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reserver = await factory.CreateUserAsync();
        var favoriter = await factory.CreateUserAsync();
        var bystander = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Frankenstein", BookStatusEnum.Reserved);
        await AddReservationAsync(bookId, reserver.Id);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await SetStatusAsync(admin, bookId, BookStatusEnum.Removed, "Copyright question");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(reserver.Id)).Should().Equal(
            "\"Frankenstein\" was removed from the shared library, so your reservation was cancelled.");
        (await MessagesOf(favoriter.Id)).Should().Equal(
            "\"Frankenstein\" was removed from the shared library, so it's no longer in your favorites.");
        (await MessagesOf(bystander.Id)).Should().BeEmpty();
        (await ReservationCountOf(bookId)).Should().Be(0);
        (await FavoriteCountOf(bookId)).Should().Be(0);
        (await BookAsync(bookId)).BookStatus.Should().Be(BookStatusEnum.Removed);
    }

    [Fact]
    public async Task Lost_SharedBook_Returns400_AndChangesNothing()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("An E-book Can't Go Missing", BookStatusEnum.Available);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await SetStatusAsync(admin, bookId, BookStatusEnum.Lost, "Gone");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("E-books can't be lost.");
        (await BookAsync(bookId)).AdminStatus.Should().BeNull();
        (await FavoriteCountOf(bookId)).Should().Be(1);
        (await MessagesOf(favoriter.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Removed_ThenDeleted_TellsNoOneASecondTime()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Taken Down, Then Gone", BookStatusEnum.Available);
        await AddFavoriteAsync(bookId, favoriter.Id);

        const string removedMessage =
            "\"Taken Down, Then Gone\" was removed from the shared library, so it's no longer in your favorites.";

        (await SetStatusAsync(admin, bookId, BookStatusEnum.Removed, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessagesOf(favoriter.Id)).Should().Equal(removedMessage); // told at Removed...
        var delete = await admin.DeleteAsync($"api/Book/{bookId}", Ct);

        delete.StatusCode.Should().Be(HttpStatusCode.OK, await delete.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(favoriter.Id)).Should().Equal(removedMessage); // ...and not again at the delete
    }

    [Fact]
    public async Task Restore_SharedBook_TellsNoOne()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Back Again", BookStatusEnum.Available);
        await AddFavoriteAsync(bookId, favoriter.Id);
        (await SetStatusAsync(admin, bookId, BookStatusEnum.Removed, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await admin.DeleteAsync($"api/Book/{bookId}/admin-status", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookAsync(bookId)).AdminStatus.Should().BeNull();
        (await MessagesOf(favoriter.Id)).Should().ContainSingle("only the Removed message, nothing on restore");
    }

    // ---- Someone else's book: the owner rule ----

    [Theory]
    [InlineData(BookStatusEnum.Removed, null)]
    [InlineData(BookStatusEnum.Removed, "  ")]
    [InlineData(BookStatusEnum.Lost, null)]
    [InlineData(BookStatusEnum.Lost, "  ")]
    public async Task SomeoneElsesBook_WithoutNotes_Returns400_ChangesNothing_AndTellsNoOne(BookStatusEnum status, string? notes)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Alma's Rebecca", BookStatusEnum.Available, ownerId: owner.Id);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await SetStatusAsync(admin, bookId, status, notes);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BookAsync(bookId)).AdminStatus.Should().BeNull();
        (await FavoriteCountOf(bookId)).Should().Be(1);
        (await MessagesOf(owner.Id)).Should().BeEmpty();
        (await MessagesOf(favoriter.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(BookStatusEnum.Removed, "removed")]
    [InlineData(BookStatusEnum.Lost, "lost")]
    public async Task SomeoneElsesBook_WithNotes_TellsTheOwnerWhyOnce_TellsTheOthers_AndWritesHistory(BookStatusEnum status, string word)
    {
        var (admin, me) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var reserver = await factory.CreateUserAsync();
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Alma's Hobbit", BookStatusEnum.Available, ownerId: owner.Id);
        await AddFavoriteAsync(bookId, owner.Id); // the owner's reading list: no second message
        await AddReservationAsync(bookId, reserver.Id); // left over from before owned books were guarded
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await SetStatusAsync(admin, bookId, status, "Reported by the owner");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var ownerNotification = (await NotificationsOf(owner.Id)).Should().ContainSingle().Subject;
        ownerNotification.Message.Should().Be($"An admin marked your book \"Alma's Hobbit\" as {word}. Reason: Reported by the owner");
        ownerNotification.Type.Should().Be(NotificationType.BookStatusChangedByAdmin);
        ownerNotification.BookId.Should().Be(bookId); // the book still exists, so it can be linked
        (await MessagesOf(reserver.Id)).Should().Equal("\"Alma's Hobbit\" was removed, so your reservation was cancelled.");
        (await MessagesOf(favoriter.Id)).Should().Equal("\"Alma's Hobbit\" was removed, so it's no longer in your favorites.");
        (await HistoryOf(bookId)).Should().Contain(h =>
            h.UserId == me.Id && h.BookStatus == status && h.Notes!.Contains("Reported by the owner"));
    }

    [Fact]
    public async Task Removed_TheAdminsOwnBook_NeedsNoNotes_AndTellsTheOthers()
    {
        var (admin, me) = await factory.LoginAsAsync(Roles.Admin);
        var favoriter = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("The Admin's Own Book", BookStatusEnum.Available, ownerId: me.Id);
        await AddFavoriteAsync(bookId, favoriter.Id);

        var response = await SetStatusAsync(admin, bookId, BookStatusEnum.Removed, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await MessagesOf(favoriter.Id)).Should().Equal("\"The Admin's Own Book\" was removed, so it's no longer in your favorites.");
        (await MessagesOf(me.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Restore_SomeoneElsesBook_WithoutNotes_Returns400_AndKeepsItRemoved(string? notes)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Still Hidden", BookStatusEnum.Removed, ownerId: owner.Id, adminStatus: BookStatusEnum.Removed);

        var response = await RestoreAsync(admin, bookId, notes);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BookAsync(bookId)).AdminStatus.Should().Be(BookStatusEnum.Removed);
        (await MessagesOf(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_SomeoneElsesBook_WithNotes_TellsTheOwnerWhy_AndWritesHistory()
    {
        var (admin, me) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Found Again", BookStatusEnum.Removed, ownerId: owner.Id, adminStatus: BookStatusEnum.Removed);

        var response = await RestoreAsync(admin, bookId, "Removed by mistake");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await BookAsync(bookId)).AdminStatus.Should().BeNull();
        var ownerNotification = (await NotificationsOf(owner.Id)).Should().ContainSingle().Subject;
        ownerNotification.Message.Should().Be("An admin restored your book \"Found Again\". Reason: Removed by mistake");
        ownerNotification.Type.Should().Be(NotificationType.BookStatusChangedByAdmin);
        ownerNotification.BookId.Should().Be(bookId);
        (await HistoryOf(bookId)).Should().Contain(h => h.UserId == me.Id && h.Notes == "Restored by admin: Removed by mistake");
    }

    // ---- Someone else's book lent to a name (#83 step 8e) ----
    // The loan is the owner's own note of who has it: Lost/Removed is a flag on the
    // book and leaves the loan alone, so after a restore the book is lent as before.

    [Theory]
    [InlineData(BookStatusEnum.Removed)]
    [InlineData(BookStatusEnum.Lost)]
    public async Task SomeoneElsesBook_LentToAName_TakenDownAndRestored_KeepsTheLoan(BookStatusEnum status)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Lent to Ruth", BookStatusEnum.CheckedOut, ownerId: owner.Id);
        await AddLoanAsync(bookId, borrowerName: "Ruth (neighbour)");

        var takeDown = await SetStatusAsync(admin, bookId, status, "Inappropriate content");

        takeDown.StatusCode.Should().Be(HttpStatusCode.OK, await takeDown.Content.ReadAsStringAsync(Ct));
        var takenDown = await BookAsync(bookId);
        takenDown.AdminStatus.Should().Be(status);
        takenDown.BookStatus.Should().Be(status);
        (await LoanOf(bookId))!.BorrowerName.Should().Be("Ruth (neighbour)");

        var restore = await RestoreAsync(admin, bookId, "Removed by mistake");

        restore.StatusCode.Should().Be(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync(Ct));
        var restored = await BookAsync(bookId);
        restored.AdminStatus.Should().BeNull();
        restored.BookStatus.Should().Be(BookStatusEnum.CheckedOut); // lent as before
        (await LoanOf(bookId))!.BorrowerName.Should().Be("Ruth (neighbour)");
    }

    [Fact]
    public async Task Book_LentToAMember_StillCantBeTakenDown()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var owner = await factory.CreateUserAsync();
        var borrower = await factory.CreateUserAsync();
        var bookId = await AddBookAsync("Lent to a member", BookStatusEnum.CheckedOut, ownerId: owner.Id);
        await AddLoanAsync(bookId, userId: borrower.Id);

        var response = await SetStatusAsync(admin, bookId, BookStatusEnum.Removed, "Inappropriate content");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("This book is checked out. Return it first.");
        (await BookAsync(bookId)).AdminStatus.Should().BeNull();
        (await LoanOf(bookId))!.UserId.Should().Be(borrower.Id);
    }

    // ---- Helpers ----

    // Exactly one of the two: a member's loan or a loan by name
    private Task AddLoanAsync(int bookId, string? borrowerName = null, string? userId = null) =>
        factory.WithDbAsync(async db =>
        {
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = bookId,
                BorrowerName = borrowerName,
                UserId = userId,
                CheckOutDate = DateTime.UtcNow,
                ReturnDate = DateTime.UtcNow.AddDays(14),
            });
            await db.SaveChangesAsync(Ct);
        });

    private Task<CheckedOutItem?> LoanOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.AsNoTracking().SingleOrDefaultAsync(c => c.BookId == bookId, Ct));

    private Task<HttpResponseMessage> SetStatusAsync(HttpClient client, int bookId, BookStatusEnum status, string? notes) =>
        client.PutAsJsonAsync($"api/Book/{bookId}/admin-status", new { status, notes }, Ct);

    private Task<HttpResponseMessage> RestoreAsync(HttpClient client, int bookId, string? notes)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/Book/{bookId}/admin-status")
        {
            Content = JsonContent.Create(new { notes }),
        };
        return client.SendAsync(request, Ct);
    }

    private Task<int> AddBookAsync(string title, BookStatusEnum status, string? ownerId = null, BookStatusEnum? adminStatus = null) =>
        factory.WithDbAsync(async db =>
        {
            var book = TestData.Book(title: title, status: status);
            book.OwnerId = ownerId;
            book.AdminStatus = adminStatus;
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });

    private Task AddReservationAsync(int bookId, string userId) => factory.WithDbAsync(async db =>
    {
        db.ReservationItems.Add(new() { BookId = bookId, UserId = userId, ReservationDate = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync(Ct);
    });

    private Task AddFavoriteAsync(int bookId, string userId) => factory.WithDbAsync(async db =>
    {
        db.FavoriteItems.Add(new() { BookId = bookId, UserId = userId });
        await db.SaveChangesAsync(Ct);
    });

    private Task<Book> BookAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AsNoTracking().SingleAsync(b => b.BookId == bookId, Ct));

    private Task<int> ReservationCountOf(int bookId) => factory.WithDbAsync(db =>
        db.ReservationItems.CountAsync(r => r.BookId == bookId, Ct));

    private Task<int> FavoriteCountOf(int bookId) => factory.WithDbAsync(db =>
        db.FavoriteItems.CountAsync(f => f.BookId == bookId, Ct));

    private Task<List<StatusHistoryItem>> HistoryOf(int bookId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.AsNoTracking().Where(h => h.BookId == bookId).ToListAsync(Ct));

    private Task<List<NotificationItem>> NotificationsOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(Ct));

    private Task<List<string>> MessagesOf(string userId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.UserId == userId).Select(n => n.Message).ToListAsync(Ct));
}
