using AwesomeAssertions;
using Microsoft.IdentityModel.Tokens;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace NyriaNest.Api.Tests.Controllers.Accounts;

/// <summary>
/// Login, and that the JWT it hands out is what [Authorize] accepts – and nothing else is.
/// GET api/User (Admin or SuperAdmin only) is used as the protected endpoint.
/// </summary>
public class AuthControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AdminOnlyEndpoint = "api/User";

    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsTokenWithUserIdAndRole()
    {
        var user = await factory.CreateUserAsync(Roles.User);
        using var client = factory.CreateClient();

        var response = await client.PostLoginAsync(user.UserName!, TestAuth.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TestAuth.LoginResponse>(TestContext.Current.CancellationToken);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.Token);

        jwt.Issuer.Should().Be(ApiFactory.JwtIssuer);
        jwt.Audiences.Should().Contain(ApiFactory.JwtAudience);
        jwt.Claims.Should().Contain(c => c.Type == "userId" && c.Value == user.Id);
        // The token handler shortens ClaimTypes.Role to "role" when it writes the token
        jwt.Claims.Should().Contain(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == Roles.User);
        body.Expiration.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await client.PostLoginAsync(user.UserName!, "Wrong-Password-123!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownUser_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostLoginAsync("nobody-by-this-name", TestAuth.Password);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SeededSuperAdmin_CanLogIn()
    {
        // Startup seeds the SuperAdmin from SeedAdmin:* – here the test values, never your real ones
        using var client = factory.CreateClient();

        var response = await client.PostLoginAsync("Nanyria", ApiFactory.SeedAdminPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithAdminToken_IsAllowed()
    {
        var (client, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await client.GetAsync(AdminOnlyEndpoint, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithUserToken_Returns403()
    {
        var (client, _) = await factory.LoginAsAsync(Roles.User);

        var response = await client.GetAsync(AdminOnlyEndpoint, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(AdminOnlyEndpoint, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenSignedByAnotherKey_Returns401()
    {
        // Same claims an admin token would have, but not signed with the API's key:
        // proves the signature is checked, not just the token's contents.
        var forged = new JwtSecurityToken(
            issuer: ApiFactory.JwtIssuer,
            audience: ApiFactory.JwtAudience,
            claims: [new Claim(ClaimTypes.Role, Roles.SuperAdmin)],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-different-key-that-is-at-least-32-characters-long")),
                SecurityAlgorithms.HmacSha256));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(forged));

        var response = await client.GetAsync(AdminOnlyEndpoint, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
