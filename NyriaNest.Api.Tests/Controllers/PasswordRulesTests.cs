using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #68: one set of password rules for register and change password, the same
/// ones the frontend shows: at least 8 characters, with an uppercase letter,
/// a lowercase letter, a digit and a symbol.
/// </summary>
public class PasswordRulesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ValidPassword = "Abcdef1!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Each breaks exactly one rule
    public static TheoryData<string, string> WeakPasswords => new()
    {
        { "Abcde1!", "shorter than 8 characters" },
        { "abcdef1!", "no uppercase letter" },
        { "ABCDEF1!", "no lowercase letter" },
        { "Abcdefg!", "no digit" },
        { "Abcdefg1", "no symbol" },
    };

    [Theory]
    [MemberData(nameof(WeakPasswords))]
    public async Task Register_WithAWeakPassword_Returns400(string password, string because)
    {
        var response = await Register(password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task Register_WithAPasswordMeetingAllRules_Works()
    {
        var response = await Register(ValidPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [MemberData(nameof(WeakPasswords))]
    public async Task ChangePassword_ToAWeakOne_Returns400_AndKeepsTheOldOne(string password, string because)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync("api/UserActions/update",
            new { password, currentPassword = TestAuth.Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
        (await CanLogIn(me.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_ToOneMeetingAllRules_Works()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync("api/UserActions/update",
            new { password = ValidPassword, currentPassword = TestAuth.Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CanLogIn(me.UserName!, ValidPassword)).Should().BeTrue();
    }

    private async Task<HttpResponseMessage> Register(string password)
    {
        using var client = factory.CreateClient();
        var name = $"pw-{Guid.NewGuid():N}"[..12];
        return await client.PostAsJsonAsync("api/UserActions", new
        {
            userName = name,
            firstName = "New",
            lastName = "Reader",
            email = $"{name}@test.local",
            password,
        }, Ct);
    }

    private async Task<bool> CanLogIn(string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostLoginAsync(username, password);
        return response.IsSuccessStatusCode;
    }
}
