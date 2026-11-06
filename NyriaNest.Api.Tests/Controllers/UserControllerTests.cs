using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Security finding 4 (#48): what an Admin and a SuperAdmin may do to other users
/// through PUT api/User/updateAsAdmin/{userId} and DELETE api/User/{userId}.
///
/// | What                                   | Admin              | SuperAdmin                      |
/// |----------------------------------------|--------------------|---------------------------------|
/// | Name, username, email of a regular user| yes                | yes                             |
/// | Another Admin's or a SuperAdmin's      | no                 | yes, but not another SuperAdmin |
/// | Set someone's password                 | no                 | yes                             |
/// | Change a role                          | no                 | yes, but not their own          |
/// | Delete a user                          | regular users only | anyone except a SuperAdmin      |
/// </summary>
public class UserControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NewPassword = "New-Password-456!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Admin ----

    [Fact]
    public async Task Admin_CanRenameARegularUser()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await Update(admin, target.Id, new { firstName = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FirstNameOf(target.Id)).Should().Be("Renamed");
    }

    [Fact]
    public async Task Admin_SendingTheUnchangedRole_IsNotARoleChange()
    {
        // The Manage users page sends the whole user, role included
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await Update(admin, target.Id, new { firstName = "Renamed", role = Roles.User });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_CantSetARegularUsersPassword()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await Update(admin, target.Id, new { password = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CanLogIn(target.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_CantChangeARegularUsersRole()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await Update(admin, target.Id, new { role = Roles.Admin });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RolesOf(target.Id)).Should().Equal(Roles.User);
    }

    [Fact]
    public async Task Admin_CantMakeThemselvesSuperAdmin()
    {
        var (admin, me) = await factory.LoginAsAsync(Roles.Admin);

        var response = await Update(admin, me.Id, new { role = Roles.SuperAdmin });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RolesOf(me.Id)).Should().Equal(Roles.Admin);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_CantChangeAnAdminOrSuperAdmin(string targetRole)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(targetRole);

        var rename = await Update(admin, target.Id, new { firstName = "Hacked" });
        var password = await Update(admin, target.Id, new { password = NewPassword });

        rename.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        password.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await FirstNameOf(target.Id)).Should().Be("Test");
        (await CanLogIn(target.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_CanDeleteARegularUser()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await admin.DeleteAsync($"api/User/{target.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Exists(target.Id)).Should().BeFalse();
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_CantDeleteAnAdminOrSuperAdmin(string targetRole)
    {
        // This is how the SuperAdmin got deleted from Manage users
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(targetRole);

        var response = await admin.DeleteAsync($"api/User/{target.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Exists(target.Id)).Should().BeTrue();
    }

    // ---- SuperAdmin ----

    [Fact]
    public async Task SuperAdmin_CanMakeAUserAdmin()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await Update(superAdmin, target.Id, new { role = Roles.Admin });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RolesOf(target.Id)).Should().Equal(Roles.Admin);
    }

    [Fact]
    public async Task SuperAdmin_CanSetAUsersPassword()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var target = await factory.CreateUserAsync(Roles.Admin);

        var response = await Update(superAdmin, target.Id, new { password = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CanLogIn(target.UserName!, NewPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_CantChangeTheirOwnRole()
    {
        // Otherwise the only SuperAdmin could lock everyone out of user management
        var (superAdmin, me) = await factory.LoginAsAsync(Roles.SuperAdmin);

        var response = await Update(superAdmin, me.Id, new { role = Roles.User });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RolesOf(me.Id)).Should().Equal(Roles.SuperAdmin);
    }

    [Fact]
    public async Task SuperAdmin_UnknownRole_Returns400()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await Update(superAdmin, target.Id, new { role = "Overlord" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RolesOf(target.Id)).Should().Equal(Roles.User);
    }

    [Fact]
    public async Task SuperAdmin_CantChangeAnotherSuperAdmin()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var other = await factory.CreateUserAsync(Roles.SuperAdmin);

        var response = await Update(superAdmin, other.Id, new { password = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CanLogIn(other.UserName!, TestAuth.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_CanDeleteAnAdmin()
    {
        var (superAdmin, _) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var target = await factory.CreateUserAsync(Roles.Admin);

        var response = await superAdmin.DeleteAsync($"api/User/{target.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Exists(target.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task SuperAdmin_CantDeleteASuperAdmin_IncludingThemselves()
    {
        var (superAdmin, me) = await factory.LoginAsAsync(Roles.SuperAdmin);
        var other = await factory.CreateUserAsync(Roles.SuperAdmin);

        var deleteOther = await superAdmin.DeleteAsync($"api/User/{other.Id}", Ct);
        var deleteSelf = await superAdmin.DeleteAsync($"api/User/{me.Id}", Ct);

        deleteOther.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deleteSelf.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Exists(other.Id)).Should().BeTrue();
        (await Exists(me.Id)).Should().BeTrue();
    }

    // ---- Finding 3: no Identity internals in responses ----

    [Fact]
    public async Task GetAllUsers_ReturnsDtos_WithoutPasswordHashOrSecurityStamp()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var someone = await factory.CreateUserAsync(Roles.User);

        var response = await admin.GetAsync("api/User", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain($"\"userId\": \"{someone.Id}\"");
        AssertNoIdentityInternals(body);
    }

    [Fact]
    public async Task DeleteUser_ResponseHasNoPasswordHashOrSecurityStamp()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var target = await factory.CreateUserAsync(Roles.User);

        var response = await admin.DeleteAsync($"api/User/{target.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertNoIdentityInternals(await response.Content.ReadAsStringAsync(Ct));
    }

    private static void AssertNoIdentityInternals(string body)
    {
        foreach (var field in new[] { "passwordHash", "securityStamp", "concurrencyStamp", "\"password\"" })
            body.Should().NotContainEquivalentOf(field);
    }

    // ---- Helpers ----

    private static Task<HttpResponseMessage> Update(HttpClient client, string userId, object body) =>
        client.PutAsJsonAsync($"api/User/updateAsAdmin/{userId}", body, Ct);

    private Task<string> FirstNameOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.FirstName).SingleAsync(Ct));

    private Task<bool> Exists(string userId) => factory.WithDbAsync(db =>
        db.Users.AnyAsync(u => u.Id == userId, Ct));

    private Task<List<string>> RolesOf(string userId) => factory.WithDbAsync(db =>
        db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(Ct));

    private async Task<bool> CanLogIn(string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostLoginAsync(username, password);
        return response.IsSuccessStatusCode;
    }
}
