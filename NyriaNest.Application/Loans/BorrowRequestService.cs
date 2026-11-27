using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Loans;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using NyriaNest.Domain.Rules;
using System.Net;

namespace NyriaNest.Application.Loans
{
    /// <summary>
    /// A friend asks to borrow a physical book, and the owner chooses (#85). Who may ask is the
    /// Library's rule (BookVisibility): a book you can't see looks missing. Several friends may ask
    /// while the book is free, one open request each; once it's lent, they reserve instead.
    /// Accepting one lends the book as a normal loan; the other open requests join the queue.
    /// A request that isn't yours to answer or withdraw looks missing (404).
    /// </summary>
    public interface IBorrowRequestService
    {
        Task<APIResponse<BorrowRequestDto>> RequestAsync(string borrowerId, int bookId);
        Task<APIResponse<BorrowRequestDto>> AcceptAsync(string ownerId, int requestId);
        Task<APIResponse<BorrowRequestDto>> DeclineAsync(string ownerId, int requestId);
        Task<APIResponse<BorrowRequestDto>> CancelAsync(string borrowerId, int requestId);
    }

    public class BorrowRequestService : IBorrowRequestService
    {
        private const string AlreadyAnswered = "This request has already been answered.";

        private readonly IAppDbContext _db;
        private readonly IUserRepo _userRepo;
        private readonly INotificationService _notifications;
        private readonly IAvailabilityService _availability;
        private readonly TimeProvider _clock;
        private readonly ILogger<BorrowRequestService> _logger;

