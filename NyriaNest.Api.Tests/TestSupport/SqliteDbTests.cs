using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>Checks the test database itself, so a broken setup fails here and not in every other test.</summary>
public class SqliteDbTests : IDisposable
{
    private readonly SqliteDb _db = new();

    [Fact]
    public async Task CreateContext_HasTheSeededBooks()
    {
        await using var context = _db.CreateContext();

        var count = await context.Books.CountAsync(TestContext.Current.CancellationToken);

        count.Should().Be(4);
    }

    [Fact]
    public async Task SaveBook_UsesTheDateAddedDefault_AndCanBeReadBack()
    {
        var book = TestData.Book("Saved book");
        await using (var arrange = _db.CreateContext())
        {
            arrange.Books.Add(book);
            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var assert = _db.CreateContext();
        var saved = await assert.Books.SingleAsync(b => b.Title == "Saved book", TestContext.Current.CancellationToken);

        saved.BookId.Should().BeGreaterThan(0);
        saved.DateAdded.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SecondLoanOfSameBook_IsRejectedByTheDatabase()
    {
        var book = TestData.Book();
        var userA = TestData.User("a");
        var userB = TestData.User("b");
        await using (var arrange = _db.CreateContext())
        {
            arrange.AddRange(book, userA, userB);
            arrange.CheckOutItems.Add(TestData.Loan(book, userA));
            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A second context, like a second request: it doesn't know about the first loan,
        // so only the database can stop it. Set IDs only, no navigation properties.
        await using var context = _db.CreateContext();
        context.CheckOutItems.Add(new()
        {
            BookId = book.BookId,
            UserId = userB.Id,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(14),
        });

        var save = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Proves SQLite enforces the unique CheckOutItems.BookId index (the EF InMemory provider would not)
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose() => _db.Dispose();
}
