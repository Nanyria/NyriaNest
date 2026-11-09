using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Accounts;

/// <summary>
/// Security finding 1 (#48): every api/UserActions endpoint needs a login, registering
/// always gives the User role, and "me" returns only my own profile.
/// </summary>
public class AccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
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
}
