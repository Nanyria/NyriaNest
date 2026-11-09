using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Users;

/// <summary>
/// #116: pins down creating an admin, getting one user and a user's history (api/User)
/// before UserService moves to UserAdminService.
/// </summary>
public class UserAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- create-admin ----

    [Fact]
    public async Task CreateAdmin_AsSuperAdmin_CreatesAnAdminWhoCanLogIn()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var name = NewName();

        var response = await CreateAdmin(superAdmin, name, Roles.Admin);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = await IdOf(name);
        (await RolesOf(userId!)).Should().Equal(Roles.Admin);
        using var asNewAdmin = factory.CreateClient();
        asNewAdmin.DefaultRequestHeaders.Authorization = new("Bearer", await factory.GetTokenAsync(name));
        (await asNewAdmin.GetAsync("api/User", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateAdmin_AsAdmin_Returns403_AndCreatesNoAccount()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var name = NewName();

        var response = await CreateAdmin(admin, name, Roles.Admin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IdOf(name)).Should().BeNull();
    }

    [Fact]
    public async Task CreateAdmin_WithAnUnknownRole_Returns400_AndCreatesNoAccount()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var name = NewName();

        var response = await CreateAdmin(superAdmin, name, "Wizard");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IdOf(name)).Should().BeNull();
    }

    // ---- one user ----

    [Fact]
    public async Task GetUserById_ReturnsTheUserAsAUserDto()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync();

        var response = await admin.GetAsync($"api/User/{target.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain($"\"userId\": \"{target.Id}\"");
        foreach (var field in new[] { "passwordHash", "securityStamp", "concurrencyStamp" })
            body.Should().NotContainEquivalentOf(field);
    }

    [Fact]
    public async Task GetUserById_UnknownId_Returns404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"api/User/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- a user's history ----

    [Fact(Skip = "Bug #121: user history is always empty")]
    public async Task GetUserHistory_ReturnsOnlyThatUsersHistory_NewestFirst()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync();
        var someoneElse = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            var book = TestData.Book();
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            db.StatusHistoryItems.AddRange(
                new() { BookId = book.BookId, UserId = target.Id, BookStatus = BookStatusEnum.CheckedOut, Timestamp = DateTime.UtcNow.AddDays(-2), Notes = "older" },
                new() { BookId = book.BookId, UserId = target.Id, BookStatus = BookStatusEnum.Returned, Timestamp = DateTime.UtcNow.AddDays(-1), Notes = "newer" },
                new() { BookId = book.BookId, UserId = someoneElse.Id, BookStatus = BookStatusEnum.CheckedOut, Notes = "not theirs" });
            await db.SaveChangesAsync(Ct);
        });

        var response = await admin.GetAsync($"api/User/history/{target.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("result").EnumerateArray().Select(h => h.GetProperty("notes").GetString())
            .Should().Equal("newer", "older");
    }

    [Fact]
    public async Task GetUserHistory_UnknownId_Returns404()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"api/User/history/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string NewName() => $"adm-{Guid.NewGuid():N}"[..12];

    private static Task<HttpResponseMessage> CreateAdmin(HttpClient client, string userName, string role) =>
        client.PostAsJsonAsync("api/User/create-admin", new
        {
            userName,
            firstName = "New",
            lastName = "Admin",
            email = $"{userName}@test.local",
            password = TestAuth.Password,
            role,
        }, Ct);

    private Task<string?> IdOf(string userName) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleOrDefaultAsync(Ct));

    private Task<List<string>> RolesOf(string userId) => factory.WithDbAsync(db =>
        db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(Ct));
}
