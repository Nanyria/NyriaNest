using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Registering, or a SuperAdmin creating an admin, with an email or username another
/// account already has is turned down with 400 and a message, and no account is created.
/// </summary>
public class TakenEmailOrUserNameTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_WithATakenEmail_Returns400_AndCreatesNoAccount()
    {
        var existing = await factory.CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await Register(client, NewName(), existing.Email!);

        await ShouldBeTurnedDown(response, "Email already exists.");
        (await AccountsWithEmail(existing.Email!)).Should().Be(1);
    }

    [Fact]
    public async Task Register_WithATakenUserName_Returns400_AndCreatesNoAccount()
    {
        var existing = await factory.CreateUserAsync();
        using var client = factory.CreateClient();
        var email = $"{NewName()}@test.local";

        var response = await Register(client, existing.UserName!, email);

        await ShouldBeTurnedDown(response, "UserName already exists.");
        (await AccountsWithEmail(email)).Should().Be(0);
    }

    [Fact]
    public async Task CreateAdmin_WithATakenEmail_Returns400_AndCreatesNoAccount()
    {
        var existing = await factory.CreateUserAsync();
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);

        var response = await CreateAdmin(superAdmin, NewName(), existing.Email!);

        await ShouldBeTurnedDown(response, "Email already exists.");
        (await AccountsWithEmail(existing.Email!)).Should().Be(1);
    }

    [Fact]
    public async Task CreateAdmin_WithATakenUserName_Returns400_AndCreatesNoAccount()
    {
        var existing = await factory.CreateUserAsync();
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var email = $"{NewName()}@test.local";

        var response = await CreateAdmin(superAdmin, existing.UserName!, email);

        await ShouldBeTurnedDown(response, "UserName already exists.");
        (await AccountsWithEmail(email)).Should().Be(0);
    }

    private static string NewName() => $"new-{Guid.NewGuid():N}"[..12];

    private static Task<HttpResponseMessage> Register(HttpClient client, string userName, string email) =>
        client.PostAsJsonAsync("api/UserActions", new
        {
            userName,
            firstName = "New",
            lastName = "Reader",
            email,
            password = TestAuth.Password,
        }, Ct);

    private static Task<HttpResponseMessage> CreateAdmin(HttpClient client, string userName, string email) =>
        client.PostAsJsonAsync("api/User/create-admin", new
        {
            userName,
            firstName = "New",
            lastName = "Admin",
            email,
            password = TestAuth.Password,
            role = Roles.Admin,
        }, Ct);

    private static async Task ShouldBeTurnedDown(HttpResponseMessage response, string message)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errorMessages").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(message);
    }

    private Task<int> AccountsWithEmail(string email) => factory.WithDbAsync(db =>
        db.Users.CountAsync(u => u.Email == email, Ct));
}
