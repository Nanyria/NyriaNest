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
        Task NotifyBookRemovedAsync(Book book, BookRemovedReason reason, string? details = null);

        /// <summary>
        /// Tells the owner that an admin marked their book as lent to someone, and why (#83).
        /// Doesn't save: the caller saves it together with the loan.
        /// </summary>
        void NotifyOwnerOfAdminLend(Book book, string borrower, string reason);

        /// <summary>Tells the owner that an admin marked their book as returned, and why (#83). Doesn't save.</summary>
        void NotifyOwnerOfAdminReturn(Book book, string borrower, string reason);

        /// <summary>
        /// An admin marked the book Lost or Removed, which counts as removing it for readers (#83):
        /// everyone with a reservation or a favorite gets the same message as for a delete. With an
        /// <paramref name="ownerReason"/> (the book is someone else's) the owner is told too, and why.
        /// Doesn't save: call it before the reservations and favorites are removed, and save together.
        /// </summary>
        Task NotifyBookTakenDownAsync(Book book, BookStatusEnum status, string? ownerReason);

        /// <summary>Tells the owner that an admin restored their book, and why (#83). Doesn't save.</summary>
        void NotifyOwnerOfAdminRestore(Book book, string reason);

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

        public async Task NotifyBookRemovedAsync(Book book, BookRemovedReason reason, string? details = null)
        {
            // The owner gets their own message, with the admin's reason, and is never one of the others
            if (reason == BookRemovedReason.DeletedByAdminFromOwner && book.OwnerId is not null)
                AddRemovedNotification(book.OwnerId, $"An admin removed your book \"{book.Title}\". Reason: {details}");

            await NotifyOthersAsync(book, WhatHappenedTo(book.Title, reason));
        }

        public async Task NotifyBookTakenDownAsync(Book book, BookStatusEnum status, string? ownerReason)
        {
            if (ownerReason is not null)
                NotifyOwner(book, $"An admin marked your book \"{book.Title}\" as {status.ToString().ToLowerInvariant()}. Reason: {ownerReason}",
                    NotificationType.BookStatusChangedByAdmin);

            // Readers are told exactly what a delete would tell them
            var sameAsDelete = book.OwnerId is null ? BookRemovedReason.RemovedFromSharedLibrary : BookRemovedReason.DeletedByAdminFromOwner;
            await NotifyOthersAsync(book, WhatHappenedTo(book.Title, sameAsDelete));
        }

        public void NotifyOwnerOfAdminRestore(Book book, string reason) =>
            NotifyOwner(book, $"An admin restored your book \"{book.Title}\". Reason: {reason}", NotificationType.BookStatusChangedByAdmin);

        /// <summary>One message to each person with a current interaction with the book, the owner excluded.</summary>
        private async Task NotifyOthersAsync(Book book, string whatHappened)
        {
            // Everyone with a current interaction (#83): a reservation or a favorite. Past reviews
            // and ratings don't count. Favorites go with the book, so they're read before the delete.
            var reservers = book.Reservations.Select(r => r.UserId).ToHashSet();
            var favoriters = await _dbContext.FavoriteItems
                .Where(f => f.BookId == book.BookId)
                .Select(f => f.UserId)
                .ToListAsync();

            // A set: one person can hold two reservations, or a reservation and a favorite. One message each.
            var others = reservers.Union(favoriters).Where(userId => userId != book.OwnerId);
            foreach (var userId in others)
            {
                // The reservation is the bigger loss, so it's the one that's named
                var consequence = reservers.Contains(userId)
                    ? "so your reservation was cancelled."
                    : "so it's no longer in your favorites.";
                AddRemovedNotification(userId, $"{whatHappened}, {consequence}");
            }
        }

        private void AddRemovedNotification(string userId, string message) =>
            _dbContext.NotificationItems.Add(new NotificationItem
            {
                UserId = userId,
                Message = message,
            });

        public void NotifyOwnerOfAdminLend(Book book, string borrower, string reason) =>
            NotifyOwner(book, $"An admin marked your book \"{book.Title}\" as lent to {borrower}. Reason: {reason}",
                NotificationType.BookLoanChangedByAdmin);

        public void NotifyOwnerOfAdminReturn(Book book, string borrower, string reason) =>
            NotifyOwner(book, $"An admin marked your book \"{book.Title}\" as returned by {borrower}. Reason: {reason}",
                NotificationType.BookLoanChangedByAdmin);

        // The book still exists here, so the notification points at it
        private void NotifyOwner(Book book, string message, NotificationType type)
        {
            if (book.OwnerId is null)
                return;
            _dbContext.NotificationItems.Add(new NotificationItem
            {
                UserId = book.OwnerId,
                Message = message,
                Type = type,
                BookId = book.BookId,
            });
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

        // What the others are told happened. The admin's reason is only for the owner.
        private static string WhatHappenedTo(string title, BookRemovedReason reason) => reason switch
        {
            BookRemovedReason.RemovedFromSharedLibrary => $"\"{title}\" was removed from the shared library",
            BookRemovedReason.DeletedByOwner => $"\"{title}\" was removed by its owner",
            BookRemovedReason.DeletedByAdminFromOwner => $"\"{title}\" was removed",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }
}
