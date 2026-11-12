using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.Notifications
{
    public interface INotificationService
    {
        /// <summary>
        /// Adds one notification for each person the removal affects. Doesn't save:
        /// the caller saves it together with the removal, so both happen or neither does.
        /// </summary>
        Task NotifyBookRemovedAsync(Book book, BookRemovedReason reason);
    }

    public class NotificationService : INotificationService
    {
        private readonly IAppDbContext _dbContext;

        public NotificationService(IAppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task NotifyBookRemovedAsync(Book book, BookRemovedReason reason)
        {
            var message = MessageFor(book.Title, reason);

            // Distinct: one person can hold more than one reservation on the same book
            foreach (var userId in RecipientsOf(book, reason).Distinct())
            {
                _dbContext.NotificationItems.Add(new NotificationItem
                {
                    UserId = userId,
                    Message = message,
                });
            }

            // Async because later reasons (#83) have to query for who to tell
            return Task.CompletedTask;
        }

        private static IEnumerable<string> RecipientsOf(Book book, BookRemovedReason reason) => reason switch
        {
            BookRemovedReason.DeletedByAdmin => book.Reservations.Select(r => r.UserId),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

        private static string MessageFor(string title, BookRemovedReason reason) => reason switch
        {
            BookRemovedReason.DeletedByAdmin => $"\"{title}\" was removed, so your reservation was cancelled.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }
}
