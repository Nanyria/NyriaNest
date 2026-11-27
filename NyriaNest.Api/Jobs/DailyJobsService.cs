using Microsoft.Extensions.Options;
using NyriaNest.Application.Common;

namespace NyriaNest.Api.Jobs
{
    /// <summary>
    /// Runs every registered IDailyJob (#85): once at startup and then every 24 hours. Each job gets
    /// its own scope (they use the database) and its own try/catch, so one failing job is logged and
    /// tried again tomorrow without stopping the others or the API. The jobs remember what they have
    /// done, so a restart doesn't repeat anything. Turned off with DailyJobs:Enabled (the tests do).
    /// </summary>
    public class DailyJobsService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly DailyJobsOptions _options;
        private readonly ILogger<DailyJobsService> _logger;

        public DailyJobsService(IServiceScopeFactory scopes, IOptions<DailyJobsOptions> options,
            ILogger<DailyJobsService> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("The daily jobs are turned off (DailyJobs:Enabled).");
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
            do
            {
                await RunAllAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task RunAllAsync(CancellationToken stoppingToken)
        {
            // Ask for the list in a scope, then give each job a fresh scope of its own
            List<Type> jobTypes;
            using (var scope = _scopes.CreateScope())
                jobTypes = scope.ServiceProvider.GetServices<IDailyJob>().Select(j => j.GetType()).ToList();

            foreach (var jobType in jobTypes)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var job = scope.ServiceProvider.GetServices<IDailyJob>().First(j => j.GetType() == jobType);
                    await job.RunAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "The daily job {Job} failed.", jobType.Name);
                }
            }
        }
    }

    /// <summary>The "DailyJobs" section. Off in tests, so the jobs don't add notifications mid-test.</summary>
    public class DailyJobsOptions
    {
        public const string SectionName = "DailyJobs";

        public bool Enabled { get; set; } = true;
    }
}
