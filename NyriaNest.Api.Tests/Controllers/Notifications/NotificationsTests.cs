using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Notifications;

/// <summary>
/// #91: the logged-in user's notifications. The list is newest first and paged,
/// can be filtered to unread only (the bell's dropdown shows the newest 3 unread),
/// and the bell gets its own unread count. A user only ever sees and changes their own.
/// </summary>
public class NotificationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Reading the list ----

    [Fact]
    public async Task GetNotifications_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("api/notifications", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetNotifications_ReturnsOnlyMine_NewestFirst()
    {
        var (client, me) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        await AddNotificationAsync(me.Id, "oldest", minutesAgo: 30);
        await AddNotificationAsync(me.Id, "newest", minutesAgo: 10);
        await AddNotificationAsync(me.Id, "middle", minutesAgo: 20);
        await AddNotificationAsync(someoneElse.Id, "not mine", minutesAgo: 5);

        var page = await GetPageAsync(client, "api/notifications");

        Messages(page).Should().Equal("newest", "middle", "oldest");
        page.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetNotifications_IsPaged()
    {
        var (client, me) = await factory.LoginAsAsync();
        for (var i = 1; i <= 5; i++)
            await AddNotificationAsync(me.Id, $"n{i}", minutesAgo: i); // n1 is the newest

        var page = await GetPageAsync(client, "api/notifications?page=2&pageSize=2");

        Messages(page).Should().Equal("n3", "n4");
        page.GetProperty("totalCount").GetInt32().Should().Be(5);
        page.GetProperty("page").GetInt32().Should().Be(2);
        page.GetProperty("pageSize").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetNotifications_ReturnsTypeAndBookId_AndIncludesReadOnes()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync("Linked Book");
        await AddNotificationAsync(me.Id, "about a book", minutesAgo: 1, isRead: true, bookId: bookId);

        var item = (await GetPageAsync(client, "api/notifications")).GetProperty("items")[0];

        item.GetProperty("message").GetString().Should().Be("about a book");
        item.GetProperty("isRead").GetBoolean().Should().BeTrue();
        item.GetProperty("type").GetInt32().Should().Be((int)NotificationType.BookRemoved);
        item.GetProperty("bookId").GetInt32().Should().Be(bookId);
    }

    [Fact]
    public async Task GetNotifications_WhenIHaveNone_ReturnsEmpty_AndUnreadCountIsZero()
    {
        var (client, _) = await factory.LoginAsAsync();

        var all = await GetPageAsync(client, "api/notifications");
        var unread = await GetPageAsync(client, "api/notifications?unreadOnly=true&pageSize=3");

        all.GetProperty("items").GetArrayLength().Should().Be(0);
        all.GetProperty("totalCount").GetInt32().Should().Be(0);
        unread.GetProperty("items").GetArrayLength().Should().Be(0);
        unread.GetProperty("totalCount").GetInt32().Should().Be(0);
        (await GetUnreadCountAsync(client)).Should().Be(0);
    }

    // ---- The dropdown: newest 3 unread ----

    [Fact]
    public async Task UnreadOnly_ReturnsTheNewestThreeUnread_EvenWhenNewerOnesAreRead()
    {
        var (client, me) = await factory.LoginAsAsync();
        await AddNotificationAsync(me.Id, "read newest", minutesAgo: 1, isRead: true);
        await AddNotificationAsync(me.Id, "read second", minutesAgo: 2, isRead: true);
        for (var i = 1; i <= 5; i++)
            await AddNotificationAsync(me.Id, $"unread{i}", minutesAgo: 10 + i); // unread1 is the newest unread

        var page = await GetPageAsync(client, "api/notifications?unreadOnly=true&pageSize=3");

        Messages(page).Should().Equal("unread1", "unread2", "unread3");
        page.GetProperty("totalCount").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task UnreadOnly_WithFewerThanThreeUnread_ReturnsJustThose()
    {
        var (client, me) = await factory.LoginAsAsync();
        await AddNotificationAsync(me.Id, "read", minutesAgo: 1, isRead: true);
        await AddNotificationAsync(me.Id, "unread newer", minutesAgo: 2);
        await AddNotificationAsync(me.Id, "unread older", minutesAgo: 3);

        var page = await GetPageAsync(client, "api/notifications?unreadOnly=true&pageSize=3");

        Messages(page).Should().Equal("unread newer", "unread older");
        page.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task UnreadOnly_WhenAllAreRead_ReturnsEmpty()
    {
        var (client, me) = await factory.LoginAsAsync();
        await AddNotificationAsync(me.Id, "read one", minutesAgo: 1, isRead: true);
        await AddNotificationAsync(me.Id, "read two", minutesAgo: 2, isRead: true);

        var page = await GetPageAsync(client, "api/notifications?unreadOnly=true&pageSize=3");

        page.GetProperty("items").GetArrayLength().Should().Be(0);
        page.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    // ---- The bell's unread count ----

    [Fact]
    public async Task UnreadCount_CountsOnlyMyUnread()
    {
        var (client, me) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        await AddNotificationAsync(me.Id, "unread 1", minutesAgo: 1);
        await AddNotificationAsync(me.Id, "unread 2", minutesAgo: 2);
        await AddNotificationAsync(me.Id, "read", minutesAgo: 3, isRead: true);
        await AddNotificationAsync(someoneElse.Id, "not mine", minutesAgo: 1);

        (await GetUnreadCountAsync(client)).Should().Be(2);
    }

    [Fact]
    public async Task UnreadCount_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("api/notifications/unread-count", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Marking as read ----

    [Fact]
    public async Task MarkAsRead_MyOwn_MarksItRead_AndLowersTheUnreadCount()
    {
        var (client, me) = await factory.LoginAsAsync();
        var target = await AddNotificationAsync(me.Id, "target", minutesAgo: 1);
        await AddNotificationAsync(me.Id, "other 1", minutesAgo: 2);
        await AddNotificationAsync(me.Id, "other 2", minutesAgo: 3);

        var response = await client.PutAsync($"api/notifications/{target}/read", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await IsReadAsync(target)).Should().BeTrue();
        (await GetUnreadCountAsync(client)).Should().Be(2);
    }

    [Fact]
    public async Task MarkAsRead_SomeoneElses_Returns404_AndLeavesItUnread()
    {
        // 404, not 403: a notification is private, so don't even confirm it exists
        var (client, _) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        var theirs = await AddNotificationAsync(someoneElse.Id, "theirs", minutesAgo: 1);

        var response = await client.PutAsync($"api/notifications/{theirs}/read", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IsReadAsync(theirs)).Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsRead_UnknownId_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.PutAsync("api/notifications/999999/read", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAllAsRead_MarksAllMine_AndLeavesOthersAlone()
    {
        var (client, me) = await factory.LoginAsAsync();
        var someoneElse = await factory.CreateUserAsync();
        var mine1 = await AddNotificationAsync(me.Id, "mine 1", minutesAgo: 1);
        var mine2 = await AddNotificationAsync(me.Id, "mine 2", minutesAgo: 2);
        var theirs = await AddNotificationAsync(someoneElse.Id, "theirs", minutesAgo: 1);

        var response = await client.PutAsync("api/notifications/read-all", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await IsReadAsync(mine1)).Should().BeTrue();
        (await IsReadAsync(mine2)).Should().BeTrue();
        (await IsReadAsync(theirs)).Should().BeFalse();
        (await GetUnreadCountAsync(client)).Should().Be(0);
    }

    // ---- Links to a book ----

    [Fact]
    public async Task DeletingTheLinkedBook_KeepsTheNotification_AndClearsBookId()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync("Soon Gone");
        await AddNotificationAsync(me.Id, "about Soon Gone", minutesAgo: 1, bookId: bookId);

        var delete = await admin.DeleteAsync($"api/Book/{bookId}", Ct);
        delete.StatusCode.Should().Be(HttpStatusCode.OK, await delete.Content.ReadAsStringAsync(Ct));

        var item = (await GetPageAsync(client, "api/notifications")).GetProperty("items")[0];
        item.GetProperty("message").GetString().Should().Be("about Soon Gone");
        item.GetProperty("bookId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- #75 end to end ----

    [Fact]
    public async Task ABookRemovedByAnAdmin_ShowsUpAsUnread_ForTheReaderWhoReservedIt()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var (reader, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync("The Cruel Prince", BookStatusEnum.Reserved);
        await factory.WithDbAsync(async db =>
        {
            db.ReservationItems.Add(new() { BookId = bookId, UserId = me.Id, ReservationDate = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync(Ct);
        });

        var delete = await admin.DeleteAsync($"api/Book/{bookId}", Ct);
        delete.StatusCode.Should().Be(HttpStatusCode.OK, await delete.Content.ReadAsStringAsync(Ct));

        var item = (await GetPageAsync(reader, "api/notifications?unreadOnly=true&pageSize=3")).GetProperty("items")
            .EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("message").GetString().Should().Contain("The Cruel Prince");
        item.GetProperty("isRead").GetBoolean().Should().BeFalse();
        item.GetProperty("type").GetInt32().Should().Be((int)NotificationType.BookRemoved);
        item.GetProperty("bookId").ValueKind.Should().Be(JsonValueKind.Null); // the book is gone, so nothing to link to
        (await GetUnreadCountAsync(reader)).Should().Be(1);
    }

    // ---- Helpers ----

    private Task<int> AddNotificationAsync(
        string userId, string message, int minutesAgo, bool isRead = false, int? bookId = null,
        NotificationType type = NotificationType.BookRemoved) => factory.WithDbAsync(async db =>
    {
        var notification = new NotificationItem
        {
            UserId = userId,
            Message = message,
            IsRead = isRead,
            CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
            Type = type,
            BookId = bookId,
        };
        db.NotificationItems.Add(notification);
        await db.SaveChangesAsync(Ct);
        return notification.Id;
    });

    private Task<int> AddBookAsync(string title, BookStatusEnum status = BookStatusEnum.Available) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title: title, status: status);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task<bool> IsReadAsync(int notificationId) => factory.WithDbAsync(db =>
        db.NotificationItems.Where(n => n.Id == notificationId).Select(n => n.IsRead).SingleAsync(Ct));

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await ResultOf(response);
    }

    private static async Task<int> GetUnreadCountAsync(HttpClient client)
    {
        var response = await client.GetAsync("api/notifications/unread-count", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await ResultOf(response)).GetInt32();
    }

    private static List<string?> Messages(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("message").GetString()).ToList();

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("result").Clone();
    }
}
