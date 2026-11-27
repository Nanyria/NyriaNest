namespace NyriaNest.Application.Common
{
    /// <summary>
    /// Work that runs once a day (#85), such as reminders and expiry. Each feature keeps its own job
    /// next to its rules (LendingJob in Loans) and registers it as an IDailyJob; DailyJobsService in
    /// the API runs them all, each in its own scope, so one failing job doesn't stop the others.
    /// A job must be safe to run twice on the same day: the API runs them at every startup too.
    /// </summary>
    public interface IDailyJob
    {
        Task RunAsync(CancellationToken ct);
    }
}
