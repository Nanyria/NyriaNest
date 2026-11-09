using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #51: a forgotten password is reset through an emailed link, never by emailing a
/// password. POST api/Auth/forgot-password sends the link; POST api/Auth/reset-password
/// sets the new password with the link's one-time token. Both work without a login,
/// and neither tells anyone whether an account exists.
/// </summary>
public class PasswordResetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ForgotUrl = "api/Auth/forgot-password";
    private const string ResetUrl = "api/Auth/reset-password";
    private const string NewPassword = "Reset-Password-789!";
    private const string SentMessage = "If an account exists for that email, we've sent a link to reset your password.";
    private const string InvalidLinkMessage = "This reset link is invalid or has expired.";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- forgot-password ----

    [Fact]
    public async Task Forgot_ForAKnownEmail_Returns200_AndEmailsALinkToTheResetPage()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient(); // no login

        var response = await client.PostAsJsonAsync(ForgotUrl, new { email = user.Email }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessageOf(response)).Should().Be(SentMessage);
        var link = SentLinkFor(user.Email!);
        link.Should().StartWith($"{ApiFactory.FrontendBaseUrl}/reset-password?");
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        query["email"].Should().Be(user.Email);
        query["token"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Forgot_ForAnUnknownEmail_Returns200_WithTheSameMessage_AndSendsNothing()
    {
        using var client = factory.CreateClient();
        var unknown = $"nobody-{Guid.NewGuid():N}@test.local";

        var response = await client.PostAsJsonAsync(ForgotUrl, new { email = unknown }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessageOf(response)).Should().Be(SentMessage);
        factory.Email.Verify(e => e.SendPasswordResetEmailAsync(unknown, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Forgot_TheEmailAndTheResponse_NeverContainThePassword()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(ForgotUrl, new { email = user.Email }, Ct);

        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain(TestAuth.Password);
        var sent = factory.Email.Invocations.Where(i => i.Arguments.Contains(user.Email)).SelectMany(i => i.Arguments);
        sent.OfType<string>().Should().NotContain(s => s.Contains(TestAuth.Password));
    }

    [Fact]
    public async Task Forgot_WithoutAnEmail_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(ForgotUrl, new { email = "" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- reset-password ----

    [Fact]
    public async Task Reset_WithTheLinksToken_SetsTheNewPassword_AndTheOldOneStopsWorking()
    {
        var user = await factory.CreateUserAsync();
        var (email, token) = await RequestLinkAsync(user.Email!);
        using var client = factory.CreateClient(); // no login

        var response = await client.PostAsJsonAsync(ResetUrl, new { email, token, newPassword = NewPassword }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CanLogIn(user.UserName!, NewPassword)).Should().BeTrue();
        (await CanLogIn(user.UserName!, TestAuth.Password)).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_UsingTheSameLinkTwice_Returns400_TheSecondTime()
    {
        var user = await factory.CreateUserAsync();
        var (email, token) = await RequestLinkAsync(user.Email!);
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(ResetUrl, new { email, token, newPassword = NewPassword }, Ct);

        var again = await client.PostAsJsonAsync(ResetUrl, new { email, token, newPassword = "Another-Password-1!" }, Ct);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsOf(again)).Should().Contain(InvalidLinkMessage);
        (await CanLogIn(user.UserName!, NewPassword)).Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("%%%")]
    [InlineData("")]
    public async Task Reset_WithAWrongToken_Returns400_AndKeepsThePassword(string token)
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(ResetUrl, new { email = user.Email, token, newPassword = NewPassword }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CanLogIn(user.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Reset_WithAnotherUsersToken_Returns400()
    {
        var me = await factory.CreateUserAsync();
        var other = await factory.CreateUserAsync();
        var (_, othersToken) = await RequestLinkAsync(other.Email!);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(ResetUrl, new { email = me.Email, token = othersToken, newPassword = NewPassword }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CanLogIn(me.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Reset_ForAnUnknownEmail_Returns400_WithTheSameMessageAsABadLink()
    {
        var user = await factory.CreateUserAsync();
        var (_, token) = await RequestLinkAsync(user.Email!);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(ResetUrl,
            new { email = $"nobody-{Guid.NewGuid():N}@test.local", token, newPassword = NewPassword }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsOf(response)).Should().Equal(InvalidLinkMessage);
    }

    [Fact]
    public async Task Reset_ToAWeakPassword_Returns400_WithTheRules_AndTheLinkStillWorks()
    {
        var user = await factory.CreateUserAsync();
        var (email, token) = await RequestLinkAsync(user.Email!);
        using var client = factory.CreateClient();

        var weak = await client.PostAsJsonAsync(ResetUrl, new { email, token, newPassword = "short" }, Ct);

        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsOf(weak)).Should().NotBeEmpty().And.NotContain(InvalidLinkMessage);
        (await CanLogIn(user.UserName!, TestAuth.Password)).Should().BeTrue();
        // A failed attempt doesn't use up the link
        var retry = await client.PostAsJsonAsync(ResetUrl, new { email, token, newPassword = NewPassword }, Ct);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void ResetLinks_ExpireAfterOneHour()
    {
        var options = factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;

        options.TokenLifespan.Should().Be(TimeSpan.FromHours(1));
    }

    // ---- rate limit ----

    [Fact]
    public async Task ForgotAndReset_AreRateLimitedPerIp()
    {
        // A server of its own with a low limit, so the other tests never hit it
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PasswordResetPermitLimit", "3"));
        using var client = limited.CreateClient();
        var email = $"nobody-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync(ForgotUrl, new { email }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync(ForgotUrl, new { email }, Ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.PostAsJsonAsync(ResetUrl, new { email, token = "x", newPassword = NewPassword }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ---- helpers ----

    private string SentLinkFor(string email) =>
        factory.Email.Invocations
            .Where(i => i.Method.Name == nameof(NyriaNest.Application.Interfaces.IEmailService.SendPasswordResetEmailAsync)
                        && (string)i.Arguments[0] == email)
            .Select(i => (string)i.Arguments[2])
            .Last();

    // Asks for a link and takes email and token out of it, like the reset page does
    private async Task<(string Email, string Token)> RequestLinkAsync(string email)
    {
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync(ForgotUrl, new { email }, Ct)).EnsureSuccessStatusCode();
        var query = HttpUtility.ParseQueryString(new Uri(SentLinkFor(email)).Query);
        return (query["email"]!, query["token"]!);
    }

    private static async Task<string?> MessageOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("result").GetString();
    }

    private static async Task<List<string>> ErrorsOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.TryGetProperty("errorMessages", out var errors)
            ? errors.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
    }

    private async Task<bool> CanLogIn(string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostLoginAsync(username, password);
        return response.IsSuccessStatusCode;
    }
}
