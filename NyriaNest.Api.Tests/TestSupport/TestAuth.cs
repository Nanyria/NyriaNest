using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models.Users;

namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>
/// Logs in through the real POST api/Auth/login, the same way the Angular app does.
/// Nothing in the API's authentication is bypassed.
/// </summary>
public static class TestAuth
{
    public const string Password = "Test-Password-123!";

    /// <summary>Creates a user with a unique username and the given role, straight in the test database.</summary>
    public static async Task<User> CreateUserAsync(this ApiFactory factory, string role = Roles.User)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // A neutral prefix plus a GUID keeps usernames unique and within Identity's allowed characters.
        var user = TestData.User($"user-{Guid.NewGuid():N}"[..13], role);
        Check(await userManager.CreateAsync(user, Password));
        Check(await userManager.AddToRoleAsync(user, role));
        return user;
    }

    /// <summary>Calls the login endpoint and returns the response (for tests of login itself).</summary>
    public static Task<HttpResponseMessage> PostLoginAsync(this HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("api/Auth/login", new { username, password }, TestContext.Current.CancellationToken);

    /// <summary>Logs in and returns the JWT.</summary>
    public static async Task<string> GetTokenAsync(this ApiFactory factory, string username, string password = Password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostLoginAsync(username, password);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        return body!.Token;
    }

    /// <summary>
    /// A new user with <paramref name="role"/>, logged in. The client sends the token on every request.
    /// <code>var (client, user) = await factory.LoginAsAsync(Roles.Admin);</code>
    /// </summary>
    public static async Task<(HttpClient Client, User User)> LoginAsAsync(this ApiFactory factory, string role = Roles.User)
    {
        var user = await factory.CreateUserAsync(role);
        var token = await factory.GetTokenAsync(user.UserName!);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, user);
    }

    public sealed record LoginResponse(string Token, DateTime Expiration);

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