        public BorrowRequestService(IAppDbContext db, IUserRepo userRepo, INotificationService notifications,
            IAvailabilityService availability, TimeProvider clock, ILogger<BorrowRequestService> logger)
        {
            _db = db;
            _userRepo = userRepo;
            _notifications = notifications;
            _availability = availability;
            _clock = clock;
            _logger = logger;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public async Task<APIResponse<BorrowRequestDto>> RequestAsync(string borrowerId, int bookId)
        {
            // The Library's rule: a friend who isn't shared with, a stranger or a taken-down book all look missing
            var book = await _db.Books
                .Where(BookVisibility.VisibleTo(borrowerId, _db.Friendships))
                .Include(b => b.CheckedOutBy)
                .Include(b => b.Owner)
                .FirstOrDefaultAsync(b => b.BookId == bookId);
            if (book == null)
                return Failure(HttpStatusCode.NotFound, $"Book {bookId} was not found.");
            if (book.OwnerId == null)
                return Failure(HttpStatusCode.BadRequest, "Books in the shared library are read, not borrowed.");
            if (book.OwnerId == borrowerId)
                return Failure(HttpStatusCode.BadRequest, "This is your own book.");
            if (book.CheckedOutBy != null)
                return Failure(HttpStatusCode.Conflict, "This book is lent out right now. You can reserve it instead.");
            // A free book can always be asked for: the owner sees everyone's "waiting since" and chooses
            if (await _db.BorrowRequests.AnyAsync(r =>
                    r.BookId == bookId && r.BorrowerId == borrowerId && r.Status == BorrowRequestStatus.Requested))
                return Failure(HttpStatusCode.Conflict, "You've already asked to borrow this book.");

            var borrowerName = await UserNameOfAsync(borrowerId);
            var now = Now;
            var request = new BorrowRequest
            {
                BookId = book.BookId,
                BorrowerId = borrowerId,
                Status = BorrowRequestStatus.Requested,
                RequestedAt = now,
                AvailableAt = now, // the book is free, so it's available from the moment they ask
            };
            _db.BorrowRequests.Add(request);
            _notifications.NotifyBorrowRequested(book, borrowerName);

            // The unique index (one open request per friend and book) catches a double click
            var saved = await SaveAsync(book.BookId, "asked for", "You've already asked to borrow this book.");
            return saved ?? Ok(ToDto(request, book, borrowerName));
        }

        public async Task<APIResponse<BorrowRequestDto>> AcceptAsync(string ownerId, int requestId)
        {
            var request = await OwnersRequestAsync(ownerId, requestId);
            if (request == null)
                return NotFound(requestId);
            if (request.Status != BorrowRequestStatus.Requested)
                return Failure(HttpStatusCode.Conflict, AlreadyAnswered);

            var book = request.Book;
            if (book.AdminStatus != null)
                return Failure(HttpStatusCode.Conflict, "This book isn't available.");
            if (book.CheckedOutBy != null)
                return Failure(HttpStatusCode.Conflict, "This book is already lent out. Mark it returned first.");

            var now = Now;
            var ownerName = book.Owner?.UserName ?? "";
            var borrowerName = request.Borrower.UserName ?? "";

            // A normal loan, as the rest of the loan system knows it
            var loan = new CheckedOutItem
            {
                BookId = book.BookId,
                UserId = request.BorrowerId,
                CheckOutDate = now,
                ReturnDate = now.AddMonths(AvailabilityService.LoanPeriodMonths),
            };
            _db.CheckOutItems.Add(loan);
            book.CheckedOutBy = loan;
            book.BookStatus = BookStatusEnum.CheckedOut;
            request.Status = BorrowRequestStatus.Lent;
            request.AnsweredAt = now;

            // A reservation of the borrower's own is used up by the loan
            var ownReservation = book.Reservations.FirstOrDefault(r => r.UserId == request.BorrowerId);
            if (ownReservation != null)
            {
                book.Reservations.Remove(ownReservation);
                _db.ReservationItems.Remove(ownReservation);
            }

            // The owner chose: everyone else who asked joins the queue, keeping the time they asked
            var others = await _db.BorrowRequests
                .Where(r => r.BookId == book.BookId && r.Status == BorrowRequestStatus.Requested && r.Id != request.Id)
                .OrderBy(r => r.RequestedAt)
                .ToListAsync();
            foreach (var other in others)
            {
                other.Status = BorrowRequestStatus.Queued;
                other.AnsweredAt = now;
                if (book.Reservations.All(r => r.UserId != other.BorrowerId))
                {
                    var reservation = new ReservationItem
                    {
                        BookId = book.BookId,
                        UserId = other.BorrowerId,
                        ReservationDate = other.RequestedAt,
                    };
                    book.Reservations.Add(reservation);
                    _db.ReservationItems.Add(reservation);
                }
                _notifications.NotifyBorrowRequestQueued(other.BorrowerId, book, ownerName);
            }

            // The queue waits for this loan's due date; the book's date is when the last in line gets it
            _availability.RecalculateAvailabilityDates(book);

            _db.StatusHistoryItems.Add(new StatusHistoryItem
            {
                BookId = book.BookId,
                UserId = ownerId,
                BookStatus = BookStatusEnum.CheckedOut,
                Timestamp = now,
                Notes = $"Lent to {borrowerName}",
            });
            _notifications.NotifyBorrowRequestAccepted(request.BorrowerId, book, ownerName, loan.ReturnDate);

            // CheckOutItems.BookId is unique: lending it twice at the same moment fails here, not with a 500
            var saved = await SaveAsync(book.BookId, "lent", "Someone just lent this book. Please refresh.");
            return saved ?? Ok(ToDto(request, book, borrowerName));
        }

        public async Task<APIResponse<BorrowRequestDto>> DeclineAsync(string ownerId, int requestId)
        {
            var request = await OwnersRequestAsync(ownerId, requestId);
            if (request == null)
                return NotFound(requestId);
            if (request.Status != BorrowRequestStatus.Requested)
                return Failure(HttpStatusCode.Conflict, AlreadyAnswered);

            request.Status = BorrowRequestStatus.Declined;
            request.AnsweredAt = Now;
            _notifications.NotifyBorrowRequestDeclined(request.BorrowerId, request.Book, request.Book.Owner?.UserName ?? "");

            var saved = await SaveAsync(request.BookId, "declined", AlreadyAnswered);
            return saved ?? Ok(ToDto(request, request.Book, request.Borrower.UserName ?? ""));
        }

        public async Task<APIResponse<BorrowRequestDto>> CancelAsync(string borrowerId, int requestId)
        {
            var request = await Requests().FirstOrDefaultAsync(r => r.Id == requestId && r.BorrowerId == borrowerId);
            if (request == null)
                return NotFound(requestId);
            if (request.Status != BorrowRequestStatus.Requested)
                return Failure(HttpStatusCode.Conflict, AlreadyAnswered);

            // The owner isn't told: nothing for them to do, and the request just leaves their list
            request.Status = BorrowRequestStatus.Cancelled;
            request.AnsweredAt = Now;

            var saved = await SaveAsync(request.BookId, "withdrawn", AlreadyAnswered);
            return saved ?? Ok(ToDto(request, request.Book, request.Borrower.UserName ?? ""));
        }

        // ---- Helpers ----

        private IQueryable<BorrowRequest> Requests() => _db.BorrowRequests
            .Include(r => r.Borrower)
            .Include(r => r.Book).ThenInclude(b => b.Owner)
            .Include(r => r.Book).ThenInclude(b => b.CheckedOutBy)
            .Include(r => r.Book).ThenInclude(b => b.Reservations);

        // Filtered on the owner too, so someone else's request looks exactly like a missing one
        private Task<BorrowRequest?> OwnersRequestAsync(string ownerId, int requestId) =>
            Requests().FirstOrDefaultAsync(r => r.Id == requestId && r.Book.OwnerId == ownerId);

        private async Task<string> UserNameOfAsync(string userId) =>
            await _userRepo.FindByCondition<User>(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync() ?? "";

        /// <summary>Saves; null when it worked, otherwise the response to give. A unique index clash is a 409.</summary>
        private async Task<APIResponse<BorrowRequestDto>?> SaveAsync(int bookId, string action, string conflictMessage)
        {
            try
            {
                await _db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Could not save book {BookId} as {Action}.", bookId, action);
                return Failure(HttpStatusCode.Conflict, conflictMessage);
            }
        }

        private static BorrowRequestDto ToDto(BorrowRequest r, Book book, string borrowerName) => new()
        {
            Id = r.Id,
            BookId = book.BookId,
            BookTitle = book.Title,
            OwnerUserName = book.Owner?.UserName ?? "",
            BorrowerId = r.BorrowerId,
            BorrowerUserName = borrowerName,
            Status = r.Status,
            RequestedAt = r.RequestedAt,
            AvailableAt = r.AvailableAt,
            AnsweredAt = r.AnsweredAt,
        };

        private static APIResponse<BorrowRequestDto> Ok(BorrowRequestDto dto) => new()
        {
            IsSuccess = true,
            StatusCode = HttpStatusCode.OK,
            Result = dto,
        };

        private static APIResponse<BorrowRequestDto> NotFound(int requestId) =>
            Failure(HttpStatusCode.NotFound, $"Borrow request {requestId} was not found.");

        private static APIResponse<BorrowRequestDto> Failure(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = { message },
        };
    }
}
