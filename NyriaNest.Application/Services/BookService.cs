using AutoMapper;
using Microsoft.Extensions.Logging;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;


namespace NyriaNest.Application.Services
{
    public interface IBookService
    {
        Task<APIResponse<Book>> GetBookByIdAsync(int id);
        Task<APIResponse<EditBookDto>> GetBookForEditAsync(int id);
        // Accept optional userId of the actor who adds the book
        Task<APIResponse<int>> AddBookAsync(SlimBookDto book, string? userId = null);
        Task<APIResponse<int>> AddEbookAsync(SlimBookDto bookDto, UploadedFile file, CancellationToken ct = default);
        Task<APIResponse<bool>> DeleteBookAsync(int id);
        Task<APIResponse<bool>> UpdateBookInfoAsync(int id, SlimBookDto bookDto);
        Task<APIResponse<Book>> UpdateBookStatusAsync(Book book, string? userId, BookStatusEnum bookStatus, string? notes);
        Task<APIResponse<List<StatusHistoryItemDto>>> GetBookHistoryAsync(int bookId);
        Task<APIResponse<List<BookReservationDto>>> GetBookReservationsAsync(int bookId);
        void RecalculateAvailabilityDates(Book book);
        Task<APIResponse<List<LibraryBookDto>>> GetLibraryAsync(string? userId);
        Task<APIResponse<PagedResult<AdminBookDto>>> GetAdminCatalogueAsync(AdminCatalogueQuery q);
        Task<APIResponse<bool>> SetAdminStatusAsync(int bookId, BookStatusEnum status, string? adminUserId, string? notes);
        Task<APIResponse<bool>> ClearAdminStatusAsync(int bookId, string? adminUserId);
    }
    public class BookService : IBookService
    {
        // How long a loan lasts. Used for the due date and for spacing out the
        // reservation queue, so the two can't drift apart.
        public const int LoanPeriodMonths = 1;

        private readonly IBookRepo _bookRepo;
        private readonly IMapper _mapper;
        private readonly IUserRepo _userRepo;
        private readonly IAppDbContext _dbContext;
        private readonly IFileStorage _fileStorage;
        private readonly ILogger<BookService> _logger;
        private readonly IEpubService _epubService;

        public BookService(IBookRepo bookRepo, IMapper mapper, IUserRepo userRepo, IAppDbContext dbContext,
            IFileStorage fileStorage, ILogger<BookService> logger, IEpubService epubService)
        {
            _bookRepo = bookRepo;
            _mapper = mapper;
            _userRepo = userRepo;
            _dbContext = dbContext;
            _fileStorage = fileStorage;
            _logger = logger;
            _epubService = epubService;
        }

