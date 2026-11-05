using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using NyriaNest.Api.Data;
using NyriaNest.Api.Services;
using NyriaNest.Api.Storage;

namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>
/// Runs the real API in memory for HTTP tests. Everything is the real pipeline
/// (routing, [Authorize], JWT validation, controllers, services) except:
/// - the database is in-memory SQLite, never SQL Server
/// - email and file storage are mocks, so nothing is sent or written to disk
/// - all settings are test values; the "Testing" environment means
///   your User Secrets (real JWT key, admin password) are never loaded.
/// Use with IClassFixture&lt;ApiFactory&gt;: one API and database per test class.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Test-only values. They are valid only against this in-memory server.
    public const string JwtKey = "test-only-jwt-signing-key-not-used-anywhere-else-0123456789";
    public const string JwtIssuer = "NyriaNest.Api";
    public const string JwtAudience = "NyriaNest.Client";
    public const string SeedAdminEmail = "superadmin@test.local";
    public const string SeedAdminPassword = "Test-SuperAdmin-123!";

    private readonly SqliteConnection _connection = SqliteDb.OpenConnection();

    public Mock<IEmailService> Email { get; } = new();
    public Mock<IFileStorage> FileStorage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so User Secrets aren't loaded.
        builder.UseEnvironment("Testing");

        // UseSetting (not ConfigureAppConfiguration) because Program.cs reads
        // Jwt:Key before the app is built, and only these are visible that early.
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.UseSetting("SeedAdmin:Email", SeedAdminEmail);
        builder.UseSetting("SeedAdmin:Password", SeedAdminPassword);
        builder.UseSetting("ConnectionStrings:ConnectionToDB", "not-used-in-tests");

        builder.ConfigureServices(services =>
        {
            // Swap SQL Server for SQLite. Both registrations must go, or EF
            // complains that two database providers are configured.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IEmailService>();
            services.AddSingleton(Email.Object);

            services.RemoveAll<IFileStorage>();
            services.AddSingleton(FileStorage.Object);
        });
    }

    /// <summary>
    /// Runs <paramref name="action"/> with a fresh AppDbContext on the API's database,
    /// to arrange data before a request or check what a request saved.
    /// </summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    public Task WithDbAsync(Func<AppDbContext, Task> action) =>
        WithDbAsync(async db => { await action(db); return true; });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
