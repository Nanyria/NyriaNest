using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.Loans
{
    /// <summary>
    /// When a friend's book comes back, its queue opens (#85): everyone who reserved it gets an open
    /// request that keeps the date they started waiting, and the owner chooses, as for any requests.
    /// The reservations become those requests. Used by returns; doesn't save: the caller saves it
    /// together with the return.
    /// </summary>
    public interface IQueueHandOver
    {
        /// <summary>
        /// If the book is free, turns each reservation into an open request (waiting since the
        /// reservation, available from now) and tells each friend, and the owner once. Sets the book's
        /// status and date. Needs the book's loan, reservations and owner loaded.
        /// </summary>
        Task HandOnAsync(Book book);
    }

    public class QueueHandOver : IQueueHandOver
    {
        private readonly IAppDbContext _db;
        private readonly IUserRepo _userRepo;
        private readonly INotificationService _notifications;
        private readonly IAvailabilityService _availability;
        private readonly TimeProvider _clock;

        public QueueHandOver(IAppDbContext db, IUserRepo userRepo, INotificationService notifications,
            IAvailabilityService availability, TimeProvider clock)
        {
            _db = db;
            _userRepo = userRepo;
            _notifications = notifications;
            _availability = availability;
            _clock = clock;
        }

        public async Task HandOnAsync(Book book)
        {
            // Only friends' books are handed on, and only when nobody has them
            if (book.OwnerId == null || book.AdminStatus != null || book.CheckedOutBy != null)
                return;

            var now = _clock.GetUtcNow().UtcDateTime;
            var queue = book.Reservations.OrderBy(r => r.ReservationDate).ToList();
            var waitingIds = queue.Select(r => r.UserId).ToList();
            var names = await _userRepo.FindByCondition<User>(u => waitingIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");
            var ownerName = book.Owner?.UserName ?? "";

            var asked = new List<string>();
            foreach (var reservation in queue)
            {
                book.Reservations.Remove(reservation);
                _db.ReservationItems.Remove(reservation);

                // Reserving is refused while you have an open request, but never ask twice
                var alreadyAsked = await _db.BorrowRequests.AnyAsync(r =>
                    r.BookId == book.BookId && r.BorrowerId == reservation.UserId && r.Status == BorrowRequestStatus.Requested);
                if (alreadyAsked)
                    continue;

                _db.BorrowRequests.Add(new BorrowRequest
                {
                    BookId = book.BookId,
                    BorrowerId = reservation.UserId,
                    Status = BorrowRequestStatus.Requested,
                    RequestedAt = reservation.ReservationDate, // waiting since they reserved
                    AvailableAt = now,                         // the reminder and expiry count from here
                });
                _notifications.NotifyQueueTurn(reservation.UserId, book, ownerName);
                asked.Add(names.GetValueOrDefault(reservation.UserId, ""));
            }

            // One message to the owner, naming everyone waiting, in the order they started
            if (asked.Count > 0)
                _notifications.NotifyQueueOpened(book, asked);

            // Free, with no queue left: available from now
            book.BookStatus = BookStatusEnum.Available;
            _availability.RecalculateAvailabilityDates(book);
        }
    }
}
