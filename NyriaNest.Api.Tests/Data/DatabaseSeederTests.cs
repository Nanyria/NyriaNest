using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Infrastructure.Data;

namespace NyriaNest.Api.Tests.Data;

/// <summary>
/// #60: DatabaseSeeder.AssignUserRoleToUsersWithoutRoleAsync runs at every startup.
/// It should only write to users whose role actually needs fixing.
/// </summary>
public class DatabaseSeederTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunningTwice_LeavesAnAlreadyCorrectUserUntouched()
    {
        var user = await factory.CreateUserAsync(Roles.User);
        await RunSeederAsync(); // whatever the first run fixes, the second has nothing to do
        var stampBefore = await StampOf(user.Id);

        await RunSeederAsync();

        (await StampOf(user.Id)).Should().Be(stampBefore, "nothing about the user changed, so it shouldn't be saved");
    }

    [Fact]
    public async Task AUserWithoutARole_GetsTheUserRole()
    {
        var userId = await CreateUserWithoutIdentityRoleAsync();

        await RunSeederAsync();

        (await IdentityRolesOf(userId)).Should().Equal(Roles.User);
        (await RoleColumnOf(userId)).Should().Be(Roles.User);
    }

    [Fact]
    public async Task AnOutdatedRoleColumn_IsCorrectedFromTheIdentityRole()
    {
        var user = await factory.CreateUserAsync(Roles.User);
        await factory.WithDbAsync(async db =>
        {
            var stored = await db.Users.SingleAsync(u => u.Id == user.Id, Ct);
            stored.Role = Roles.Admin; // says Admin, but Identity says User
            await db.SaveChangesAsync(Ct);
        });

        await RunSeederAsync();

        (await RoleColumnOf(user.Id)).Should().Be(Roles.User);
        (await IdentityRolesOf(user.Id)).Should().Equal(Roles.User);
    }

    // ---- Helpers ----

    private async Task RunSeederAsync()
    {
        using var scope = factory.Services.CreateScope();
        await DatabaseSeeder.AssignUserRoleToUsersWithoutRoleAsync(scope.ServiceProvider);
    }

    private async Task<string> CreateUserWithoutIdentityRoleAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = TestData.User($"norole-{Guid.NewGuid():N}"[..15], role: "");
        var result = await userManager.CreateAsync(user, TestAuth.Password);
        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private Task<string?> StampOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.ConcurrencyStamp).SingleAsync(Ct));

    private Task<string> RoleColumnOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.Role).SingleAsync(Ct));

    private Task<List<string>> IdentityRolesOf(string userId) => factory.WithDbAsync(db =>
        db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(Ct));
}
