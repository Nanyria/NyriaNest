using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Api.Tests.Data;

/// <summary>
/// #85: the rules the BorrowRequests table and the loan's return columns enforce by themselves,
/// checked against SQLite (unique indexes, foreign keys). Who may request is LendingService's job.
/// </summary>
public class BorrowRequestModelTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewRequest_StartsRequested_WithNoAnswerAndNoReminderYet()
    {
        var (book, friend) = await FriendsBookAsync();

        await SaveRequestAsync(book.BookId, friend.Id);

        await using var assert = _db.CreateContext();
        var row = await assert.BorrowRequests.SingleAsync(Ct);
        row.Status.Should().Be(BorrowRequestStatus.Requested);
        row.AnsweredAt.Should().BeNull();
        row.LastReminderAt.Should().BeNull();
    }

    [Fact]
    public async Task TwoOpenRequests_FromTheSameFriendForTheSameBook_AreRejectedByTheDatabase()
    {
        var (book, friend) = await FriendsBookAsync();
        await SaveRequestAsync(book.BookId, friend.Id);

        var save = () => SaveRequestAsync(book.BookId, friend.Id);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData(BorrowRequestStatus.Lent)]
    [InlineData(BorrowRequestStatus.Declined)]
    [InlineData(BorrowRequestStatus.Cancelled)]
    [InlineData(BorrowRequestStatus.Expired)]
    public async Task AFinishedRequest_DoesNotStopANewOne(BorrowRequestStatus finished)
    {
        // Only one open request per friend and book; the finished ones stay as the record
        var (book, friend) = await FriendsBookAsync();
        await SaveRequestAsync(book.BookId, friend.Id, finished);

        await SaveRequestAsync(book.BookId, friend.Id);

        await using var assert = _db.CreateContext();
        (await assert.BorrowRequests.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task DeletingTheBook_DeletesItsRequests()
    {
        // The requests are the book's history, and the history goes with the book.
        // Whoever had an open request is told before the delete (BookService, step 7).
        var (book, friend) = await FriendsBookAsync();
        await SaveRequestAsync(book.BookId, friend.Id);

        await using (var context = _db.CreateContext())
        {
            context.Books.Remove(await context.Books.SingleAsync(b => b.BookId == book.BookId, Ct));
            await context.SaveChangesAsync(Ct);
        }

        await using var assert = _db.CreateContext();
        (await assert.BorrowRequests.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task DeletingTheBorrower_IsBlockedByTheDatabase()
    {
        // Restrict, as for friendships: accounts are dealt with on purpose, never by a cascade
        var (book, friend) = await FriendsBookAsync();
        await SaveRequestAsync(book.BookId, friend.Id);

        await using var context = _db.CreateContext();
        context.Users.Remove(await context.Users.SingleAsync(u => u.Id == friend.Id, Ct));
        var save = () => context.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task NewLoan_HasNoReportedReturnAndNoReturnReminderYet()
    {
        var (book, friend) = await FriendsBookAsync();

        await using (var context = _db.CreateContext())
        {
            context.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = book.BookId,
                UserId = friend.Id,
                CheckOutDate = DateTime.UtcNow,
                ReturnDate = DateTime.UtcNow.AddMonths(1),
            });
            await context.SaveChangesAsync(Ct);
        }

        await using var assert = _db.CreateContext();
        var loan = await assert.CheckOutItems.SingleAsync(Ct);
        loan.ReportedReturnedDate.Should().BeNull();
        loan.ReturnReminderSentAt.Should().BeNull();
    }

    // ---- Helpers ----

    /// <summary>A physical book owned by "owner", and a second user, "friend", to request it.</summary>
    private async Task<(Book, User)> FriendsBookAsync()
    {
        await using var context = _db.CreateContext();
        var owner = TestData.User("owner");
        var friend = TestData.User("friend");
        var book = TestData.Book();
        book.Owner = owner;
        context.Users.AddRange(owner, friend);
        context.Books.Add(book);
        await context.SaveChangesAsync(Ct);
        return (book, friend);
    }

    private async Task SaveRequestAsync(int bookId, string borrowerId,
        BorrowRequestStatus status = BorrowRequestStatus.Requested)
    {
        await using var context = _db.CreateContext();
        context.BorrowRequests.Add(new BorrowRequest
        {
            BookId = bookId,
            BorrowerId = borrowerId,
            Status = status,
            RequestedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(Ct);
    }

    public void Dispose() => _db.Dispose();
}
