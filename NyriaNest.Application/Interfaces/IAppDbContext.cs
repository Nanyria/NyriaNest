using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Application.Interfaces
{
    /// <summary>
    /// What the services may use of the database: the tables, saving, and a
    /// transaction. AppDbContext implements it, so the services don't depend on
    /// the SQL Server setup, migrations or Identity's tables (see ADR 0002).
    /// It's registered as the same scoped instance the repositories get, so
    /// tracked entities and transactions are shared between them.
    /// </summary>
    public interface IAppDbContext
    {
        DbSet<Book> Books { get; }
        DbSet<ReviewItem> ReviewItems { get; }
        DbSet<RatingItem> RatingItems { get; }
        DbSet<FavoriteItem> FavoriteItems { get; }
        DbSet<StatusHistoryItem> StatusHistoryItems { get; }
        DbSet<CheckedOutItem> CheckOutItems { get; }
        DbSet<ReservationItem> ReservationItems { get; }
        DbSet<NotificationItem> NotificationItems { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>Disposing the transaction without committing rolls everything back.</summary>
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }
}
