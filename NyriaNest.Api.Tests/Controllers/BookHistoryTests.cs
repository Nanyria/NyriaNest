using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #32: GET api/Book/history/{bookId} returns the book's status history,
/// newest first, as StatusHistoryItemDto (with the book), not as entities.
/// </summary>
public class BookHistoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_ReturnsTheBooksEntries_NewestFirst()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync(Roles.User);
        var bookId = await AddBookAsync("History book");
        var otherBookId = await AddBookAsync("Other book");
        var start = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        await AddHistoryAsync(bookId, reader.Id, BookStatusEnum.Reserved, start);
        await AddHistoryAsync(bookId, reader.Id, BookStatusEnum.CheckedOut, start.AddDays(2));
        await AddHistoryAsync(bookId, reader.Id, BookStatusEnum.Returned, start.AddDays(9));
        await AddHistoryAsync(otherBookId, reader.Id, BookStatusEnum.CheckedOut, start.AddDays(3));

        var response = await admin.GetAsync($"api/Book/history/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray();
        entries.Select(e => (int)e!["bookStatus"]!).Should().Equal(
            (int)BookStatusEnum.Returned, (int)BookStatusEnum.CheckedOut, (int)BookStatusEnum.Reserved);
    }

    [Fact]
    public async Task History_EntriesAreDtos_WithTheBook()
    {
        // The entity hides its Book ([JsonIgnore]), so a title in the response means a DTO
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var reader = await factory.CreateUserAsync(Roles.User);
        var bookId = await AddBookAsync("Titled book");
        await AddHistoryAsync(bookId, reader.Id, BookStatusEnum.CheckedOut, DateTime.UtcNow);

        var response = await admin.GetAsync($"api/Book/history/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray().Single()!;
        ((string?)entry["book"]?["title"]).Should().Be("Titled book");
        ((string?)entry["userID"]).Should().Be(reader.Id);
    }

    [Fact]
    public async Task History_OfASeededBook_HasItsInitialAddedRow()
    {
        // The seeded books come with an "Initial added status" history row
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync("api/Book/history/1004", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var notes = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray()
            .Select(e => (string?)e!["notes"]);
        notes.Should().Contain("Initial added status");
    }

    [Fact]
    public async Task History_OfABookWithoutEntries_IsEmpty()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync("New book");

        var response = await admin.GetAsync($"api/Book/history/{bookId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task History_OfAnUnknownBook_Returns404_SayingTheBookWasNotFound()
    {
        // It used to say "User not found."
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync("api/Book/history/999999", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("Book 999999 was not found.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.User)]
    public async Task History_IsForAdminsOnly(string? role)
    {
        var client = role is null ? factory.CreateClient() : (await factory.LoginAsAsync(role)).Item1;
        var bookId = await AddBookAsync("Private history");

        var response = await client.GetAsync($"api/Book/history/{bookId}", Ct);

        response.StatusCode.Should().Be(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
    }

    // ---- Helpers ----

    private Task<int> AddBookAsync(string title) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title: title);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddHistoryAsync(int bookId, string userId, BookStatusEnum status, DateTime when) =>
        factory.WithDbAsync(async db =>
        {
            db.StatusHistoryItems.Add(new() { BookId = bookId, UserId = userId, BookStatus = status, Timestamp = when });
            await db.SaveChangesAsync(Ct);
        });
}
