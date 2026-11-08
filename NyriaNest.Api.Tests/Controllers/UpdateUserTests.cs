using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Net.Http.Json;

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

    [Fact]
    public async Task ChangeEmail_ToOneAnotherAccountHas_Returns400_AndKeepsTheOldOne()
    {
        var (client, me) = await factory.LoginAsAsync();
        var other = await factory.CreateUserAsync();

        // Different case, same address
        var response = await client.PutAsJsonAsync(Url,
            new { email = other.Email!.ToUpperInvariant(), currentPassword = TestAuth.Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EmailOf(me.Id)).Should().Be(me.Email);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ChangeEmail_ToAnInvalidOne_Returns400_AndKeepsTheOldOne(string email)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new { email, currentPassword = TestAuth.Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EmailOf(me.Id)).Should().Be(me.Email);
    }

    [Fact]
    public async Task Update_PictureAndBio_NeedsNoPassword()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url,
            new { profilePictureUrl = "assets/images/profile-pictures/RedDragon.svg", bio = "Dragons" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (picture, bio) = await factory.WithDbAsync(db => db.Users
            .Where(u => u.Id == me.Id)
            .Select(u => new ValueTuple<string?, string?>(u.ProfilePictureUrl, u.Bio))
            .SingleAsync(Ct));
        picture.Should().Be("assets/images/profile-pictures/RedDragon.svg");
        bio.Should().Be("Dragons");
    }

    [Fact]
    public async Task Update_WithABioOf300Characters_Works()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bio = new string('a', 300);

        var response = await client.PutAsJsonAsync(Url, new { bio }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BioOf(me.Id)).Should().Be(bio);
    }

    [Fact]
    public async Task Update_WithABioLongerThan300Characters_Returns400_AndKeepsTheOldOne()
    {
        var (client, me) = await factory.LoginAsAsync();
        await client.PutAsJsonAsync(Url, new { bio = "Short bio" }, Ct);

        var response = await client.PutAsJsonAsync(Url, new { bio = new string('a', 301) }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("The bio can be at most 300 characters.");
        (await BioOf(me.Id)).Should().Be("Short bio");
    }

    [Theory]
    [InlineData("firstName", "")]
    [InlineData("firstName", "   ")]
    [InlineData("lastName", "")]
    [InlineData("lastName", "   ")]
    public async Task Update_WithAnEmptyName_Returns400_AndKeepsTheOldOne(string field, string value)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(Url, new Dictionary<string, string> { [field] = value }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (first, last) = await factory.WithDbAsync(db => db.Users
            .Where(u => u.Id == me.Id)
            .Select(u => new ValueTuple<string, string>(u.FirstName, u.LastName))
            .SingleAsync(Ct));
        first.Should().Be(me.FirstName);
        last.Should().Be(me.LastName);
    }

    private Task<string> FirstNameOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.FirstName).SingleAsync(Ct));

    private Task<string?> BioOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.Bio).SingleAsync(Ct));

    private Task<string?> EmailOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(Ct));

    private async Task<bool> CanLogIn(string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostLoginAsync(username, password);
        return response.IsSuccessStatusCode;
    }
}
