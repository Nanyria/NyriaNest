using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using NyriaNest.Infrastructure.Data;

namespace NyriaNest.Api.Tests.Data;

/// <summary>
/// #83: DatabaseSeeder.SeedDemoDataAsync fills a development database with a few users
/// who own physical books (one lent to a name) and some shared-library e-books.
/// Each test gets its own API and database, so the counts are exact:
/// before seeding there is only the SuperAdmin and the HasData e-book (1004).
/// </summary>
public class DemoDataSeederTests
{
    private const string DemoPassword = "Test-Demo-123!";
    private const int HasDataBookId = 1004;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedsThreeUsers_EachOwningPhysicalBooks_WithAnAddedHistoryRow()
    {
        using var factory = new ApiFactory();
        var api = WithPassword(factory, DemoPassword);

        await RunSeederAsync(api);

        var demoUsers = await WithDbAsync(api, db => db.Users
            .Where(u => u.Email != ApiFactory.SeedAdminEmail)
            .Select(u => new { u.Id, u.Role, OwnBooks = u.OwnBooks.Count })
            .ToListAsync(Ct));
        demoUsers.Should().HaveCount(3);
        demoUsers.Should().OnlyContain(u => u.Role == Roles.User);
        demoUsers.Should().OnlyContain(u => u.OwnBooks >= 2, "every demo user has a shelf of their own");

        var identityRoles = await WithDbAsync(api, db => db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(Ct));
        foreach (var user in demoUsers)
            identityRoles.Where(r => r.UserId == user.Id).Select(r => r.Name).Should().Equal(Roles.User);

        var ownedBooks = await WithDbAsync(api, db => db.Books
            .Where(b => b.OwnerId != null)
            .Select(b => new
            {
                b.BookType,
                b.OwnerId,
                AddedByOwner = b.StatusHistory.Any(h => h.BookStatus == BookStatusEnum.Added && h.UserId == b.OwnerId)
            })
            .ToListAsync(Ct));
        ownedBooks.Should().OnlyContain(b => b.BookType == BookTypeEnums.Paperback || b.BookType == BookTypeEnums.Hardcover,
            "own books are physical; e-books live in the shared library (ADR 0004)");
        ownedBooks.Should().Contain(b => b.BookType == BookTypeEnums.Paperback);
        ownedBooks.Should().Contain(b => b.BookType == BookTypeEnums.Hardcover);
        ownedBooks.Should().OnlyContain(b => b.AddedByOwner, "each own book starts with an Added row by its owner");
    }

    [Fact]
    public async Task LendsExactlyOneOwnBook_ToANameOnly()
    {
        using var factory = new ApiFactory();
        var api = WithPassword(factory, DemoPassword);

        await RunSeederAsync(api);

        var loans = await WithDbAsync(api, db => db.CheckOutItems
            .Select(l => new { l.UserId, l.BorrowerName, l.CheckOutDate, l.ReturnDate, l.Book.OwnerId, l.Book.BookStatus })
            .ToListAsync(Ct));
        var loan = loans.Should().ContainSingle().Subject;
        loan.UserId.Should().BeNull("in #83 you lend to a name only");
        loan.BorrowerName.Should().NotBeNullOrWhiteSpace();
        loan.OwnerId.Should().NotBeNull("only own books are lent");
        loan.BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        loan.ReturnDate.Should().BeAfter(loan.CheckOutDate);
    }

    [Fact]
    public async Task SeedsSharedLibraryEbooks_WithoutAnOwner()
    {
        using var factory = new ApiFactory();
        var api = WithPassword(factory, DemoPassword);

        await RunSeederAsync(api);

        var shared = await WithDbAsync(api, db => db.Books
            .Where(b => b.OwnerId == null)
            .Select(b => new { b.BookId, b.BookType })
            .ToListAsync(Ct));
        shared.Should().Contain(b => b.BookId != HasDataBookId, "the seeder adds e-books besides the HasData one");
        shared.Should().OnlyContain(b => b.BookType == BookTypeEnums.EBook, "the shared library holds e-books only (ADR 0004)");
    }

