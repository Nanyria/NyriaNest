using AutoMapper;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Books;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Application.Loans
{
    public interface ILoanService
    {
        Task<APIResponse<List<CheckedOutItemDto>>> GetCheckedOutBooksAsync(string userId);
        Task<APIResponse<UserDto>> CheckOutBookAsync(string userId, int bookId);
        Task<APIResponse<UserDto>> ReturnBookAsync(string userId, int bookId);
    }

    /// <summary>
    /// Loans: the user's loans, borrowing and returning. Borrowing uses up the
    /// borrower's own reservation through ReservationService (one way only).
    /// Moved out of UserActionsService unchanged (#116).
    /// </summary>
    public class LoanService : ILoanService
    {
        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IBookService _bookService;
        private readonly IAvailabilityService _availability;
        private readonly IReservationService _reservations;
        private readonly IAppDbContext _dbContext;
        private readonly IMapper _mapper;

        public LoanService(IUserRepo userRepo, IBookRepo bookRepo, IBookService bookService,
            IAvailabilityService availability, IReservationService reservations, IAppDbContext dbContext, IMapper mapper)
        {
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _bookService = bookService;
            _availability = availability;
            _reservations = reservations;
            _dbContext = dbContext;
            _mapper = mapper;
        }

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
                .Include(r => r.Book)
                .OrderByDescending(r => r.CheckOutDate)
                .ToListAsync();

            // Map to DTOs
            var checkedOutDtos = _mapper.Map<List<CheckedOutItemDto>>(checkedOutBooks);

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = checkedOutDtos;
            return response;
        }

        public async Task<APIResponse<UserDto>> CheckOutBookAsync(string userId, int bookId)
        {
            var response = new APIResponse<UserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };


            var bookResponse = await _bookService.GetBookByIdAsync(bookId);
            var user = await _userRepo.GetByIdAsync<User>(userId);
            var book = bookResponse.Result;


            if (user == null || book == null)
            {
                response.ErrorMessages.Add("User or Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            // Check every rule before changing anything, so a refused check-out
            // never leaves a half-made change behind.

            // A book an admin has marked Lost or Removed can't be borrowed
            if (book.AdminStatus != null)
            {
                response.ErrorMessages.Add("This book isn't available.");
                response.StatusCode = HttpStatusCode.Conflict;
                return response;
            }

            // Someone already has it. A book has only one loan at a time, so replacing
            // it would silently drop the other person's loan.
            if (book.CheckedOutBy != null)
            {
                response.ErrorMessages.Add(book.CheckedOutBy.UserId == user.Id
                    ? "You already have this book."
                    : "This book is checked out by someone else.");
                response.StatusCode = HttpStatusCode.Conflict;
                return response;
            }

            // If there's a queue, only the person at the front may borrow it
            var firstInLine = book.Reservations
                .OrderBy(r => r.ReservationDate)
                .FirstOrDefault();
            if (firstInLine != null && firstInLine.UserId != user.Id)
            {
                response.ErrorMessages.Add("This book is reserved for someone else.");
                response.StatusCode = HttpStatusCode.Conflict;
                return response;
            }

            _reservations.RemoveReservation(user, book); // Does nothing if they had no reservation
            SetCheckedOutBook(user, book);

            // The rest of the queue now waits for this new loan's due date
            _availability.RecalculateAvailabilityDates(book);

            try
            {
                // UpdateBookStatusAsync saves the changes
                await _availability.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.CheckedOut, $"Checked out by {user.UserName}");
                await _userRepo.UpdateAsync(user);
                await _userRepo.SaveAsync();
            }
            catch (DbUpdateException)
            {
                // CheckOutItems.BookId is unique (one loan per book). If two people borrow
                // at the same moment, the second save fails here instead of crashing with a 500.
                response.ErrorMessages.Add("Someone just borrowed this book. Please refresh.");
                response.StatusCode = HttpStatusCode.Conflict;
                return response;
            }

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = _mapper.Map<UserDto>(user);
            return response;
        }

        // Creates the loan. Not async: it only adds tracked entities, saving happens later.
        public void SetCheckedOutBook(User user, Book book)
        {
            var checkedOutItem = new CheckedOutItem
            {
                BookId = book.BookId,
                UserId = user.Id,
                CheckOutDate = DateTime.UtcNow,
                ReturnDate = DateTime.UtcNow.AddMonths(AvailabilityService.LoanPeriodMonths), // the due date
            };
            user.CheckedOutBooks.Add(checkedOutItem);
            book.CheckedOutBy = checkedOutItem;
        }

        public async Task<APIResponse<UserDto>> ReturnBookAsync(string userId, int bookId)
        {
            var response = new APIResponse<UserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            var bookResponse = await _bookService.GetBookByIdAsync(bookId);
            var user = await _userRepo.GetByIdAsync<User>(userId);
            var book = bookResponse.Result;

            if (user != null && book != null)
            {
                // Only the borrower can return a book. Without this check, a return of a
                // book you don't have would still log "Returned by ..." and report success.
                if (!RemoveFromCheckedOutList(user, book))
                {
                    response.ErrorMessages.Add("You don't have this book checked out.");
                    response.StatusCode = HttpStatusCode.Conflict;
                    return response;
                }

                // The book may come back early, so the queue restarts from today
                // instead of from the old due date
                _availability.RecalculateAvailabilityDates(book);

                await _availability.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.Returned, $"Returned by {user.UserName}");

                await _userRepo.SaveAsync();
                await _bookRepo.SaveAsync();

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = _mapper.Map<UserDto>(user);
            }
            else
            {
                response.ErrorMessages.Add("User or Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }

            return response;
        }
        public bool RemoveFromCheckedOutList(User user, Book book)
        {
            // Find the reservation item for the given book
            var checkedOutItem = user.CheckedOutBooks.FirstOrDefault(r => r.BookId == book.BookId);
            if (checkedOutItem != null)
            {
                // Remove the reservation from both the user and the book
                user.CheckedOutBooks.Remove(checkedOutItem);
                book.CheckedOutBy = null;

                _dbContext.CheckOutItems.Remove(checkedOutItem);

                return true;
            }

            return false;
        }
    }
}
