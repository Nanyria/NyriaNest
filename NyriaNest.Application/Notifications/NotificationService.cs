using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Linq.Expressions;
using System.Net;

namespace NyriaNest.Application.Notifications
{
    public interface INotificationService
    {
        /// <summary>
        /// Adds one notification for each person the removal affects. Doesn't save:
        /// the caller saves it together with the removal, so both happen or neither does.
        /// </summary>
        Task NotifyBookRemovedAsync(Book book, BookRemovedReason reason);

        /// <summary>The user's notifications, newest first, one page at a time; optionally unread only.</summary>
        Task<APIResponse<PagedResult<NotificationItemDto>>> GetMyNotificationsAsync(string userId, bool unreadOnly, int page, int pageSize);

        /// <summary>How many unread notifications the user has, for the bell.</summary>
        Task<APIResponse<int>> GetUnreadCountAsync(string userId);

        /// <summary>Marks one of the user's notifications as read. Someone else's is treated as not found.</summary>
        Task<APIResponse<NotificationItemDto>> MarkAsReadAsync(string userId, int notificationId);

        /// <summary>Marks all the user's notifications as read and returns how many changed.</summary>
        Task<APIResponse<int>> MarkAllAsReadAsync(string userId);
    }

    public class NotificationService : INotificationService
    {
        private const int MaxPageSize = 50;

        // One mapping, used both inside database queries and on a loaded entity
        private static readonly Expression<Func<NotificationItem, NotificationItemDto>> ToDtoExpression = n => new NotificationItemDto
        {
            Id = n.Id,
            UserId = n.UserId,
            Message = n.Message,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
            Type = n.Type,
            BookId = n.BookId,
        };
        private static readonly Func<NotificationItem, NotificationItemDto> ToDto = ToDtoExpression.Compile();

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

        public async Task<APIResponse<PagedResult<NotificationItemDto>>> GetMyNotificationsAsync(
            string userId, bool unreadOnly, int page, int pageSize)
        {
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            page = Math.Max(1, page);

            var mine = Mine(userId);
            if (unreadOnly)
                mine = mine.Where(n => !n.IsRead);

            var total = await mine.CountAsync();
            var items = await mine
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToDtoExpression)
                .ToListAsync();

            return Ok(new PagedResult<NotificationItemDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            });
        }

        public async Task<APIResponse<int>> GetUnreadCountAsync(string userId) =>
            Ok(await Mine(userId).CountAsync(n => !n.IsRead));

        public async Task<APIResponse<NotificationItemDto>> MarkAsReadAsync(string userId, int notificationId)
        {
            // Filtered on the user too, so another user's id looks exactly like a missing one
            var notification = await _dbContext.NotificationItems
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
            if (notification == null)
            {
                return new APIResponse<NotificationItemDto>
                {
                    IsSuccess = false,
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorMessages = { "Notification not found." },
                };
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                await _dbContext.SaveChangesAsync();
            }

            return Ok(ToDto(notification));
        }

        public async Task<APIResponse<int>> MarkAllAsReadAsync(string userId)
        {
            // One UPDATE in the database instead of loading every notification first
            var changed = await Mine(userId)
                .Where(n => !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

            return Ok(changed);
        }

        // Every read and change starts here, so nothing can reach another user's notifications
        private IQueryable<NotificationItem> Mine(string userId) =>
            _dbContext.NotificationItems.Where(n => n.UserId == userId);

        private static APIResponse<T> Ok<T>(T result) => new()
        {
            IsSuccess = true,
            StatusCode = HttpStatusCode.OK,
            Result = result,
        };

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
