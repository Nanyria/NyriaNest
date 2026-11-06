using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Security finding 1 (#48): book actions need a login, act only as the logged-in user
/// (taken from the token, not the URL or body), and can't touch other users' reviews.
/// </summary>
public class UserActionsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> BookActions => new()
    {
        { "GET", "api/UserActions/me" },
        { "GET", "api/UserActions/checkedout" },
        { "PUT", "api/UserActions/borrow/1001" },
        { "PUT", "api/UserActions/return/1001" },
        { "GET", "api/UserActions/reserved" },
        { "PUT", "api/UserActions/reserve/1001" },
        { "PUT", "api/UserActions/unreserve/1001" },
        { "GET", "api/UserActions/reviews" },
        { "POST", "api/UserActions/newReview" },
        { "PUT", "api/UserActions/editReview/1001" },
        { "DELETE", "api/UserActions/deleteReview/1001" },
        { "PUT", "api/UserActions/editRating/1001" },
        { "DELETE", "api/UserActions/deleteRating/1001" },
        { "GET", "api/UserActions/favorites" },
        { "PUT", "api/UserActions/favorite/1001" },
        { "DELETE", "api/UserActions/unfavorite/1001" },
        { "PUT", "api/UserActions/update" },
    };

    [Theory]
    [MemberData(nameof(BookActions))]
    public async Task BookAction_WithoutToken_Returns401(string method, string url)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { })
        };

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_WithoutToken_AndWithoutRole_Works()
    {
        // Exactly what the registration page sends: no role
        using var client = factory.CreateClient();
        var name = $"new-{Guid.NewGuid():N}"[..12];

        var response = await client.PostAsJsonAsync("api/UserActions", new
        {
            userName = name,
            firstName = "New",
            lastName = "Reader",
            email = $"{name}@test.local",
            password = TestAuth.Password,
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RoleColumnOf(name)).Should().Be(Roles.User);
    }

    [Fact]
    public async Task Register_AskingForSuperAdmin_StillGetsUser()
    {
        using var client = factory.CreateClient();
        var name = $"new-{Guid.NewGuid():N}"[..12];

        var response = await client.PostAsJsonAsync("api/UserActions", new
        {
            userName = name,
            firstName = "Sneaky",
            lastName = "Reader",
            email = $"{name}@test.local",
            password = TestAuth.Password,
            role = Roles.SuperAdmin, // ignored
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RoleColumnOf(name)).Should().Be(Roles.User);
        var loginToken = await factory.GetTokenAsync(name);
        loginToken.Should().NotBeNullOrEmpty();
        using var asNewUser = factory.CreateClient();
        asNewUser.DefaultRequestHeaders.Authorization = new("Bearer", loginToken);
        (await asNewUser.GetAsync("api/User", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<string> RoleColumnOf(string userName) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.UserName == userName).Select(u => u.Role).SingleAsync(Ct));

    [Theory]
    [InlineData(Roles.User)]
    [InlineData(Roles.Admin)]
    public async Task Me_ReturnsMyOwnProfile_ForAnyRole(string role)
    {
        // Bug 8: the frontend loaded the profile with the admin-only GET api/User/{id},
        // so regular users got 403 and their name, avatar and notifications never loaded
        var (client, me) = await factory.LoginAsAsync(role);

        var response = await client.GetAsync("api/UserActions/me", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain(me.Id).And.Contain(me.UserName!);
        // The profile is a DTO, never the raw Identity user (finding 3)
        body.Should().NotContainEquivalentOf("passwordHash").And.NotContainEquivalentOf("securityStamp");
    }

    [Fact]
    public async Task Borrow_LendsTheBookToTheTokensUser()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync();

        var response = await client.PutAsync($"api/UserActions/borrow/{bookId}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var borrower = await factory.WithDbAsync(db =>
            db.CheckOutItems.Where(c => c.BookId == bookId).Select(c => c.UserId).SingleAsync(Ct));
        borrower.Should().Be(me.Id);
    }

    [Fact]
    public async Task CheckedOut_ListsOnlyMyLoans()
    {
        var (client, me) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();
        var myBook = await AddBookAsync();
        var theirBook = await AddBookAsync();
        await AddLoanAsync(myBook, me.Id);
        await AddLoanAsync(theirBook, other.Id);

        var response = await client.GetAsync("api/UserActions/checkedout", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain($"\"bookId\": {myBook}").And.NotContain($"\"bookId\": {theirBook}");
    }

    [Fact]
    public async Task NewReview_IsSavedAsTheTokensUser_NotTheUserIdInTheBody()
    {
        var (client, me) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();
        var bookId = await AddBookAsync();

        var response = await client.PostAsJsonAsync("api/UserActions/newReview", new
        {
            userId = other.Id, // trying to post as someone else
            bookId,
            reviewHeader = "Mine",
            reviewText = "Written by me",
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var author = await factory.WithDbAsync(db =>
            db.ReviewItems.Where(r => r.BookId == bookId).Select(r => r.UserId).SingleAsync(Ct));
        author.Should().Be(me.Id);
    }

    [Fact]
    public async Task EditReview_OfSomeoneElse_Returns403_AndLeavesItUnchanged()
    {
        var (client, _) = await factory.LoginAsAsync();
        var owner = await factory.CreateUserAsync();
        var reviewId = await AddReviewAsync(owner.Id, rating: 7);

        // The body even claims to be the owner: the token decides, not the body
        var response = await client.PutAsJsonAsync($"api/UserActions/editReview/{reviewId}", EditReviewBody(reviewId, owner.Id), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var header = await factory.WithDbAsync(db =>
            db.ReviewItems.Where(r => r.Id == reviewId).Select(r => r.ReviewHeader).SingleAsync(Ct));
        header.Should().Be("Original");
    }

    [Fact]
    public async Task DeleteReview_OfSomeoneElse_Returns403_AndKeepsIt()
    {
        var (client, _) = await factory.LoginAsAsync();
        var owner = await factory.CreateUserAsync();
        var reviewId = await AddReviewAsync(owner.Id);

        var response = await client.DeleteAsync($"api/UserActions/deleteReview/{reviewId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.WithDbAsync(db => db.ReviewItems.AnyAsync(r => r.Id == reviewId, Ct))).Should().BeTrue();
    }

    [Theory]
    [InlineData(Roles.Admin, "PUT", "editReview")]
    [InlineData(Roles.Admin, "DELETE", "deleteReview")]
    [InlineData(Roles.Admin, "PUT", "editRating")]
    [InlineData(Roles.Admin, "DELETE", "deleteRating")]
    [InlineData(Roles.SuperAdmin, "PUT", "editReview")]
    [InlineData(Roles.SuperAdmin, "DELETE", "deleteReview")]
    [InlineData(Roles.SuperAdmin, "PUT", "editRating")]
    [InlineData(Roles.SuperAdmin, "DELETE", "deleteRating")]
    public async Task ReviewActions_OnSomeoneElsesReview_AsAdmin_Return403(string role, string method, string action)
    {
        // These endpoints are own-things-only for every role. Admin moderation
        // of other people's reviews gets its own admin endpoints (#50).
        var (client, _) = await factory.LoginAsAsync(role);
        var owner = await factory.CreateUserAsync();
        var reviewId = await AddReviewAsync(owner.Id, rating: 7);
        object body = action == "editRating" ? new { rating = 1 } : EditReviewBody(reviewId, owner.Id);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"api/UserActions/{action}/{reviewId}")
        {
            Content = JsonContent.Create(body)
        };

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.WithDbAsync(db => db.RatingItems.AnyAsync(r => r.ReviewItemId == reviewId && r.Rating == 7, Ct))).Should().BeTrue();
    }

    [Fact]
    public async Task EditRating_OfSomeoneElse_Returns403()
    {
        var (client, _) = await factory.LoginAsAsync();
        var owner = await factory.CreateUserAsync();
        var reviewId = await AddReviewAsync(owner.Id, rating: 7);

        var response = await client.PutAsJsonAsync($"api/UserActions/editRating/{reviewId}", new { rating = 1 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var rating = await factory.WithDbAsync(db =>
            db.RatingItems.Where(r => r.ReviewItemId == reviewId).Select(r => r.Rating).SingleAsync(Ct));
        rating.Should().Be(7);
    }

    [Fact]
    public async Task DeleteRating_OfSomeoneElse_Returns403()
    {
        var (client, _) = await factory.LoginAsAsync();
        var owner = await factory.CreateUserAsync();
        var reviewId = await AddReviewAsync(owner.Id, rating: 7);

        var response = await client.DeleteAsync($"api/UserActions/deleteRating/{reviewId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.WithDbAsync(db => db.RatingItems.AnyAsync(r => r.ReviewItemId == reviewId, Ct))).Should().BeTrue();
    }

    [Fact]
    public async Task EditAndDeleteReview_OfMyOwn_Works()
    {
        var (client, me) = await factory.LoginAsAsync();
        var reviewId = await AddReviewAsync(me.Id, rating: 7);

        var edit = await client.PutAsJsonAsync($"api/UserActions/editReview/{reviewId}", EditReviewBody(reviewId, me.Id), Ct);
        var delete = await client.DeleteAsync($"api/UserActions/deleteReview/{reviewId}", Ct);

        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ReviewItemDto requires userId and book, so a valid edit request carries them
    private static object EditReviewBody(int reviewId, string userId) => new
    {
        id = reviewId,
        userId,
        book = new { title = "Any", author = "Any" },
        reviewHeader = "Changed",
        reviewText = "Changed",
    };

    private Task<int> AddBookAsync() => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book($"Book {Guid.NewGuid():N}"[..13]);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });

    private Task AddLoanAsync(int bookId, string userId) => factory.WithDbAsync(async db =>
    {
        db.CheckOutItems.Add(new()
        {
            BookId = bookId,
            UserId = userId,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(14),
        });
        var book = await db.Books.SingleAsync(b => b.BookId == bookId, Ct);
        book.BookStatus = BookStatusEnum.CheckedOut;
        await db.SaveChangesAsync(Ct);
    });

    private async Task<int> AddReviewAsync(string userId, int? rating = null)
    {
        var bookId = await AddBookAsync();
        return await factory.WithDbAsync(async db =>
        {
            var review = new ReviewItem
            {
                UserId = userId,
                BookId = bookId,
                ReviewHeader = "Original",
                ReviewText = "Original text",
                CreatedAt = DateTime.UtcNow,
            };
            db.ReviewItems.Add(review);
            await db.SaveChangesAsync(Ct);
            if (rating is int value)
            {
                db.RatingItems.Add(new RatingItem { ReviewItemId = review.Id, Rating = value, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync(Ct);
            }
            return review.Id;
        });
    }
}
