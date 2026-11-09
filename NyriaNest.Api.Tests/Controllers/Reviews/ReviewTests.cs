using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Reviews;

/// <summary>
/// Pins down how reviews and ratings behave today (creating, listing, a book's reviews,
/// changing your own rating), so #116 can move the code into ReviewService without
/// changing it. Who may change whose review is covered in ReviewOwnershipTests.
/// </summary>
public class ReviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NewReviewUrl = "api/UserActions/newReview";
    private const string MyReviewsUrl = "api/UserActions/reviews";
    private static string BookReviewsUrl(int bookId) => $"api/Library/review/{bookId}";
    private static string EditRatingUrl(int reviewId) => $"api/UserActions/editRating/{reviewId}";
    private static string DeleteRatingUrl(int reviewId) => $"api/UserActions/deleteRating/{reviewId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The request must have a userId (the DTO says it's required), but the API replaces it
    // with the logged-in user, as ReviewOwnershipTests shows
    private const string IgnoredUserId = "replaced-by-the-api";

    // ---- create ----

    [Fact]
    public async Task Create_SavesTheReview_AndItsRating_AsMe_AndReturns201()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PostAsJsonAsync(NewReviewUrl,
            new { userId = IgnoredUserId, bookId, reviewHeader = "Loved it", reviewText = "Dragons everywhere", ratingItem = new { rating = 8 } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var saved = await ReviewsOfAsync(me.Id);
        saved.Should().ContainSingle();
        saved[0].BookId.Should().Be(bookId);
        saved[0].ReviewHeader.Should().Be("Loved it");
        saved[0].ReviewText.Should().Be("Dragons everywhere");
        saved[0].Rating.Should().Be(8);
    }

    [Fact]
    public async Task Create_WithoutARating_SavesJustTheReview()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PostAsJsonAsync(NewReviewUrl, new { userId = IgnoredUserId, bookId, reviewHeader = "Fine", reviewText = "Just fine" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReviewsOfAsync(me.Id)).Single().Rating.Should().BeNull();
    }

    [Fact]
    public async Task Create_ForABookThatDoesntExist_Returns404_AndSavesNothing()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PostAsJsonAsync(NewReviewUrl, new { userId = IgnoredUserId, bookId = 999999, reviewHeader = "?", reviewText = "?" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReviewsOfAsync(me.Id)).Should().BeEmpty();
    }

    // ---- my reviews ----

    [Fact]
    public async Task MyReviews_ListsOnlyMine()
    {
        var (client, _) = await factory.LoginAsAsync();
        var (otherClient, _) = await factory.LoginAsAsync();
        var mine = await AddBookAsync("Mine");
        var theirs = await AddBookAsync("Theirs");
        await client.PostAsJsonAsync(NewReviewUrl, new { userId = IgnoredUserId, bookId = mine, reviewHeader = "Mine", reviewText = "x" }, Ct);
        await otherClient.PostAsJsonAsync(NewReviewUrl, new { userId = IgnoredUserId, bookId = theirs, reviewHeader = "Theirs", reviewText = "x" }, Ct);

        var response = await client.GetAsync(MyReviewsUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ResultOf(response)).EnumerateArray().Select(r => r.GetProperty("bookId").GetInt32()).Should().Equal(mine);
    }

    // Like the reading list, no reviews is a 404 today, not an empty 200. Pinned so the
    // move doesn't change it by accident.
    [Fact]
    public async Task MyReviews_WhenNone_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.GetAsync(MyReviewsUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("No reviews found for this user.");
    }

    // ---- a book's reviews ----

    [Fact]
    public async Task BookReviews_AreNewestFirst_WithTheReviewersNames()
    {
        var (first, firstUser) = await factory.LoginAsAsync();
        var (second, secondUser) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();
        await first.PostAsJsonAsync(NewReviewUrl, new { userId = IgnoredUserId, bookId, reviewHeader = "Older", reviewText = "x", createdAt = DateTime.UtcNow.AddDays(-2) }, Ct);
        await second.PostAsJsonAsync(NewReviewUrl, new { userId = IgnoredUserId, bookId, reviewHeader = "Newer", reviewText = "x", createdAt = DateTime.UtcNow }, Ct);

        var response = await first.GetAsync(BookReviewsUrl(bookId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviews = (await ResultOf(response)).EnumerateArray().ToList();
        reviews.Select(r => r.GetProperty("reviewHeader").GetString()).Should().Equal("Newer", "Older");
        reviews.Select(r => r.GetProperty("userName").GetString()).Should().Equal(secondUser.UserName, firstUser.UserName);
    }

    [Fact]
    public async Task BookReviews_ForABookThatDoesntExist_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.GetAsync(BookReviewsUrl(999999), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- my own rating ----

    [Fact]
    public async Task EditRating_OfMyOwnReview_ChangesIt()
    {
        var (client, me) = await factory.LoginAsAsync();
        var reviewId = await CreateReviewAsync(client, rating: 5);

        var response = await client.PutAsJsonAsync(EditRatingUrl(reviewId), new { rating = 9 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReviewsOfAsync(me.Id)).Single().Rating.Should().Be(9);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task EditRating_OutsideZeroToTen_Returns400_AndKeepsIt(int rating)
    {
        var (client, me) = await factory.LoginAsAsync();
        var reviewId = await CreateReviewAsync(client, rating: 5);

        var response = await client.PutAsJsonAsync(EditRatingUrl(reviewId), new { rating }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReviewsOfAsync(me.Id)).Single().Rating.Should().Be(5);
    }

    [Fact]
    public async Task EditRating_OnAReviewWithoutARating_Returns404()
    {
        var (client, _) = await factory.LoginAsAsync();
        var reviewId = await CreateReviewAsync(client, rating: null);

        var response = await client.PutAsJsonAsync(EditRatingUrl(reviewId), new { rating = 7 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRating_OfMyOwnReview_RemovesIt_AndKeepsTheReview()
    {
        var (client, me) = await factory.LoginAsAsync();
        var reviewId = await CreateReviewAsync(client, rating: 6);

        var response = await client.DeleteAsync(DeleteRatingUrl(reviewId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var review = (await ReviewsOfAsync(me.Id)).Single();
        review.Id.Should().Be(reviewId);
        review.Rating.Should().BeNull();
    }

    // ---- helpers ----

    private record SavedReview(int Id, int BookId, string? ReviewHeader, string? ReviewText, int? Rating);

    private Task<int> AddBookAsync(string title = "Review book") => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(title);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private async Task<int> CreateReviewAsync(HttpClient client, int? rating)
    {
        var bookId = await AddBookAsync();
        object body = rating is null
            ? new { userId = IgnoredUserId, bookId, reviewHeader = "Header", reviewText = "Text" }
            : new { userId = IgnoredUserId, bookId, reviewHeader = "Header", reviewText = "Text", ratingItem = new { rating } };
        (await client.PostAsJsonAsync(NewReviewUrl, body, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        return await factory.WithDbAsync(db => db.ReviewItems.Where(r => r.BookId == bookId).Select(r => r.Id).SingleAsync(Ct));
    }

    private Task<List<SavedReview>> ReviewsOfAsync(string userId) => factory.WithDbAsync(db =>
        db.ReviewItems.Where(r => r.UserId == userId)
            .Select(r => new SavedReview(r.Id, r.BookId, r.ReviewHeader, r.ReviewText, r.RatingItem == null ? null : r.RatingItem.Rating))
            .ToListAsync(Ct));

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("result").Clone();
    }
}
