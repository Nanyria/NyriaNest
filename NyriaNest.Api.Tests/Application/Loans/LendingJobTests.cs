using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using NyriaNest.Infrastructure.Data;

namespace NyriaNest.Api.Tests.Application.Loans;

/// <summary>
/// #85 step 6: the daily lending job, driven by a fake clock. Each run:
/// - reminds the owner about a request they haven't answered, RequestReminderDays (14) after the
///   book became free for it (AvailableAt), and again every 14 days;
/// - expires a request the owner hasn't answered after RequestExpiryMonths (1), and tells the friend,
///   who can ask again; nothing is handed on, since the whole queue was asked for at once;
/// - reminds the owner every 14 days about a return a friend reported that they haven't confirmed.
/// Running twice on the same day sends nothing twice.
/// </summary>
public class LendingJobTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly User _owner;
    private readonly User _friend;
    private readonly int _bookId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LendingJobTests()
    {
        using var db = _db.CreateContext();
        _owner = TestData.User("owner");
        _friend = TestData.User("friend");
        db.Users.AddRange(_owner, _friend);
        var book = TestData.Book("The owner's paperback");
        book.OwnerId = _owner.Id;
        db.Books.Add(book);
        db.SaveChanges();
        _bookId = book.BookId;
    }

    public void Dispose() => _db.Dispose();

    // ---- Reminding the owner about a request ----

    [Fact]
    public async Task A_request_isnt_mentioned_before_14_days()
    {
        await OpenRequestAsync(availableAt: FakeClock.Start);
        _clock.Advance(TimeSpan.FromDays(13));

        await RunAsync();

        (await NotificationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task After_14_days_the_owner_is_reminded_once()
    {
        var requestId = await OpenRequestAsync(availableAt: FakeClock.Start);
        _clock.Advance(TimeSpan.FromDays(14));

        var result = await RunAsync();
        await RunAsync(); // the same day again: nothing new

        result.RequestReminders.Should().Be(1);
        var told = await NotificationsAsync();
        told.Should().ContainSingle();
        told[0].UserId.Should().Be(_owner.Id);
        told[0].Type.Should().Be(NotificationType.LoanRequested);
        told[0].BookId.Should().Be(_bookId);
        told[0].Message.Should().Contain(_friend.UserName).And.Contain("The owner's paperback");
        (await RequestAsync(requestId)).LastReminderAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task The_owner_is_reminded_again_every_14_days()
    {
        await OpenRequestAsync(availableAt: FakeClock.Start);
        _clock.Advance(TimeSpan.FromDays(14));
        await RunAsync();
        _clock.Advance(TimeSpan.FromDays(13));
        await RunAsync();
        (await NotificationsAsync()).Should().HaveCount(1, "13 days after the last reminder is too soon");

        _clock.Advance(TimeSpan.FromDays(1));
        await RunAsync();

        (await NotificationsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_reminder_counts_from_when_the_book_became_free_not_from_when_they_started_waiting()
    {
        // From the queue: waiting for two months, but the book only came back today
        await OpenRequestAsync(requestedAt: FakeClock.Start.AddMonths(-2), availableAt: FakeClock.Start);
        _clock.Advance(TimeSpan.FromDays(10));

        await RunAsync();

        (await NotificationsAsync()).Should().BeEmpty();
    }

    // ---- Expiring a request ----

    [Fact]
    public async Task A_request_is_still_open_the_day_before_a_month_has_passed()
    {
        var requestId = await OpenRequestAsync(availableAt: FakeClock.Start);
        _clock.UtcNow = FakeClock.Start.AddMonths(1).AddDays(-1);

        await RunAsync();

        (await RequestAsync(requestId)).Status.Should().Be(BorrowRequestStatus.Requested);
    }

    [Fact]
    public async Task After_a_month_the_request_expires_and_the_friend_is_told_they_can_ask_again()
    {
        var requestId = await OpenRequestAsync(availableAt: FakeClock.Start);
        _clock.UtcNow = FakeClock.Start.AddMonths(1);

        var result = await RunAsync();

        result.Expired.Should().Be(1);
        var request = await RequestAsync(requestId);
        request.Status.Should().Be(BorrowRequestStatus.Expired);
        request.AnsweredAt.Should().Be(_clock.UtcNow);

        // Only the friend is told; an expiring request isn't also a reminder to the owner
        var told = await NotificationsAsync();
        told.Should().ContainSingle();
        told[0].UserId.Should().Be(_friend.Id);
        told[0].Type.Should().Be(NotificationType.LoanRequestAnswered);
        told[0].BookId.Should().Be(_bookId);
        told[0].Message.Should().Contain("expired").And.Contain("ask again");

        // The book is untouched, and the friend may ask again
        using var db = _db.CreateContext();
        (await db.Books.SingleAsync(b => b.BookId == _bookId, Ct)).BookStatus.Should().Be(BookStatusEnum.Available);
        (await db.CheckOutItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task An_expired_request_isnt_expired_or_mentioned_again()
    {
        await OpenRequestAsync(availableAt: FakeClock.Start);
        _clock.UtcNow = FakeClock.Start.AddMonths(1);
        await RunAsync();

        _clock.Advance(TimeSpan.FromDays(14));
        var second = await RunAsync();

        second.Expired.Should().Be(0);
        second.RequestReminders.Should().Be(0);
        (await NotificationsAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData(BorrowRequestStatus.Lent)]
    [InlineData(BorrowRequestStatus.Declined)]
    [InlineData(BorrowRequestStatus.Cancelled)]
    [InlineData(BorrowRequestStatus.Queued)]
    public async Task Finished_requests_are_left_alone(BorrowRequestStatus finished)
    {
        await OpenRequestAsync(availableAt: FakeClock.Start, status: finished);
        _clock.UtcNow = FakeClock.Start.AddMonths(2);

        var result = await RunAsync();

        result.Expired.Should().Be(0);
        result.RequestReminders.Should().Be(0);
        (await NotificationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_days_and_months_come_from_the_settings()
    {
        var requestId = await OpenRequestAsync(availableAt: FakeClock.Start);
        var options = new LendingOptions { RequestReminderDays = 3, RequestExpiryMonths = 2 };

        _clock.Advance(TimeSpan.FromDays(3));
        (await RunAsync(options)).RequestReminders.Should().Be(1);

        _clock.UtcNow = FakeClock.Start.AddMonths(1);
        (await RunAsync(options)).Expired.Should().Be(0, "two months in these settings");

        _clock.UtcNow = FakeClock.Start.AddMonths(2);
        (await RunAsync(options)).Expired.Should().Be(1);
        (await RequestAsync(requestId)).Status.Should().Be(BorrowRequestStatus.Expired);
    }

    // ---- Reminding the owner about a reported return ----

    [Fact]
    public async Task A_reported_return_isnt_mentioned_before_14_days()
    {
        await LoanAsync(reportedReturnedOn: FakeClock.Start);
        _clock.Advance(TimeSpan.FromDays(13));

        await RunAsync();

        (await NotificationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task After_14_days_the_owner_is_reminded_to_confirm_the_return_and_again_every_14_days()
    {
        var loanId = await LoanAsync(reportedReturnedOn: FakeClock.Start);
        _clock.Advance(TimeSpan.FromDays(14));

        var result = await RunAsync();
        await RunAsync(); // the same day again: nothing new

        result.ReturnReminders.Should().Be(1);
        var told = await NotificationsAsync();
        told.Should().ContainSingle();
        told[0].UserId.Should().Be(_owner.Id);
        told[0].Type.Should().Be(NotificationType.ReturnMarked);
        told[0].BookId.Should().Be(_bookId);
        told[0].Message.Should().Contain(_friend.UserName).And.Contain("confirm");
        using (var db = _db.CreateContext())
            (await db.CheckOutItems.SingleAsync(l => l.Id == loanId, Ct)).ReturnReminderSentAt.Should().Be(_clock.UtcNow);

        _clock.Advance(TimeSpan.FromDays(14));
        await RunAsync();
        (await NotificationsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_loan_that_isnt_reported_returned_isnt_mentioned()
    {
        // Overdue loans are a later issue (#78); this job only nudges about unconfirmed returns
        await LoanAsync(reportedReturnedOn: null);
        _clock.Advance(TimeSpan.FromDays(60));

        var result = await RunAsync();

        result.ReturnReminders.Should().Be(0);
        (await NotificationsAsync()).Should().BeEmpty();
    }

    // ---- Helpers ----

    private async Task<LendingJobResult> RunAsync(LendingOptions? options = null)
    {
        using var db = _db.CreateContext();
        var job = new LendingJob(db, new NotificationService(db), Options.Create(options ?? new LendingOptions()),
            _clock, NullLogger<LendingJob>.Instance);
        return await job.RunAsync(Ct);
    }

    private async Task<int> OpenRequestAsync(DateTime availableAt, DateTime? requestedAt = null,
        BorrowRequestStatus status = BorrowRequestStatus.Requested)
    {
        using var db = _db.CreateContext();
        var request = new BorrowRequest
        {
            BookId = _bookId,
            BorrowerId = _friend.Id,
            Status = status,
            RequestedAt = requestedAt ?? availableAt,
            AvailableAt = availableAt,
            AnsweredAt = status == BorrowRequestStatus.Requested ? null : availableAt,
        };
        db.BorrowRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return request.Id;
    }

    /// <summary>The book lent to the friend a while ago, reported returned on the given day (or not at all).</summary>
    private async Task<int> LoanAsync(DateTime? reportedReturnedOn)
    {
        using var db = _db.CreateContext();
        var loan = new CheckedOutItem
        {
            BookId = _bookId,
            UserId = _friend.Id,
            CheckOutDate = FakeClock.Start.AddDays(-20),
            ReturnDate = FakeClock.Start.AddDays(10),
            ReportedReturnedDate = reportedReturnedOn,
        };
        db.CheckOutItems.Add(loan);
        var book = await db.Books.SingleAsync(b => b.BookId == _bookId, Ct);
        book.BookStatus = BookStatusEnum.CheckedOut;
        await db.SaveChangesAsync(Ct);
        return loan.Id;
    }

    private async Task<BorrowRequest> RequestAsync(int id)
    {
        using var db = _db.CreateContext();
        return await db.BorrowRequests.AsNoTracking().SingleAsync(r => r.Id == id, Ct);
    }

    private async Task<List<NotificationItem>> NotificationsAsync()
    {
        using var db = _db.CreateContext();
        return await db.NotificationItems.AsNoTracking().OrderBy(n => n.Id).ToListAsync(Ct);
    }
}
