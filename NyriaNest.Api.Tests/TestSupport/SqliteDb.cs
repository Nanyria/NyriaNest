using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Data;

namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>
/// A fresh in-memory SQLite database per test class instance (xUnit creates one per test).
/// Unlike the EF InMemory provider, SQLite enforces foreign keys, unique indexes
/// and transactions, so rollback and "one loan per book" tests mean something.
/// </summary>
public sealed class SqliteDb : IDisposable
{
    // The database lives only as long as this connection is open.
    private readonly SqliteConnection _connection;

    public SqliteDb()
    {
        _connection = OpenConnection();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    /// <summary>
    /// An open in-memory SQLite connection that AppDbContext can use. The database
    /// is deleted when the connection is closed. Also used by <see cref="ApiFactory"/>.
    /// </summary>
    public static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        // AppDbContext uses SQL Server's GETUTCDATE() as a column default.
        // SQLite has no such function, so register one with the same meaning.
        connection.CreateFunction("GETUTCDATE", () => DateTime.UtcNow);
        return connection;
    }

    /// <summary>
    /// A new context on the same database. Use one context to arrange and
    /// another to assert, so the assert reads from the database and not
    /// from entities the first context is still tracking.
    /// </summary>
    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);

    public void Dispose() => _connection.Dispose();
}
