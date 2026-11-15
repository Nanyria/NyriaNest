using Microsoft.Extensions.Logging;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Application.Loans
{
    /// <summary>
    /// Lending a user's own book and marking it returned (#83). In #83 a book is lent to a name
    /// (someone without an account); lending to members comes with friends (#85), which will
    /// build on this. The old borrow flow (LoanService) stays for the shared library until then.
    /// </summary>
    public interface ILendingService
    {
        /// <summary>The owner lends their book. Someone else's book or a shared-library book looks missing (404).</summary>
        Task<APIResponse<bool>> LendOwnBookAsync(int bookId, string ownerId, LendBookRequest request);

        /// <summary>The owner marks their book returned. Someone else's book or a shared-library book looks missing (404).</summary>
        Task<APIResponse<bool>> ReturnOwnBookAsync(int bookId, string ownerId, ReturnBookRequest request);

        /// <summary>An admin lends a book someone owns. Needs a reason unless it's the admin's own book; the owner is told.</summary>
        Task<APIResponse<bool>> LendAsAdminAsync(int bookId, string adminId, AdminLendBookRequest request);

        /// <summary>An admin marks a book someone owns returned. Needs a reason unless it's the admin's own book; the owner is told.</summary>
        Task<APIResponse<bool>> ReturnAsAdminAsync(int bookId, string adminId, AdminReturnBookRequest request);
    }

    public class LendingService : ILendingService
    {
        // The due date until the owner can choose one (#133); the demo seeder uses the same
        private const int DefaultLoanDays = 14;
        private const string DateFormat = "yyyy-MM-dd";
        private const string ReasonMessage = "Give a reason: this book belongs to someone else.";

        private readonly IBookRepo _bookRepo;
        private readonly IAppDbContext _dbContext;
        private readonly INotificationService _notifications;
        private readonly ILogger<LendingService> _logger;

        public LendingService(IBookRepo bookRepo, IAppDbContext dbContext,
            INotificationService notifications, ILogger<LendingService> logger)
        {
            _bookRepo = bookRepo;
            _dbContext = dbContext;
            _notifications = notifications;
            _logger = logger;
        }

        // ---- Entry points: find the book, then the same path for everyone ----

        public async Task<APIResponse<bool>> LendOwnBookAsync(int bookId, string ownerId, LendBookRequest request)
        {
            var book = await GetOwnAsync(bookId, ownerId);
            return book == null
                ? NotFound(bookId)
                : await LendAsActorAsync(book, ownerId, request.BorrowerUserId, request.BorrowerName, request.LentOn, reason: null);
        }

        public async Task<APIResponse<bool>> ReturnOwnBookAsync(int bookId, string ownerId, ReturnBookRequest request)
        {
            var book = await GetOwnAsync(bookId, ownerId);
            return book == null
                ? NotFound(bookId)
                : await ReturnAsActorAsync(book, ownerId, request.ReturnedOn, reason: null);
        }

        public async Task<APIResponse<bool>> LendAsAdminAsync(int bookId, string adminId, AdminLendBookRequest request)
        {
            var (book, failure) = await GetOwnedBookAsync(bookId);
            return failure ?? await LendAsActorAsync(book!, adminId,
                request.BorrowerUserId, request.BorrowerName, request.LentOn, request.Reason);
        }

        public async Task<APIResponse<bool>> ReturnAsAdminAsync(int bookId, string adminId, AdminReturnBookRequest request)
        {
            var (book, failure) = await GetOwnedBookAsync(bookId);
            return failure ?? await ReturnAsActorAsync(book!, adminId, request.ReturnedOn, request.Reason);
        }

        // ---- The checks every lend and return goes through, whoever asks ----

        private async Task<APIResponse<bool>> LendAsActorAsync(Book book, string actorId,
            string? borrowerUserId, string? borrowerName, DateOnly? lentOn, string? reason)
        {
            // Exactly one borrower. A member's user id is refused until friends exist (#85).
            var name = borrowerName?.Trim();
            if (borrowerUserId != null && name != null)
                return Failure(HttpStatusCode.BadRequest, "Lend the book to a member or to a name, not both.");
            if (borrowerUserId != null)
                return Failure(HttpStatusCode.BadRequest, "Lending to members comes with friends. Lend it to a name for now.");
            if (string.IsNullOrEmpty(name))
                return Failure(HttpStatusCode.BadRequest, "Say who you lent the book to.");
            if (name.Length > BorrowerNameLimit.MaxLength)
                return Failure(HttpStatusCode.BadRequest, $"The name can be at most {BorrowerNameLimit.MaxLength} characters.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (lentOn > today)
                return Failure(HttpStatusCode.BadRequest, "The lend date can't be in the future.");

            var someoneElses = IsSomeoneElses(book, actorId);
            if (someoneElses && string.IsNullOrWhiteSpace(reason))
                return Failure(HttpStatusCode.BadRequest, ReasonMessage);

            // One loan per book: lending it again would silently drop the first loan
            if (book.CheckedOutBy != null)
                return Failure(HttpStatusCode.Conflict, "This book is already lent out. Mark it returned first.");

            // A chosen earlier date counts from midnight; today keeps the time of day
            var now = DateTime.UtcNow;
            var lentAt = lentOn is { } date && date < today
                ? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                : now;
            var loan = new CheckedOutItem
            {
                BookId = book.BookId,
                BorrowerName = name,
                CheckOutDate = lentAt,
                ReturnDate = lentAt.AddDays(DefaultLoanDays),
            };
            _dbContext.CheckOutItems.Add(loan);
            book.BookStatus = BookStatusEnum.CheckedOut;
            book.AvailabilityDate = loan.ReturnDate;

            var notes = $"Lent to {name}" + OnDate(lentOn) + ByAnAdmin(someoneElses, reason);
            AddHistory(book, actorId, BookStatusEnum.CheckedOut, notes, now);
            if (someoneElses)
                _notifications.NotifyOwnerOfAdminLend(book, name, reason!.Trim());

            return await SaveAsync(book, "lent");
        }

        private async Task<APIResponse<bool>> ReturnAsActorAsync(Book book, string actorId, DateOnly? returnedOn, string? reason)
        {
            var loan = book.CheckedOutBy;
            if (loan == null)
                return Failure(HttpStatusCode.Conflict, "This book isn't lent out.");

            // Member loans only come from the old borrow flow; #85 decides how they end
            if (loan.BorrowerName == null)
                return Failure(HttpStatusCode.Conflict, "This book is borrowed by a member and can't be marked returned here.");

            var someoneElses = IsSomeoneElses(book, actorId);
            if (someoneElses && string.IsNullOrWhiteSpace(reason))
                return Failure(HttpStatusCode.BadRequest, ReasonMessage);

            if (returnedOn > DateOnly.FromDateTime(DateTime.UtcNow))
                return Failure(HttpStatusCode.BadRequest, "The return date can't be in the future.");
            if (returnedOn < DateOnly.FromDateTime(loan.CheckOutDate))
                return Failure(HttpStatusCode.BadRequest, "The return date can't be before the book was lent.");

            var now = DateTime.UtcNow;
            _dbContext.CheckOutItems.Remove(loan);
            book.BookStatus = BookStatusEnum.Available;
            book.AvailabilityDate = now; // available from now, as when a book is added

            // One row: "Returned" already says it's back on the shelf
            var notes = $"Returned by {loan.BorrowerName}" + OnDate(returnedOn) + ByAnAdmin(someoneElses, reason);
            AddHistory(book, actorId, BookStatusEnum.Returned, notes, now);
            if (someoneElses)
                _notifications.NotifyOwnerOfAdminReturn(book, loan.BorrowerName, reason!.Trim());

            return await SaveAsync(book, "marked returned");
        }

        // ---- Helpers ----

        // Someone else's book and a shared-library book both look missing
        private async Task<Book?> GetOwnAsync(int bookId, string ownerId)
        {
            var book = await _bookRepo.GetByIdAsync(bookId);
            return book?.OwnerId == ownerId ? book : null;
        }

        // For admins: the book has to exist and belong to someone (shared-library e-books aren't lent by name)
        private async Task<(Book? Book, APIResponse<bool>? Failure)> GetOwnedBookAsync(int bookId)
        {
            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book == null)
                return (null, NotFound(bookId));
            if (book.OwnerId == null)
                return (null, Failure(HttpStatusCode.BadRequest, "Only books someone owns can be lent by name."));
            return (book, null);
        }

        // By actor, not role, as for admin edit and delete in BookService
        private static bool IsSomeoneElses(Book book, string actorId) =>
            book.OwnerId != null && book.OwnerId != actorId;

        private static string OnDate(DateOnly? date) =>
            date is { } d ? $" on {d.ToString(DateFormat)}" : "";

        private static string ByAnAdmin(bool someoneElses, string? reason) =>
            someoneElses ? $" (by an admin: {reason!.Trim()})" : "";

        private void AddHistory(Book book, string actorId, BookStatusEnum status, string notes, DateTime timestamp) =>
            _dbContext.StatusHistoryItems.Add(new StatusHistoryItem
            {
                BookId = book.BookId,
                UserId = actorId,
                BookStatus = status,
                Timestamp = timestamp,
                Notes = notes,
            });

        // The loan, the book, the history row and any notification are saved together
        private async Task<APIResponse<bool>> SaveAsync(Book book, string action)
        {
            try
            {
                await _bookRepo.SaveAsync();
                return new APIResponse<bool> { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = true };
            }
            catch (Exception ex)
            {
                // Log the real error; the client gets a plain message, never EF's text
                _logger.LogError(ex, "Could not save book {BookId} as {Action}.", book.BookId, action);
                return Failure(HttpStatusCode.InternalServerError, $"The book couldn't be {action}.");
            }
        }

        private static APIResponse<bool> NotFound(int bookId) =>
            Failure(HttpStatusCode.NotFound, $"Book {bookId} was not found.");

        private static APIResponse<bool> Failure(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = { message },
        };
    }
}
