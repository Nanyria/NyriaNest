using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Application.Loans
{
    /// <summary>
    /// Loans (#83, #85): the owner lends their book to a name (someone without an account) and marks
    /// it returned; friends borrow through requests (BorrowRequestService), report their loan
    /// returned, and the owner confirms it here, as for any of their loans. Admins act on an owner's
    /// behalf, with a reason. The old self-service borrow and return went with the shared library's
    /// physical books (ADR 0004); this was LendingService until #85 merged the two.
    /// </summary>
    public interface ILoanService
    {
        /// <summary>The user's loans (My loans), newest first.</summary>
        Task<APIResponse<List<CheckedOutItemDto>>> GetCheckedOutBooksAsync(string userId);

        /// <summary>
        /// The friend who has the book says they returned it, and when (#85). It isn't a return until
        /// the owner confirms. Anyone else, or a book not lent to them, looks missing (404).
        /// </summary>
        Task<APIResponse<bool>> ReportReturnedAsync(int bookId, string borrowerId, ReturnBookRequest request);

        /// <summary>The owner lends their book. Someone else's book or a shared-library book looks missing (404).</summary>
        Task<APIResponse<bool>> LendOwnBookAsync(int bookId, string ownerId, LendBookRequest request);

        /// <summary>The owner marks their book returned. Someone else's book or a shared-library book looks missing (404).</summary>
        Task<APIResponse<bool>> ReturnOwnBookAsync(int bookId, string ownerId, ReturnBookRequest request);

        /// <summary>An admin lends a book someone owns. Needs a reason unless it's the admin's own book; the owner is told.</summary>
        Task<APIResponse<bool>> LendAsAdminAsync(int bookId, string adminId, AdminLendBookRequest request);

        /// <summary>An admin marks a book someone owns returned. Needs a reason unless it's the admin's own book; the owner is told.</summary>
        Task<APIResponse<bool>> ReturnAsAdminAsync(int bookId, string adminId, AdminReturnBookRequest request);
    }

    public class LoanService : ILoanService
    {
        // The due date until the owner can choose one (#133); the demo seeder uses the same
        private const int DefaultLoanDays = 14;
        private const string DateFormat = "yyyy-MM-dd";
        private const string ReasonMessage = "Give a reason: this book belongs to someone else.";

        private readonly IBookRepo _bookRepo;
        private readonly IAppDbContext _dbContext;
        private readonly INotificationService _notifications;
        private readonly ILogger<LoanService> _logger;
        private readonly IBorrowRequestService _requests;
        private readonly IUserRepo _userRepo;
        private readonly IMapper _mapper;

        public LoanService(IBookRepo bookRepo, IAppDbContext dbContext,
            INotificationService notifications, ILogger<LoanService> logger, IBorrowRequestService requests,
            IUserRepo userRepo, IMapper mapper)
        {
            _requests = requests;
            _bookRepo = bookRepo;
            _dbContext = dbContext;
            _notifications = notifications;
            _logger = logger;
            _userRepo = userRepo;
            _mapper = mapper;
        }

        // My loans (#65): with the owner, for a friend's book, and a return I reported (#85 step 9a)
        public async Task<APIResponse<List<CheckedOutItemDto>>> GetCheckedOutBooksAsync(string userId)
        {
            var response = new APIResponse<List<CheckedOutItemDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user == null)
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }
            var checkedOutBooks = await _dbContext.CheckOutItems
                .Where(r => r.UserId == userId)
                .Include(r => r.Book).ThenInclude(b => b.Owner)
                .OrderByDescending(r => r.CheckOutDate)
                .ToListAsync();

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = _mapper.Map<List<CheckedOutItemDto>>(checkedOutBooks);
            return response;
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

        public async Task<APIResponse<bool>> ReportReturnedAsync(int bookId, string borrowerId, ReturnBookRequest request)
        {
            var book = await _bookRepo.GetByIdAsync(bookId);
            var loan = book?.CheckedOutBy;
            if (book == null || loan == null || loan.UserId != borrowerId)
                return NotFound(bookId);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var returnedOn = request.ReturnedOn ?? today;
            if (returnedOn > today)
                return Failure(HttpStatusCode.BadRequest, "The return date can't be in the future.");
            if (returnedOn < DateOnly.FromDateTime(loan.CheckOutDate))
                return Failure(HttpStatusCode.BadRequest, "The return date can't be before you borrowed it.");

            // Reporting again just changes the date; the book stays lent until the owner confirms
            loan.ReportedReturnedDate = returnedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            _notifications.NotifyReturnReported(book, loan.User?.UserName ?? "", returnedOn);

            return await SaveAsync(book, "reported returned");
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
                return Failure(HttpStatusCode.BadRequest, "Friends borrow by asking for the book. Here you lend it to a name.");
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

            var someoneElses = IsSomeoneElses(book, actorId);
            if (someoneElses && string.IsNullOrWhiteSpace(reason))
                return Failure(HttpStatusCode.BadRequest, ReasonMessage);

            // #85: a friend's loan is confirmed with the date they reported, unless the owner gives another.
            // Without a report the owner's word is enough: returned today.
            var isMemberLoan = loan.UserId != null;
            returnedOn ??= loan.ReportedReturnedDate is { } reported ? DateOnly.FromDateTime(reported) : null;

            if (returnedOn > DateOnly.FromDateTime(DateTime.UtcNow))
                return Failure(HttpStatusCode.BadRequest, "The return date can't be in the future.");
            if (returnedOn < DateOnly.FromDateTime(loan.CheckOutDate))
                return Failure(HttpStatusCode.BadRequest, "The return date can't be before the book was lent.");

            var now = DateTime.UtcNow;
            var borrower = loan.BorrowerName ?? loan.User?.UserName ?? "";
            var borrowerId = loan.UserId;
            _dbContext.CheckOutItems.Remove(loan);
            book.CheckedOutBy = null;
            book.BookStatus = BookStatusEnum.Available;
            book.AvailabilityDate = now; // available from now, as when a book is added

            // #85: friends waiting for it: the queue opens from now, when the owner knows it's back
            await _requests.OpenQueueAsync(book);

            // One row: "Returned" already says it's back on the shelf. A friend's loan is recorded
            // on the confirmed date (#85); a name loan keeps the time it was marked, as in #83.
            var notes = $"Returned by {borrower}" + OnDate(returnedOn) + ByAnAdmin(someoneElses, reason);
            var recordedAt = isMemberLoan && returnedOn is { } confirmed && confirmed < DateOnly.FromDateTime(now)
                ? confirmed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                : now;
            AddHistory(book, actorId, BookStatusEnum.Returned, notes, recordedAt);
            if (someoneElses)
                _notifications.NotifyOwnerOfAdminReturn(book, borrower, reason!.Trim());
            if (borrowerId != null)
                _notifications.NotifyReturnConfirmed(borrowerId, book, book.Owner?.UserName ?? "");

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