        public async Task<APIResponse<Book>> GetBookByIdAsync(int id)
        {
            var response = new APIResponse<Book>();

            try
            {
                var book = await _bookRepo.GetByIdAsync(id);
                if (book != null)
                {
                    response.Result = book;
                    response.IsSuccess = true;
                    response.StatusCode = HttpStatusCode.OK;
                }
                else
                {
                    response.IsSuccess = false;
                    response.ErrorMessages.Add("Book not found.");
                    response.StatusCode = HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load book {BookId}.", id);
                response.IsSuccess = false;
                response.ErrorMessages.Add("The book couldn't be loaded.");
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        // For the edit book page (GET api/Book/{id}). GetBookByIdAsync above stays for the
        // services that need the tracked entity (borrow, return, cancel); returning that
        // entity to a client looped for books with reservations (#76).
        public async Task<APIResponse<EditBookDto>> GetBookForEditAsync(int id)
        {
            var response = new APIResponse<EditBookDto>();
            var book = await _bookRepo.GetByIdAsync(id);
            if (book == null)
            {
                response.IsSuccess = false;
                response.ErrorMessages.Add($"Book {id} was not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            response.Result = new EditBookDto
            {
                BookId = book.BookId,
                Title = book.Title,
                Author = book.Author,
                BookDescription = book.BookDescription,
                PublicationYear = book.PublicationYear,
                CoverImagePath = book.CoverImagePath,
                Genre = book.Genre,
                BookType = book.BookType,
                Language = book.Language,
                HasEpub = book.HasEpub,
                EpubFileName = book.EpubFileName,
                EpubFileSize = book.EpubFileSize,
            };
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }

        // Returns the new book's id: the entity itself isn't sent to clients (#76)
        public async Task<APIResponse<int>> AddBookAsync(SlimBookDto bookDto, string? userId = null)
        {
            var response = new APIResponse<int>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            if (string.IsNullOrEmpty(bookDto.Title))
            {
                response.ErrorMessages.Add("Title must not be empty.");
                return response;
            }

            try
            {
                var book = new Book
                {
                    Title = bookDto.Title,
                    Author = bookDto.Author,
                    Genre = bookDto.Genre,
                    Language = bookDto.Language,
                    BookDescription = bookDto.BookDescription,
                    PublicationYear = bookDto.PublicationYear,
                    BookType = bookDto.BookType,
                    CoverImagePath = bookDto.CoverImagePath,
                    // current live status for the book (what clients should see)
                    BookStatus = BookStatusEnum.Available,
                    AvailabilityDate = DateTime.UtcNow
                };

                // persist book so we have a BookId for history items
                await _bookRepo.CreateBookAsync(book);
                await _bookRepo.SaveAsync();

                // lookup user if provided
                User? user = null;
                if (!string.IsNullOrEmpty(userId))
                {
                    try
                    {
                        user = await _userRepo.GetByIdAsync<User>(userId);
                    }
                    catch
                    {
                        user = null;
                    }
                }

                // record lifecycle: Added -> Available. Keep the book.BookStatus as Available (current).
                var now = DateTime.UtcNow;
                var later = now.AddTicks(1);

                if (user != null)
                {
                    // first: Added (actor)
                    var addedItem = new StatusHistoryItem
                    {
                        UserId = user.Id,
                        BookId = book.BookId,
                        BookStatus = BookStatusEnum.Added,
                        Timestamp = now,
                        Notes = "Added"
                    };
                    // second: Available (current)
                    var availableItem = new StatusHistoryItem
                    {
                        UserId = user.Id,
                        BookId = book.BookId,
                        BookStatus = BookStatusEnum.Available,
                        Timestamp = later,
                        Notes = "Set to Available"
                    };

                    _dbContext.StatusHistoryItems.AddRange(addedItem, availableItem);
                    user.UserHistory.Add(addedItem);
                    user.UserHistory.Add(availableItem);
                    book.StatusHistory.Add(addedItem);
                    book.StatusHistory.Add(availableItem);

                    // persist history
                    await _bookRepo.SaveAsync();
                    await _userRepo.UpdateAsync(user);
                }
                else
                {
                    // no user -> history items with null UserId
                    var addedItem = new StatusHistoryItem
                    {
                        UserId = null,
                        BookId = book.BookId,
                        Book = book,
                        BookStatus = BookStatusEnum.Added,
                        Timestamp = now,
                        Notes = "Added"
                    };
                    var availableItem = new StatusHistoryItem
                    {
                        UserId = null,
                        BookId = book.BookId,
                        Book = book,
                        BookStatus = BookStatusEnum.Available,
                        Timestamp = later,
                        Notes = "Set to Available"
                    };

                    _dbContext.StatusHistoryItems.AddRange(addedItem, availableItem);
                    book.StatusHistory.Add(addedItem);
                    book.StatusHistory.Add(availableItem);

                    await _bookRepo.SaveAsync();
                }

                response.Result = book.BookId;
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.Created;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not add book {Title}.", bookDto.Title);
                response.ErrorMessages.Add("The book couldn't be added.");
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
        public async Task<APIResponse<int>> AddEbookAsync(SlimBookDto bookDto, UploadedFile file, CancellationToken ct = default)
        {
            var response = new APIResponse<int>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            // Validate the file before anything is written, so an invalid file never creates a book
            var fileError = await _epubService.ValidateAsync(file, ct);
            if (fileError is not null)
            {
                response.ErrorMessages.Add(fileError);
                return response;
            }

            // This endpoint only creates e-books, whatever the client sent
            bookDto.BookType = BookTypeEnums.EBook;

            // Disposing the transaction without a commit rolls back the book and its history
            await using var transaction = await _dbContext.BeginTransactionAsync(ct);

            var created = await AddBookAsync(bookDto);
            if (!created.IsSuccess)
                return created;

            var upload = await _epubService.UploadAsync(created.Result, file, ct);
            if (!upload.IsSuccess)
            {
                response.StatusCode = upload.StatusCode;
                response.ErrorMessages.AddRange(upload.ErrorMessages);
                return response;
            }

            try
            {
                await transaction.CommitAsync(ct);
            }
            catch
            {
                // The book is rolled back, so don't leave its file behind
                await _fileStorage.DeleteAsync(upload.Result!, CancellationToken.None);
                throw;
            }

            // created holds the new book's id, and the book now has its EPUB
            return created;
        }
        public async Task<APIResponse<bool>> DeleteBookAsync(int id)
        {
            var response = new APIResponse<bool>();

            try
            {
                var bookToDelete = await _bookRepo.GetByIdAsync(id);

                // Deleting a book someone has borrowed would wipe out their loan
                // (and the loan's FK is Restrict, so the delete would fail anyway)
                if (bookToDelete?.CheckedOutBy != null)
                {
                    response.IsSuccess = false;
                    response.ErrorMessages.Add("Return this book before deleting it.");
                    response.StatusCode = HttpStatusCode.Conflict;
                    return response;
                }

                if (bookToDelete != null)
                {
                    // Reservations go with the book (decided in #39). Their FK is Restrict,
                    // so they have to be removed explicitly, in the same save as the book.
                    // Telling the readers is #75.
                    _dbContext.ReservationItems.RemoveRange(bookToDelete.Reservations);
                    await _bookRepo.DeleteAsync(bookToDelete);
                    await _bookRepo.SaveAsync();

                    // Delete the EPUB only after the book is gone from the database,
                    // so a failed DB delete never leaves a book pointing at a missing file
                    if (bookToDelete.EpubFileKey is not null)
                    {
                        try
                        {
                            await _fileStorage.DeleteAsync(bookToDelete.EpubFileKey);
                        }
                        catch (Exception ex)
                        {
                            // The book is already deleted; a leftover file is harmless, so log instead of failing
                            _logger.LogWarning(ex, "Could not delete EPUB {Key} for deleted book {BookId}.", bookToDelete.EpubFileKey, id);
                        }
                    }

                    // No entity in the response: it's deleted, and its navigations would loop
                    response.Result = true;
                    response.IsSuccess = true;
                    response.StatusCode = HttpStatusCode.OK;
                }
                else
                {
                    response.IsSuccess = false;
                    response.ErrorMessages.Add("Book not found.");
                    response.StatusCode = HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                // Log the real error; the client only gets a plain message, never
                // EF's internal text about the data model
                _logger.LogError(ex, "Could not delete book {BookId}.", id);
                response.ErrorMessages.Add("The book couldn't be deleted.");
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
        public async Task<APIResponse<bool>> UpdateBookInfoAsync(int id, SlimBookDto updatedBook)
        {
            var response = new APIResponse<bool>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            try
            {
                var existingBook = await _bookRepo.GetByIdAsync(id);
                if (existingBook != null)
                {
                    // The edit page always sends every field, so assign them as they are.
                    // That way a description or cover can be cleared.
                    existingBook.Title = updatedBook.Title.Trim();
                    existingBook.Author = updatedBook.Author.Trim();
                    existingBook.Genre = updatedBook.Genre;
                    existingBook.Language = updatedBook.Language;
                    existingBook.PublicationYear = updatedBook.PublicationYear;
                    existingBook.BookDescription = string.IsNullOrWhiteSpace(updatedBook.BookDescription) ? null : updatedBook.BookDescription.Trim();
                    existingBook.BookType = updatedBook.BookType;
                    existingBook.CoverImagePath = string.IsNullOrWhiteSpace(updatedBook.CoverImagePath) ? null : updatedBook.CoverImagePath.Trim();

                    // Only e-books can have an EPUB, so changing the type drops the file.
                    // The edit page warns the admin before saving.
                    string? droppedEpubKey = null;
                    if (existingBook.BookType != BookTypeEnums.EBook && existingBook.EpubFileKey is not null)
                    {
                        droppedEpubKey = existingBook.EpubFileKey;
                        existingBook.EpubFileKey = null;
                        existingBook.EpubFileName = null;
                        existingBook.EpubFileSize = null;
                    }

                    await _bookRepo.SaveAsync();

                    // As in DeleteBookAsync: remove the file only once the database no longer points at it
                    if (droppedEpubKey is not null)
                    {
                        try
                        {
                            await _fileStorage.DeleteAsync(droppedEpubKey);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not delete EPUB {Key} after book {BookId} stopped being an e-book.", droppedEpubKey, id);
                        }
                    }

                    // No entity in the response: with reservations its navigations loop.
                    // The edit page only needs to know that it was saved.
                    response.Result = true;
                    response.IsSuccess = true;
                    response.StatusCode = HttpStatusCode.OK;
                }
                else
                {
                    response.ErrorMessages.Add("Book not found.");
                    response.StatusCode = HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                // As in DeleteBookAsync: log the real error, give the client a plain message
                _logger.LogError(ex, "Could not update book {BookId}.", id);
                response.ErrorMessages.Add("The book couldn't be saved.");
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
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
        public async Task<APIResponse<List<StatusHistoryItemDto>>> GetBookHistoryAsync(int bookId)
        {
            var response = new APIResponse<List<StatusHistoryItemDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book != null)
            {
                var history = await _bookRepo.GetStatusHistoryAsync(bookId);
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = _mapper.Map<List<StatusHistoryItemDto>>(history);
            }
            else
            {
                response.ErrorMessages.Add($"Book {bookId} was not found.");
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
        public async Task<APIResponse<List<LibraryBookDto>>> GetLibraryAsync(string? userId)
        {
            var response = new APIResponse<List<LibraryBookDto>>();
            try
            {
                response.Result = await _bookRepo.GetLibraryListAsync(userId);
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages.Add(ex.Message);
            }
            return response;
        }

        public async Task<APIResponse<PagedResult<AdminBookDto>>> GetAdminCatalogueAsync(AdminCatalogueQuery q)
        {
            var response = new APIResponse<PagedResult<AdminBookDto>>();

            // Returned and Added are momentary events, not states a book stays in,
            // so filtering by them would silently match nothing useful
            if (q.Status is BookStatusEnum.Returned or BookStatusEnum.Added)
            {
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorMessages.Add($"Can't filter by {q.Status}.");
                return response;
            }

            try
            {
                response.Result = await _bookRepo.GetAdminCatalogueAsync(q);
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                // Log the details, but don't send exception text to the client
                _logger.LogError(ex, "Failed to load admin catalogue");
                response.IsSuccess = false;
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorMessages.Add("Could not load the catalogue.");
            }
            return response;
        }

        public async Task<APIResponse<bool>> SetAdminStatusAsync(int bookId, BookStatusEnum status, string? adminUserId, string? notes)
        {
            var response = new APIResponse<bool> { IsSuccess = false, StatusCode = HttpStatusCode.BadRequest };

            if (status is not (BookStatusEnum.Lost or BookStatusEnum.Removed))
            {
                response.ErrorMessages.Add("Admins can only set Lost or Removed.");
                return response;
            }

            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book == null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                response.ErrorMessages.Add("Book not found.");
                return response;
            }
            if (book.CheckedOutBy != null)
            {
                response.ErrorMessages.Add("This book is checked out. Return it first.");
                return response;
            }

            book.AdminStatus = status;
            book.AdminStatusSetAt = DateTime.UtcNow;
            book.AdminStatusNotes = notes;
            GetBookStatus(book, status);

            await AddHistoryIfAdminAsync(adminUserId, book, status, notes);
            await _bookRepo.UpdateAsync(book);
            await _bookRepo.SaveAsync();

            response.Result = true; // not the entity: it loops for books with reservations (#76)
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }

        public async Task<APIResponse<bool>> ClearAdminStatusAsync(int bookId, string? adminUserId)
        {
            var response = new APIResponse<bool> { IsSuccess = false, StatusCode = HttpStatusCode.NotFound };
            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book == null) { response.ErrorMessages.Add("Book not found."); return response; }

            book.AdminStatus = null;
            book.AdminStatusSetAt = null;
            book.AdminStatusNotes = null;
            // "Returned" makes GetBookStatus recompute: Reserved if there are reservations, else Available
            GetBookStatus(book, BookStatusEnum.Returned);

            await AddHistoryIfAdminAsync(adminUserId, book, book.BookStatus, "Restored by admin");
            await _bookRepo.UpdateAsync(book);
            await _bookRepo.SaveAsync();

            response.Result = true; // not the entity: it loops for books with reservations (#76)
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }

        private async Task AddHistoryIfAdminAsync(string? adminUserId, Book book, BookStatusEnum status, string? notes)
        {
            if (string.IsNullOrEmpty(adminUserId)) return;
            var admin = await _userRepo.GetByIdAsync<User>(adminUserId);
            if (admin != null) AddStatusHistoryItem(admin, book, status, notes);
        }

    }
}
