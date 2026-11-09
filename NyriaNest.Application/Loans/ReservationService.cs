using AutoMapper;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Services;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Application.Loans
{
    public interface IReservationService
    {
        Task<APIResponse<List<ReservationItemDto>>> GetReservedBooksAsync(string userId);
        Task<APIResponse<UserDto>> ReserveBookAsync(string userId, int bookId);
        Task<APIResponse<UserDto>> CancelReservationAsync(string userId, int bookId);
        // Takes the user's reservation off the book and moves the queue up, without
        // saving. Borrowing uses it too: a loan uses up the borrower's reservation.
        bool RemoveReservation(User user, Book book);
        Task<APIResponse<List<BookReservationDto>>> GetBookReservationsAsync(int bookId);
    }

    /// <summary>
    /// The reservation queue: the user's reservations (list, reserve, cancel) and a
    /// book's whole queue for admins. Moved out of UserActionsService and BookService
    /// unchanged (#116).
    /// </summary>
    public class ReservationService : IReservationService
    {
        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IBookService _bookService;
        private readonly IAvailabilityService _availability;
        private readonly IAppDbContext _dbContext;
        private readonly IMapper _mapper;

        public ReservationService(IUserRepo userRepo, IBookRepo bookRepo, IBookService bookService,
            IAvailabilityService availability, IAppDbContext dbContext, IMapper mapper)
        {
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _bookService = bookService;
            _availability = availability;
            _dbContext = dbContext;
            _mapper = mapper;
        }

        public async Task<APIResponse<List<ReservationItemDto>>> GetReservedBooksAsync(string userId)
        {
            var response = new APIResponse<List<ReservationItemDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            // Check if user exists
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user == null)
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            // Query ReservationItems directly and include the Book
            var reservations = await _dbContext.ReservationItems
                .Where(r => r.UserId == userId)
                .Include(r => r.Book)
                .OrderByDescending(r => r.ReservationDate)
                .ToListAsync();

            // Map to DTOs
            var reservationDtos = _mapper.Map<List<ReservationItemDto>>(reservations);

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = reservationDtos;
            return response;
        }
        public async Task<APIResponse<UserDto>> ReserveBookAsync(string userId, int bookId)
        {
            var response = new APIResponse<UserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            var user = await _userRepo.GetByIdAsync<User>(userId);
            var book = await _bookRepo.GetByIdAsync(bookId);

            if (user == null || book == null)
            {
                response.ErrorMessages.Add("User or Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            // A book an admin has marked Lost or Removed can't be reserved
            if (book.AdminStatus != null)
            {
                response.ErrorMessages.Add("This book isn't available.");
                response.StatusCode = HttpStatusCode.Conflict;
                return response;
            }

            if (user != null && book != null)
            {
                if (user.ReservedBooks.Any(r => r.BookId == book.BookId))
                {
                    response.ErrorMessages.Add("Book already reserved by user.");
                    response.StatusCode = HttpStatusCode.Conflict;
                    return response;
                }
                else if (user.CheckedOutBooks.Any(b => b.BookId == book.BookId))
                {
                    response.ErrorMessages.Add("Book already checked out by user.");
                    response.StatusCode = HttpStatusCode.Conflict;
                    return response;
                }
                ;

                AddReservation(user, book);

                await _availability.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.Reserved, $"Book reserved by {user.UserName}"); // Update the book status to Reserved

                await _dbContext.SaveChangesAsync();

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
        private void AddReservation(User user, Book book)
        {
            var reservation = new ReservationItem
            {
                BookId = book.BookId,
                Book = book,
                BookIsAvaliableEmailSent = null,
                AvailabilityDate = null, // Will be set after calculation
                UserId = user.Id,
                User = user,
                ReservationDate = DateTime.UtcNow
            };
            user.ReservedBooks.Add(reservation);
            book.Reservations.Add(reservation);
            _dbContext.ReservationItems.Add(reservation);

            // Now update all availability dates, including the new reservation
            _availability.RecalculateAvailabilityDates(book);
        }

        public bool RemoveReservation(User user, Book book)
        {
            // Find the reservation item for the given book
            var reservationItem = user.ReservedBooks.FirstOrDefault(r => r.BookId == book.BookId);
            if (reservationItem != null)
            {
                // Remove the reservation from both the user and the book
                user.ReservedBooks.Remove(reservationItem);
                book.Reservations.Remove(reservationItem);
                _dbContext.ReservationItems.Remove(reservationItem);

                // Update all availability dates for remaining reservations
                _availability.RecalculateAvailabilityDates(book);

                return true;
            }

            return false;
        }

        public async Task<APIResponse<UserDto>> CancelReservationAsync(string userId, int bookId)
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
                // Use the refactored RemoveReservation method
                if (RemoveReservation(user, book))
                {
                    // Returned means "recalculate": Reserved if others are still queued, otherwise Available
                    await _availability.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.Returned, $"Reservation cancelled by {user.UserName}");

                    await _userRepo.SaveAsync();
                    await _bookRepo.SaveAsync();


                    response.IsSuccess = true;
                    response.StatusCode = HttpStatusCode.OK;
                    response.Result = _mapper.Map<UserDto>(user);
                }
                else
                {
                    response.ErrorMessages.Add("Book not reserved by user.");
                    response.StatusCode = HttpStatusCode.Conflict;
                }
            }
            else
            {
                response.ErrorMessages.Add("User or Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }

            return response;
        }

        // Admins only (BookController). Readers see their own place as MyQueuePosition (#71)
        public async Task<APIResponse<List<BookReservationDto>>> GetBookReservationsAsync(int bookId)
        {
            var response = new APIResponse<List<BookReservationDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book != null)
            {
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = await _bookRepo.GetReservationQueueAsync(bookId);
            }
            else
            {
                response.ErrorMessages.Add($"Book {bookId} was not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }
    }
}
