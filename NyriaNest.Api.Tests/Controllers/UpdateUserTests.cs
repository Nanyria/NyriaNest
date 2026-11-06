using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Security finding 2 (#48): PUT api/UserActions/update changes only the logged-in user,
/// and changing the password or email needs the current password.
/// </summary>
public class UpdateUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Url = "api/UserActions/update";
    private const string NewPassword = "New-Password-456!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(Url, new { firstName = "X" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_Name_NeedsNoPassword_AndChangesOnlyMe()
    {
        var (client, me) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();

        var response = await client.PutAsJsonAsync(Url, new { firstName = "Renamed", bio = "Hello" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FirstNameOf(me.Id)).Should().Be("Renamed");
        (await FirstNameOf(other.Id)).Should().Be("Test");
    }

    [Fact]
    public async Task Update_WithAnotherUsersIdInTheUrl_DoesNotChangeThem()
    {
        // The old route, update/{userId}, let any user change anyone
        var (client, _) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();

        await client.PutAsJsonAsync($"{Url}/{other.Id}", new { firstName = "Hacked" }, Ct);

        (await FirstNameOf(other.Id)).Should().Be("Test");
    }

    [Fact]
    public async Task ChangePassword_WithoutCurrentPassword_Returns400_AndKeepsTheOldOne()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new { password = NewPassword }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CanLogIn(me.UserName!, TestAuth.Password)).Should().BeTrue();
        (await CanLogIn(me.UserName!, NewPassword)).Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns400_AndKeepsTheOldOne()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new { password = NewPassword, currentPassword = "Wrong-123!" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("The current password you entered is incorrect.");
        (await CanLogIn(me.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WithCurrentPassword_Works()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new { password = NewPassword, currentPassword = TestAuth.Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CanLogIn(me.UserName!, NewPassword)).Should().BeTrue();
        (await CanLogIn(me.UserName!, TestAuth.Password)).Should().BeFalse();
    }

    [Fact]
    public async Task ChangeEmail_WithoutCurrentPassword_Returns400_AndKeepsTheOldOne()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new { email = "attacker@test.local" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EmailOf(me.Id)).Should().Be(me.Email);
    }

    [Fact]
    public async Task ChangeEmail_WithCurrentPassword_Works()
    {
        var (client, me) = await factory.LoginAsAsync();
        var newEmail = $"new-{me.UserName}@test.local";

        var response = await client.PutAsJsonAsync(Url, new { email = newEmail, currentPassword = TestAuth.Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EmailOf(me.Id)).Should().Be(newEmail);
    }

    [Fact]
    public async Task Update_WithMyUnchangedEmail_NeedsNoPassword()
    {
        // The profile page sends the whole user, email included
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new { email = me.Email, firstName = "Same email" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<string> FirstNameOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.FirstName).SingleAsync(Ct));

    private Task<string?> EmailOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(Ct));

    private async Task<bool> CanLogIn(string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostLoginAsync(username, password);
        return response.IsSuccessStatusCode;
    }
}
