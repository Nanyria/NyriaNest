using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Infrastructure.Data;

namespace NyriaNest.Api.Tests.Data;

/// <summary>
/// #59: login data lives in the "identity" schema and library data in the "library" schema.
/// The model is built with the SQL Server provider, as in production. Nothing connects to a database.
/// </summary>
public class SchemaTests
{
    private static readonly string[] IdentityTables =
    [
        "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
        "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims",
    ];

    private static readonly string[] LibraryTables =
    [
        "Books", "CheckOutItems", "ReservationItems", "StatusHistoryItems",
        "ReviewItems", "RatingItems", "FavoriteItems", "NotificationItems",
    ];

    public static TheoryData<string> IdentityTableNames => new(IdentityTables);
    public static TheoryData<string> LibraryTableNames => new(LibraryTables);

    [Theory]
    [MemberData(nameof(IdentityTableNames))]
    public void IdentityTable_IsInTheIdentitySchema(string table)
    {
        SchemaOf(table).Should().Be("identity");
    }

    [Theory]
    [MemberData(nameof(LibraryTableNames))]
    public void LibraryTable_IsInTheLibrarySchema(string table)
    {
        SchemaOf(table).Should().Be("library");
    }

    [Fact]
    public void EveryTable_IsListedHere()
    {
        // A new table (for example from #78, or a new Identity version) fails here until
        // it's added to one of the lists above, so nobody forgets to pick its schema.
        MappedTables().Select(t => t.Name)
            .Should().BeEquivalentTo(IdentityTables.Concat(LibraryTables));
    }

    [Fact]
    public void NoTable_IsInTheDefaultSchema()
    {
        MappedTables().Where(t => t.Schema is null or "dbo").Select(t => t.Name)
            .Should().BeEmpty("every table belongs in either identity or library");
    }

    private static string? SchemaOf(string table)
    {
        var match = MappedTables().Where(t => t.Name == table).ToList();
        match.Should().ContainSingle($"the model should map exactly one table called {table}");
        return match[0].Schema;
    }

    private static List<(string Name, string? Schema)> MappedTables()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused") // never opened: only the model is read
            .Options);

        return db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .Select(e => (e.GetTableName()!, e.GetSchema()))
            .Distinct()
            .ToList();
    }
}
