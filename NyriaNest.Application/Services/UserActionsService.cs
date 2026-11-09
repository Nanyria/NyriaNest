using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;

namespace NyriaNest.Application.Services
{
    public interface IUserActionsService
    {
        // Register methods
        Task<APIResponse<CreateUserDto>> AddUserAsync(CreateUserDto createUserDTO);
        Task<APIResponse<UpdateUserDto>> UpdateUserAsync(string userId, UpdateUserDto userToUpdate);
        // Reservation methods
        Task<APIResponse<List<ReservationItemDto>>> GetReservedBooksAsync(string userId);
        Task<APIResponse<UserDto>> ReserveBookAsync(string userId, int bookId);
        Task<APIResponse<UserDto>> CancelReservationAsync(string userId, int bookId);

        // Borrow methods
        Task<APIResponse<List<CheckedOutItemDto>>> GetCheckedOutBooksAsync(string userId);
        Task<APIResponse<UserDto>> CheckOutBookAsync(string userId, int bookId);
        Task<APIResponse<UserDto>> ReturnBookAsync(string userId, int bookId);
    }

    public class UserActionsService : IUserActionsService
    {
        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IMapper _mapper;
        private readonly IBookService _bookService;
        private readonly IAppDbContext _dbContext;
        private readonly UserManager<User> _userManager;
        private readonly IEmailService _emailService;
        private readonly IProfilePictureService _profilePictures;

        public UserActionsService(IUserRepo userRepo, IBookRepo bookRepo, IMapper mapper, IBookService bookService, IAppDbContext dbContext, UserManager<User> userManager, IEmailService emailService, IProfilePictureService profilePictures)
        {
            _profilePictures = profilePictures;
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _mapper = mapper;
            _bookService = bookService;
            _dbContext = dbContext;
            _userManager = userManager;
            _emailService = emailService;
        }
        //Register methods
        public async Task<APIResponse<CreateUserDto>> AddUserAsync(CreateUserDto createUserDTO)
        {
            var response = new APIResponse<CreateUserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            CheckIfExists(createUserDTO.Email, createUserDTO.UserName);

            var user = _mapper.Map<User>(createUserDTO);
            user.Role = Roles.User; // always, whatever the request contains
            // Create user with password
            var result = await _userManager.CreateAsync(user, createUserDTO.Password);
            if (!result.Succeeded)
            {
                response.ErrorMessages.AddRange(result.Errors.Select(e => e.Description));
                return response;
            }
            await _userManager.AddToRoleAsync(user, Roles.User);
            await _emailService.SendRegistrationEmailAsync(
                createUserDTO.Email,
                createUserDTO.FirstName,
                createUserDTO.UserName
            );

            var createdUserDto = _mapper.Map<CreateUserDto>(user);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.Created;
            response.Result = createdUserDto;
            return response;
        }

        public async Task<APIResponse<UpdateUserDto>> UpdateUserAsync(string userId, UpdateUserDto userToUpdate)
        {
            var response = new APIResponse<UpdateUserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user != null)
            {
                // Names are optional in the request (null = unchanged), but can't be cleared
                if ((userToUpdate.FirstName != null && string.IsNullOrWhiteSpace(userToUpdate.FirstName))
                    || (userToUpdate.LastName != null && string.IsNullOrWhiteSpace(userToUpdate.LastName)))
                {
                    response.ErrorMessages.Add("First name and last name can't be empty.");
                    return response;
                }

                if (userToUpdate.Bio != null && userToUpdate.Bio.Length > UserLimits.BioMaxLength)
                {
                    response.ErrorMessages.Add($"The bio can be at most {UserLimits.BioMaxLength} characters.");
                    return response;
                }

                // A preset or the user's own upload, never any URL: other readers' browsers load it
                if (userToUpdate.ProfilePictureUrl != null && !_profilePictures.IsAllowed(userToUpdate.ProfilePictureUrl, user))
                {
                    response.ErrorMessages.Add("Choose one of the preset pictures or upload your own.");
                    return response;
                }
                var oldPicture = user.ProfilePictureUrl;

                // Changing the password or the email needs the current password.
                // Resending the email you already have (the profile page does) doesn't count.
                var changesPassword = !string.IsNullOrWhiteSpace(userToUpdate.Password);
                var changesEmail = !string.IsNullOrWhiteSpace(userToUpdate.Email)
                    && !string.Equals(userToUpdate.Email, user.Email, StringComparison.OrdinalIgnoreCase);
                if ((changesPassword || changesEmail)
                    && (string.IsNullOrEmpty(userToUpdate.CurrentPassword)
                        || !await _userManager.CheckPasswordAsync(user, userToUpdate.CurrentPassword)))
                {
                    response.ErrorMessages.Add("The current password you entered is incorrect. Please try again.");
                    return response;
                }

                // Map UpdateUserDto to User
                _mapper.Map(userToUpdate, user);

                if (changesPassword)
                {
                    var passwordResult = await _userManager.ChangePasswordAsync(
                        user, userToUpdate.CurrentPassword!, userToUpdate.Password!);
                    if (!passwordResult.Succeeded)
                    {
                        response.ErrorMessages.AddRange(passwordResult.Errors.Select(e => e.Description));
                        return response;
                    }
                }

                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    response.ErrorMessages.AddRange(updateResult.Errors.Select(e => e.Description));
                    return response;
                }

                // A new email ends the user's other sessions, like a new password (#113).
                // The password change already renewed the security stamp; the email is set
                // directly above, so its stamp is renewed here.
                if (changesEmail)
                    await _userManager.UpdateSecurityStampAsync(user);

                // Switched away from an uploaded picture: its file isn't needed any more
                if (user.ProfilePictureUrl != oldPicture)
                    await _profilePictures.DeleteUploadAsync(oldPicture);

                var updatedUserDto = _mapper.Map<UpdateUserDto>(user);
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = updatedUserDto;
            }
            else
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }
        private void CheckIfExists(string email, string userName)
        {
            var user = _userRepo.GetByEmailAsync<User>(email).Result;
            if (user != null)
            {
                throw new Exception("Email already exists.");
            }
            var userNameExists = _userRepo.GetByUserNameAsync<User>(userName).Result;
            if (userNameExists != null)
            {
                throw new Exception("UserName already exists.");
            }
        }
        // Reservation methods
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

                await _bookService.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.Reserved, $"Book reserved by {user.UserName}"); // Update the book status to Reserved

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
            _bookService.RecalculateAvailabilityDates(book);
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
                _bookService.RecalculateAvailabilityDates(book);

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
                    await _bookService.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.Returned, $"Reservation cancelled by {user.UserName}");

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

        // Borrow methods
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

            RemoveReservation(user, book); // Does nothing if they had no reservation
            SetCheckedOutBook(user, book);

            // The rest of the queue now waits for this new loan's due date
            _bookService.RecalculateAvailabilityDates(book);

            try
            {
                // UpdateBookStatusAsync saves the changes
                await _bookService.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.CheckedOut, $"Checked out by {user.UserName}");
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
                ReturnDate = DateTime.UtcNow.AddMonths(BookService.LoanPeriodMonths), // the due date
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
                _bookService.RecalculateAvailabilityDates(book);

                await _bookService.UpdateBookStatusAsync(book, user.Id, BookStatusEnum.Returned, $"Returned by {user.UserName}");

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

