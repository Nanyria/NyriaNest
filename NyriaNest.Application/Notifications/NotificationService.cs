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

        /// <summary>Tells someone they got a friend request (#84). Doesn't save.</summary>
        void NotifyFriendRequestReceived(string addresseeId, string requesterUserName);

        /// <summary>Tells the requester their friend request was accepted, and that nothing is shared yet (#84). Doesn't save.</summary>
        void NotifyFriendRequestAccepted(string requesterId, string accepterUserName);

        /// <summary>Tells the owner a friend asks to borrow their book (#85). Doesn't save.</summary>
        void NotifyBorrowRequested(Book book, string borrowerUserName);

        /// <summary>Tells the friend the owner lent them the book, and when it's due (#85). Doesn't save.</summary>
        void NotifyBorrowRequestAccepted(string borrowerId, Book book, string ownerUserName, DateTime dueDate);

        /// <summary>Tells the friend the owner said no (#85). Doesn't save.</summary>
        void NotifyBorrowRequestDeclined(string borrowerId, Book book, string ownerUserName);

        /// <summary>Tells the friend the book went to someone else and they're now in the queue (#85). Doesn't save.</summary>
        void NotifyBorrowRequestQueued(string borrowerId, Book book, string ownerUserName);

        /// <summary>Tells a friend in the queue that the book is back and the owner has been asked (#85). Doesn't save.</summary>
        void NotifyQueueTurn(string borrowerId, Book book, string ownerUserName);

        /// <summary>Tells the owner their book's queue opened, naming everyone waiting (#85). Doesn't save.</summary>
        void NotifyQueueOpened(Book book, IReadOnlyList<string> waitingUserNames);

        /// <summary>Tells the owner a friend says they returned the book, and when; the owner confirms (#85). Doesn't save.</summary>
        void NotifyReturnReported(Book book, string borrowerUserName, DateOnly returnedOn);

        /// <summary>Tells the friend the owner confirmed the book is back (#85). Doesn't save.</summary>
        void NotifyReturnConfirmed(string borrowerId, Book book, string ownerUserName);

        /// <summary>Reminds the owner a friend is still waiting for an answer (#85, the daily job). Doesn't save.</summary>
        void NotifyRequestReminder(Book book, string borrowerUserName);

        /// <summary>Tells the friend their request expired unanswered, and that they can ask again (#85, the daily job). Doesn't save.</summary>
        void NotifyRequestExpired(string borrowerId, Book book, string ownerUserName);

        /// <summary>Reminds the owner to confirm a return a friend reported (#85, the daily job). Doesn't save.</summary>
        void NotifyReturnReminder(Book book, string borrowerUserName);

        /// <summary>Tells a friend their requests and reservations for the owner's books were cancelled, once (#85). Doesn't save.</summary>
        void NotifySharingEnded(string friendId, string ownerUserName);

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
            // Everyone with a current interaction (#83): a reservation or a favorite, and since #85 a
            // friend who has the book and an open request. Past reviews, ratings and finished requests
            // don't count. Favorites and requests go with the book, so they're read before the delete.
            var borrower = book.CheckedOutBy?.UserId; // a friend's loan; a loan to a name has no user
            var reservers = book.Reservations.Select(r => r.UserId).ToHashSet();
            var requesters = (await _dbContext.BorrowRequests
                .Where(r => r.BookId == book.BookId && r.Status == BorrowRequestStatus.Requested)
                .Select(r => r.BorrowerId)
                .ToListAsync()).ToHashSet();
            var favoriters = await _dbContext.FavoriteItems
                .Where(f => f.BookId == book.BookId)
                .Select(f => f.UserId)
                .ToListAsync();

            // A set: one person can be in several of these. One message each.
            var others = (borrower is null ? Enumerable.Empty<string>() : [borrower])
                .Union(reservers).Union(requesters).Union(favoriters)
                .Where(userId => userId != book.OwnerId);
            foreach (var userId in others)
            {
                // The biggest loss is the one that's named: the loan, the queue, the request, the favorite
                var consequence = userId == borrower ? "so your loan of it ended."
                    : reservers.Contains(userId) ? "so your reservation was cancelled."
                    : requesters.Contains(userId) ? "so your request to borrow it was cancelled."
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

        public void NotifyFriendRequestReceived(string addresseeId, string requesterUserName) =>
            _dbContext.NotificationItems.Add(new NotificationItem
            {
                UserId = addresseeId,
                Message = $"{requesterUserName} sent you a friend request. You can answer it in Friends.",
                Type = NotificationType.FriendRequestReceived,
            });

        // Accepting shares nothing by itself (ADR 0005), so the requester is asked whether they want to
        public void NotifyFriendRequestAccepted(string requesterId, string accepterUserName) =>
            _dbContext.NotificationItems.Add(new NotificationItem
            {
                UserId = requesterId,
                Message = $"{accepterUserName} accepted your friend request. Do you want to share your library or e-books with {accepterUserName}? You can turn it on in Friends.",
                Type = NotificationType.FriendRequestAccepted,
            });

        // ---- Borrow requests (#85). Each points at the book. ----

        public void NotifyBorrowRequested(Book book, string borrowerUserName) =>
            NotifyOwner(book, $"{borrowerUserName} asks to borrow \"{book.Title}\". You can answer in My books.",
                NotificationType.LoanRequested);

        public void NotifyBorrowRequestAccepted(string borrowerId, Book book, string ownerUserName, DateTime dueDate) =>
            AddBookNotification(borrowerId, book,
                $"{ownerUserName} lent you \"{book.Title}\". It's due back on {dueDate:yyyy-MM-dd}.",
                NotificationType.LoanRequestAnswered);

        public void NotifyBorrowRequestDeclined(string borrowerId, Book book, string ownerUserName) =>
            AddBookNotification(borrowerId, book,
                $"{ownerUserName} can't lend you \"{book.Title}\" this time.",
                NotificationType.LoanRequestAnswered);

        public void NotifyBorrowRequestQueued(string borrowerId, Book book, string ownerUserName) =>
            AddBookNotification(borrowerId, book,
                $"{ownerUserName} lent \"{book.Title}\" to someone else. You're in the queue for it now, in the order you asked.",
                NotificationType.LoanRequestAnswered);

        public void NotifyQueueTurn(string borrowerId, Book book, string ownerUserName) =>
            AddBookNotification(borrowerId, book,
                $"\"{book.Title}\" is back. {ownerUserName} has been asked to lend it to you.",
                NotificationType.ReservationAvailable);

        public void NotifyQueueOpened(Book book, IReadOnlyList<string> waitingUserNames) =>
            NotifyOwner(book, waitingUserNames.Count == 1
                    ? $"\"{book.Title}\" is back, and {waitingUserNames[0]} is waiting to borrow it. You can answer in My books."
                    : $"\"{book.Title}\" is back, and {waitingUserNames.Count} friends are waiting to borrow it: {string.Join(", ", waitingUserNames)}. You can choose in My books.",
                NotificationType.LoanRequested);

        public void NotifyReturnReported(Book book, string borrowerUserName, DateOnly returnedOn) =>
            NotifyOwner(book, $"{borrowerUserName} says they returned \"{book.Title}\" on {returnedOn:yyyy-MM-dd}. Confirm it in My books once you have it.",
                NotificationType.ReturnMarked);

        public void NotifyReturnConfirmed(string borrowerId, Book book, string ownerUserName) =>
            AddBookNotification(borrowerId, book,
                $"{ownerUserName} confirmed that \"{book.Title}\" is back. Thanks for returning it!",
                NotificationType.ReturnConfirmed);

        public void NotifyRequestReminder(Book book, string borrowerUserName) =>
            NotifyOwner(book, $"{borrowerUserName} is still waiting to borrow \"{book.Title}\". You can answer in My books.",
                NotificationType.LoanRequested);

        public void NotifyRequestExpired(string borrowerId, Book book, string ownerUserName) =>
            AddBookNotification(borrowerId, book,
                $"Your request to borrow \"{book.Title}\" expired because {ownerUserName} didn't answer. You can ask again.",
                NotificationType.LoanRequestAnswered);

        public void NotifyReturnReminder(Book book, string borrowerUserName) =>
            NotifyOwner(book, $"{borrowerUserName} said they returned \"{book.Title}\". Please confirm it in My books once you have it.",
                NotificationType.ReturnMarked);

        // About several books, so it points at none
        public void NotifySharingEnded(string friendId, string ownerUserName) =>
            _dbContext.NotificationItems.Add(new NotificationItem
            {
                UserId = friendId,
                Message = $"{ownerUserName} no longer shares their physical library with you, so your requests and reservations for {ownerUserName}'s books were cancelled.",
                Type = NotificationType.SharingEnded,
            });

        private void AddBookNotification(string userId, Book book, string message, NotificationType type) =>
            _dbContext.NotificationItems.Add(new NotificationItem
            {
                UserId = userId,
                Message = message,
                Type = type,
                BookId = book.BookId,
            });

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
