using NyriaNest.Application.Common;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Application.Loans
{
    public interface IAvailabilityService
    {
        Task<APIResponse<Book>> UpdateBookStatusAsync(Book book, string? userId, BookStatusEnum bookStatus, string? notes);
        void RecalculateAvailabilityDates(Book book);
        void AddStatusHistoryItem(User user, Book book, BookStatusEnum bookStatus, string? notes);
        Book GetBookStatus(Book book, BookStatusEnum bookStatus);
    }

    /// <summary>
    /// A book's status, the reservation queue's expected dates, and the status history,
    /// shared by loans, reservations, the admin status and user deletion.
    /// Moved out of BookService unchanged (#116).
    /// </summary>
    public class AvailabilityService : IAvailabilityService
    {
        // How long a loan lasts. Used for the due date and for spacing out the
        // reservation queue, so the two can't drift apart.
        public const int LoanPeriodMonths = 1;

        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IAppDbContext _dbContext;

        public AvailabilityService(IUserRepo userRepo, IBookRepo bookRepo, IAppDbContext dbContext)
        {
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _dbContext = dbContext;
        }

        public async Task<APIResponse<Book>> UpdateBookStatusAsync(Book book, string? userId, BookStatusEnum bookStatus, string? notes)
        {
            var response = new APIResponse<Book>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            User? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userRepo.GetByIdAsync<User>(userId);

            if (book == null)
            {
                response.ErrorMessages.Add("Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            var newBookStatus = GetBookStatus(book, bookStatus);

            // Only add status history if user is present
            if (user != null)
                AddStatusHistoryItem(user, book, bookStatus, notes);

            book.BookStatus = newBookStatus.BookStatus;
            book.CheckedOutBy = newBookStatus.CheckedOutBy ?? book.CheckedOutBy;
            book.Reservations = newBookStatus.Reservations ?? book.Reservations;

            if (user != null)
                await _userRepo.UpdateAsync(user);

            await _bookRepo.UpdateAsync(book);
            await _bookRepo.SaveAsync();

            response.Result = newBookStatus;
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }
        /// <summary>
        /// Recalculates when the book is expected to be available for each person in
        /// the reservation queue. Call it whenever the loan or the queue changes.
        /// A reservation's AvailabilityDate is an estimate, not a hold expiry.
        /// </summary>
        // Plain (not async) on purpose: it only updates tracked entities in memory,
        // and callers used to block on it with .Wait(), which can deadlock.
        public void RecalculateAvailabilityDates(Book book)
        {
            // The queue starts when the current loan is due, or today if nobody has the book
            var baseDate = book.CheckedOutBy?.ReturnDate ?? DateTime.UtcNow;

            // First come, first served
            var queue = book.Reservations
                .OrderBy(r => r.ReservationDate)
                .ToList();

            // Each person after the first waits one more loan period
            for (int i = 0; i < queue.Count; i++)
            {
                queue[i].AvailabilityDate = baseDate.AddMonths(i * LoanPeriodMonths);
            }

            // The book's own date is when the last person in the queue gets it
            // (or when it's free, if there's no queue)
            book.AvailabilityDate = queue.Count > 0
                ? queue[^1].AvailabilityDate ?? baseDate
                : baseDate;
        }
        public void AddStatusHistoryItem(User user, Book book, BookStatusEnum bookStatus, string? notes)
        {
            var statusHistoryItem = new StatusHistoryItem
            {
                UserId = user.Id,
                BookId = book.BookId,
                BookStatus = bookStatus,
                Timestamp = DateTime.UtcNow,
                Notes = notes
            };

            _dbContext.StatusHistoryItems.Add(statusHistoryItem);
            user.UserHistory.Add(statusHistoryItem);
            book.StatusHistory.Add(statusHistoryItem);

        }
        public Book GetBookStatus(Book book, BookStatusEnum bookStatus)
        {
            // An admin-set status (Lost/Removed) always wins
            if (book.AdminStatus is BookStatusEnum.Lost or BookStatusEnum.Removed)
            {
                book.BookStatus = book.AdminStatus.Value;
                return book;
            }
            // If the book was checked out and the user is now removed
            if (book.CheckedOutBy == null && (bookStatus == BookStatusEnum.CheckedOut || bookStatus == BookStatusEnum.Returned))
            {
                // If there are reservations, set to Reserved, else Available
                book.BookStatus = (book.Reservations != null && book.Reservations.Any())
                    ? BookStatusEnum.Reserved
                    : BookStatusEnum.Available;
            }
            else if (bookStatus == BookStatusEnum.Returned)
            {
                // Returned while someone still has the book: a cancelled reservation,
                // not a return (a return clears CheckedOutBy first and is handled above).
                // The loan still decides, so the book stays checked out (#39).
                book.BookStatus = BookStatusEnum.CheckedOut;
            }
            else if (book.BookStatus != bookStatus)
            {
                // Determine status based on CheckedOutBy and Reservations
                book.BookStatus = book.CheckedOutBy != null
                    ? BookStatusEnum.CheckedOut
                    : (book.Reservations != null && book.Reservations.Any())
                        ? BookStatusEnum.Reserved
                        : BookStatusEnum.Available;
            }

            return book;
        }
    }
}
