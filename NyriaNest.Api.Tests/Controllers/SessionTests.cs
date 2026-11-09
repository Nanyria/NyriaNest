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
using System.Text.Json;
using System.Web;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #113: changing the password or email ends every other session. Logins are JWTs, so
/// each token carries the user's security stamp, which Identity changes on every
/// password and email change, and the API rejects a token whose stamp is out of date.
/// The device the change was made on gets a new token back and stays logged in.
/// </summary>
public class SessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UpdateUrl = "api/UserActions/update";
    private const string NewPassword = "Session-Password-321!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangingThePassword_EndsTheOtherSessions()
    {
        var user = await factory.CreateUserAsync();
        var phone = await factory.GetTokenAsync(user.UserName!);
        var laptop = await factory.GetTokenAsync(user.UserName!);

        var change = await Put(laptop, new { password = NewPassword, currentPassword = TestAuth.Password });

        change.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MeWith(phone)).Should().Be(HttpStatusCode.Unauthorized);
        (await MeWith(laptop)).Should().Be(HttpStatusCode.Unauthorized, "the token used for the change is old too");
    }

    [Fact]
    public async Task AnEndedSession_Says_WhyInAHeader()
    {
        // So the app can tell "your password or email was changed" from an expired login
        var user = await factory.CreateUserAsync();
        var phone = await factory.GetTokenAsync(user.UserName!);
        var laptop = await factory.GetTokenAsync(user.UserName!);
        await Put(laptop, new { password = NewPassword, currentPassword = TestAuth.Password });
        using var client = ClientWith(phone);

        var response = await client.GetAsync("api/UserActions/me", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("X-Session-Ended").Should().Equal("credentials-changed");
    }

    [Fact]
    public async Task ABadToken_GetsNoSessionEndedHeader()
    {
        using var client = ClientWith("not-a-token");

        var response = await client.GetAsync("api/UserActions/me", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("X-Session-Ended").Should().BeFalse();
    }

    [Fact]
    public async Task ChangingThePassword_ReturnsANewToken_ThatKeepsWorking()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.GetTokenAsync(user.UserName!);

        var change = await Put(token, new { password = NewPassword, currentPassword = TestAuth.Password });

        var newToken = await TokenIn(change);
        newToken.Should().NotBeNullOrEmpty();
        (await MeWith(newToken!)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangingTheEmail_EndsTheOtherSessions_AndReturnsANewToken()
    {
        var user = await factory.CreateUserAsync();
        var other = await factory.GetTokenAsync(user.UserName!);
        var token = await factory.GetTokenAsync(user.UserName!);

        var change = await Put(token, new { email = $"moved-{user.UserName}@test.local", currentPassword = TestAuth.Password });

        change.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MeWith(other)).Should().Be(HttpStatusCode.Unauthorized);
        var newToken = await TokenIn(change);
        (await MeWith(newToken!)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangingNameOrBio_KeepsEverySession_AndReturnsNoNewToken()
    {
        var user = await factory.CreateUserAsync();
        var other = await factory.GetTokenAsync(user.UserName!);
        var token = await factory.GetTokenAsync(user.UserName!);

        var change = await Put(token, new { firstName = "Renamed", bio = "Still here" });

        change.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MeWith(other)).Should().Be(HttpStatusCode.OK);
        (await MeWith(token)).Should().Be(HttpStatusCode.OK);
        (await TokenIn(change)).Should().BeNull();
    }

    [Fact]
    public async Task AFailedPasswordChange_KeepsTheSessions()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.GetTokenAsync(user.UserName!);

        var change = await Put(token, new { password = NewPassword, currentPassword = "Wrong-Password-1!" });

        change.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MeWith(token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResettingAForgottenPassword_EndsEverySession()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.GetTokenAsync(user.UserName!);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("api/Auth/forgot-password", new { email = user.Email }, Ct)).EnsureSuccessStatusCode();
        var link = factory.Email.Invocations
            .Where(i => i.Method.Name == "SendPasswordResetEmailAsync" && (string)i.Arguments[0] == user.Email)
            .Select(i => (string)i.Arguments[2]).Last();
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);

        var reset = await anonymous.PostAsJsonAsync("api/Auth/reset-password",
            new { email = query["email"], token = query["token"], newPassword = NewPassword }, Ct);

        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MeWith(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASuperAdminSettingThePassword_EndsTheUsersSessions()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.GetTokenAsync(user.UserName!);
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);

        var set = await superAdmin.PutAsJsonAsync($"api/User/updateAsAdmin/{user.Id}", new { password = NewPassword }, Ct);

        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MeWith(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OtherUsers_StayLoggedIn()
    {
        var me = await factory.CreateUserAsync();
        var someoneElse = await factory.CreateUserAsync();
        var mine = await factory.GetTokenAsync(me.UserName!);
        var theirs = await factory.GetTokenAsync(someoneElse.UserName!);

        await Put(mine, new { password = NewPassword, currentPassword = TestAuth.Password });

        (await MeWith(theirs)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATokenWithoutASecurityStamp_Returns401()
    {
        // Tokens issued before #113 have no stamp: they can't be checked, so they're refused
        var user = await factory.CreateUserAsync();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey));
        var jwt = new JwtSecurityToken(
            issuer: ApiFactory.JwtIssuer,
            audience: ApiFactory.JwtAudience,
            claims: [new Claim(ClaimTypes.Name, user.UserName!), new Claim("userId", user.Id), new Claim(ClaimTypes.Role, Roles.User)],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var stampless = new JwtSecurityTokenHandler().WriteToken(jwt);

        (await MeWith(stampless)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AFreshLogin_Works()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.GetTokenAsync(user.UserName!);

        (await MeWith(token)).Should().Be(HttpStatusCode.OK);
    }

    // ---- helpers ----

    private HttpClient ClientWith(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpResponseMessage> Put(string token, object body)
    {
        using var client = ClientWith(token);
        return await client.PutAsJsonAsync(UpdateUrl, body, Ct);
    }

    private async Task<HttpStatusCode> MeWith(string token)
    {
        using var client = ClientWith(token);
        return (await client.GetAsync("api/UserActions/me", Ct)).StatusCode;
    }

    // The new token sent back with a password or email change (result.token), if any
    private static async Task<string?> TokenIn(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.TryGetProperty("result", out var result)
               && result.ValueKind == JsonValueKind.Object
               && result.TryGetProperty("token", out var token)
               && token.ValueKind == JsonValueKind.String
            ? token.GetString()
            : null;
    }
}