    [Fact]
    public async Task SeedsAFriendshipSharedOneWay_AndAPendingRequest()
    {
        // #84: alma and boris are friends and alma shares her physical library with boris, not the
        // other way round (sharing is per direction). cleo has asked alma and is waiting.
        using var factory = new ApiFactory();
        var api = WithPassword(factory, DemoPassword);

        await RunSeederAsync(api);

        var rows = await WithDbAsync(api, db => db.Friendships
            .Select(f => new
            {
                Requester = f.Requester.UserName,
                Addressee = f.Addressee.UserName,
                f.Status,
                f.AcceptedAt,
                f.RequesterSharesPhysicalLibrary,
                f.RequesterSharesEbooks,
                f.AddresseeSharesPhysicalLibrary,
                f.AddresseeSharesEbooks,
            })
            .ToListAsync(Ct));
        rows.Should().HaveCount(2);

        var friends = rows.Should().ContainSingle(r => r.Status == FriendshipStatus.Accepted).Subject;
        (friends.Requester, friends.Addressee).Should().Be(("alma.reads", "boris.books"));
        friends.AcceptedAt.Should().NotBeNull();
        friends.RequesterSharesPhysicalLibrary.Should().BeTrue();
        friends.RequesterSharesEbooks.Should().BeFalse();
        friends.AddresseeSharesPhysicalLibrary.Should().BeFalse();
        friends.AddresseeSharesEbooks.Should().BeFalse();

        var pending = rows.Should().ContainSingle(r => r.Status == FriendshipStatus.Pending).Subject;
        (pending.Requester, pending.Addressee).Should().Be(("cleo.shelf", "alma.reads"));
    }

    [Fact]
    public async Task RunningTwice_AddsNothingTheSecondTime()
    {
        using var factory = new ApiFactory();
        var api = WithPassword(factory, DemoPassword);
        await RunSeederAsync(api);
        var before = await CountsAsync(api);
        var friendshipsBefore = await WithDbAsync(api, db => db.Friendships.CountAsync(Ct));

        await RunSeederAsync(api);

        (await CountsAsync(api)).Should().Be(before);
        (await WithDbAsync(api, db => db.Friendships.CountAsync(Ct))).Should().Be(friendshipsBefore);
    }

    [Fact]
    public async Task WithoutAPassword_SeedsNothing()
    {
        using var factory = new ApiFactory();
        var api = WithPassword(factory, null);

        await RunSeederAsync(api);

        (await CountsAsync(api)).Should().Be((Users: 1, Books: 1, Loans: 0), "only the SuperAdmin and the HasData e-book");
    }

    [Fact]
    public async Task AtStartup_NothingIsSeeded_UnlessEnabled()
    {
        // A password is set, but SeedDemoData:Enabled isn't (as in Testing and production)
        using var factory = new ApiFactory();
        var api = WithPassword(factory, DemoPassword);

        _ = api.Services; // starts the API, which runs InitialiseDatabaseAsync

        (await CountsAsync(api)).Should().Be((Users: 1, Books: 1, Loans: 0));
    }

    // ---- Helpers ----

    /// <summary>
    /// The API with the demo password set, or as it is. Disposing the ApiFactory disposes
    /// this too (and the database), so the tests dispose the factory.
    /// </summary>
    private static WebApplicationFactory<Program> WithPassword(ApiFactory factory, string? password) =>
        password is null
            ? factory
            : factory.WithWebHostBuilder(b => b.UseSetting("SeedDemoData:Password", password));

    private static async Task RunSeederAsync(WebApplicationFactory<Program> api)
    {
        using var scope = api.Services.CreateScope();
        await DatabaseSeeder.SeedDemoDataAsync(scope.ServiceProvider);
    }

    private static async Task<T> WithDbAsync<T>(WebApplicationFactory<Program> api, Func<AppDbContext, Task<T>> action)
    {
        using var scope = api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<(int Users, int Books, int Loans)> CountsAsync(WebApplicationFactory<Program> api) =>
        WithDbAsync(api, async db => (
            await db.Users.CountAsync(Ct),
            await db.Books.CountAsync(Ct),
            await db.CheckOutItems.CountAsync(Ct)));
}
