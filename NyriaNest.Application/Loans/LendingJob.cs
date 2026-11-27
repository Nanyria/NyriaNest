using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NyriaNest.Application.Common;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.Loans
{
    /// <summary>
    /// The daily lending job (#85), one of the IDailyJobs DailyJobsService in the API runs once a day;
    /// tests run it directly with a fake clock. It stays here, not under Notifications, because expiry
    /// is a lending rule and the reminders are decided by lending data. Each run:
    /// - reminds the owner about a request they haven't answered, RequestReminderDays after the book
    ///   became free for it (AvailableAt), and again every as many days;
    /// - expires a request the owner hasn't answered after RequestExpiryMonths and tells the friend,
    ///   who can ask again. Nothing is handed on: the whole queue was asked for when the book came back;
    /// - reminds the owner every RequestReminderDays about a return a friend reported and they haven't confirmed.
    /// What was sent is remembered on the rows, so running twice on the same day sends nothing twice.
    /// </summary>
    public class LendingJob : IDailyJob
    {
        private readonly IAppDbContext _db;
        private readonly INotificationService _notifications;
        private readonly LendingOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<LendingJob> _logger;

        public LendingJob(IAppDbContext db, INotificationService notifications, IOptions<LendingOptions> options,
            TimeProvider clock, ILogger<LendingJob> logger)
        {
            _db = db;
            _notifications = notifications;
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        // The daily runner only needs it done; the counts are logged here
        async Task IDailyJob.RunAsync(CancellationToken ct) => await RunAsync(ct);

        /// <summary>One run; returns what it did (the tests check the counts).</summary>
        public async Task<LendingJobResult> RunAsync(CancellationToken ct = default)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var every = TimeSpan.FromDays(_options.RequestReminderDays);

            // ---- Open requests: expire, or remind the owner ----
            var open = await _db.BorrowRequests
                .Include(r => r.Borrower)
                .Include(r => r.Book).ThenInclude(b => b.Owner)
                .Where(r => r.Status == BorrowRequestStatus.Requested)
                .ToListAsync(ct);

            int expired = 0, requestReminders = 0;
            foreach (var request in open)
            {
                if (now >= request.AvailableAt.AddMonths(_options.RequestExpiryMonths))
                {
                    // Only the friend is told; an expiring request isn't also a reminder
                    request.Status = BorrowRequestStatus.Expired;
                    request.AnsweredAt = now;
                    _notifications.NotifyRequestExpired(request.BorrowerId, request.Book, request.Book.Owner?.UserName ?? "");
                    expired++;
                }
                else if (IsDue(request.AvailableAt, request.LastReminderAt, now, every))
                {
                    _notifications.NotifyRequestReminder(request.Book, request.Borrower.UserName ?? "");
                    request.LastReminderAt = now;
                    requestReminders++;
                }
            }

            // ---- Reported returns the owner hasn't confirmed ----
            var reported = await _db.CheckOutItems
                .Include(l => l.User)
                .Include(l => l.Book)
                .Where(l => l.ReportedReturnedDate != null)
                .ToListAsync(ct);

            int returnReminders = 0;
            foreach (var loan in reported)
            {
                if (!IsDue(loan.ReportedReturnedDate!.Value, loan.ReturnReminderSentAt, now, every))
                    continue;
                _notifications.NotifyReturnReminder(loan.Book, loan.User?.UserName ?? "");
                loan.ReturnReminderSentAt = now;
                returnReminders++;
            }

            await _db.SaveChangesAsync(ct);
            var result = new LendingJobResult(requestReminders, expired, returnReminders);
            _logger.LogInformation("Lending job: {RequestReminders} request reminders, {Expired} expired, {ReturnReminders} return reminders.",
                result.RequestReminders, result.Expired, result.ReturnReminders);
            return result;
        }

        // The first reminder is due "every" after the start; each next one "every" after the last
        private static bool IsDue(DateTime start, DateTime? lastSent, DateTime now, TimeSpan every) =>
            now >= (lastSent ?? start) + every;
    }

    /// <summary>What one run of the daily job did, for the log.</summary>
    public record LendingJobResult(int RequestReminders, int Expired, int ReturnReminders);
}
