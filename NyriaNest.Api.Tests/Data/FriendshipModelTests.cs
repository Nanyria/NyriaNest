using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Api.Tests.Data;

/// <summary>
/// #84: the rules the Friendships table enforces by itself, checked against SQLite, which
/// enforces unique indexes, check constraints and foreign keys. The reverse pair (Anna → me
/// when me → Anna exists) is blocked by FriendshipService, not here (see FriendshipServiceTests).
/// </summary>
public class FriendshipModelTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewFriendship_StartsPending_WithAllFourSettingsOff_AndARequestedDate()
    {
        var (me, anna) = await TwoUsersAsync();

        await SaveFriendshipAsync(me.Id, anna.Id);

        await using var assert = _db.CreateContext();
        var row = await assert.Friendships.SingleAsync(Ct);
        row.Status.Should().Be(FriendshipStatus.Pending);
        row.RequesterSharesPhysicalLibrary.Should().BeFalse();
        row.RequesterSharesEbooks.Should().BeFalse();
        row.AddresseeSharesPhysicalLibrary.Should().BeFalse();
        row.AddresseeSharesEbooks.Should().BeFalse();
        row.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        row.AcceptedAt.Should().BeNull();
        row.DeclinedAt.Should().BeNull();
    }

    [Fact]
    public async Task TheSamePairTwice_IsRejectedByTheDatabase()
    {
        var (me, anna) = await TwoUsersAsync();
        await SaveFriendshipAsync(me.Id, anna.Id);

        var save = () => SaveFriendshipAsync(me.Id, anna.Id);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AFriendshipWithYourself_IsRejectedByTheDatabase()
    {
        var (me, _) = await TwoUsersAsync();

        var save = () => SaveFriendshipAsync(me.Id, me.Id);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingAUserWithAFriendship_IsBlockedByTheDatabase()
    {
        // Restrict: the user delete removes friendships on purpose (UserControllerTests),
        // and from #78 on accounts are anonymised, never deleted.
        var (me, anna) = await TwoUsersAsync();
        await SaveFriendshipAsync(me.Id, anna.Id);

        await using var context = _db.CreateContext();
        context.Users.Remove(await context.Users.SingleAsync(u => u.Id == anna.Id, Ct));
        var save = () => context.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    // ---- Helpers ----

    private async Task<(User, User)> TwoUsersAsync()
    {
        await using var context = _db.CreateContext();
        var me = TestData.User("me");
        var anna = TestData.User("anna");
        context.Users.AddRange(me, anna);
        await context.SaveChangesAsync(Ct);
        return (me, anna);
    }

    private async Task SaveFriendshipAsync(string requesterId, string addresseeId)
    {
        await using var context = _db.CreateContext();
        context.Friendships.Add(new Friendship { RequesterId = requesterId, AddresseeId = addresseeId });
        await context.SaveChangesAsync(Ct);
    }

    public void Dispose() => _db.Dispose();
}
