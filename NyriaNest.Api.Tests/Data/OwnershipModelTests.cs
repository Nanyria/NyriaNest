using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Api.Tests.Data;

/// <summary>
/// #83: physical books have an owner (Book.OwnerId; null = the shared library), and a loan
/// goes either to a member (UserId) or to someone without an account (BorrowerName), never both
/// and never neither. Checked against SQLite, which enforces check constraints and foreign keys.
/// </summary>
public class OwnershipModelTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Loans: a user or a name, exactly one ----

    [Fact]
    public async Task Loan_WithNeitherUserNorName_IsRejectedByTheDatabase()
    {
        var book = await AddAsync(TestData.Book());

        var save = () => SaveLoanAsync(book.BookId, userId: null, borrowerName: null);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Loan_WithOnlyAName_IsSaved()
    {
        var book = await AddAsync(TestData.Book());

        await SaveLoanAsync(book.BookId, userId: null, borrowerName: "Anna");

        await using var assert = _db.CreateContext();
        var loan = await assert.CheckOutItems.SingleAsync(c => c.BookId == book.BookId, Ct);
        loan.UserId.Should().BeNull();
        loan.BorrowerName.Should().Be("Anna");
    }

    [Fact]
    public async Task Loan_WithBothUserAndName_IsRejectedByTheDatabase()
    {
        var book = await AddAsync(TestData.Book());
        var member = await AddAsync(TestData.User("member"));

        var save = () => SaveLoanAsync(book.BookId, userId: member.Id, borrowerName: "Anna");

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    // ---- Owners ----

    [Fact]
    public async Task Book_CanHaveAnOwner_AndABookWithoutOneIsShared()
    {
        var owner = await AddAsync(TestData.User("owner"));
        var owned = TestData.Book("Owned book");
        owned.OwnerId = owner.Id;
        await AddAsync(owned);
        await AddAsync(TestData.Ebook("Shared e-book"));

        await using var assert = _db.CreateContext();
        (await assert.Books.Where(b => b.Title == "Owned book").Select(b => b.OwnerId).SingleAsync(Ct))
            .Should().Be(owner.Id);
        (await assert.Books.Where(b => b.Title == "Shared e-book").Select(b => b.OwnerId).SingleAsync(Ct))
            .Should().BeNull();
    }

    [Fact]
    public async Task DeletingABooksOwner_IsBlockedByTheDatabase()
    {
        // Restrict: a user delete must deal with their books on purpose. SetNull would
        // quietly move them into the shared library (OwnerId null = shared).
        var owner = await AddAsync(TestData.User("owner"));
        var owned = TestData.Book();
        owned.OwnerId = owner.Id;
        await AddAsync(owned);

        // A fresh context that doesn't know about the book, so only the database can stop it
        await using var context = _db.CreateContext();
        context.Users.Remove(await context.Users.SingleAsync(u => u.Id == owner.Id, Ct));
        var save = () => context.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    // ---- Helpers ----

    private async Task<T> AddAsync<T>(T entity) where T : class
    {
        await using var context = _db.CreateContext();
        context.Add(entity);
        await context.SaveChangesAsync(Ct);
        return entity;
    }

    private async Task SaveLoanAsync(int bookId, string? userId, string? borrowerName)
    {
        await using var context = _db.CreateContext();
        context.CheckOutItems.Add(new CheckedOutItem
        {
            BookId = bookId,
            UserId = userId,
            BorrowerName = borrowerName,
            CheckOutDate = DateTime.UtcNow,
            ReturnDate = DateTime.UtcNow.AddDays(14),
        });
        await context.SaveChangesAsync(Ct);
    }

    public void Dispose() => _db.Dispose();
}
