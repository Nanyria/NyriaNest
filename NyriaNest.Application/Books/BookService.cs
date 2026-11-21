using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;


namespace NyriaNest.Application.Books
{
    public interface IBookService
    {
        Task<APIResponse<Book>> GetBookByIdAsync(int id);
        Task<APIResponse<EditBookDto>> GetBookForEditAsync(int id);
        // Accept optional userId of the actor who adds the book
        Task<APIResponse<int>> AddBookAsync(SlimBookDto book, string? userId = null);
        Task<APIResponse<int>> AddEbookAsync(SlimBookDto bookDto, UploadedFile file, CancellationToken ct = default);
        // actorId: who is doing it. When that isn't the book's owner, a reason is required (#83).
        Task<APIResponse<bool>> DeleteBookAsync(int id, string actorId, string? reason = null, bool confirmLent = false);
        Task<APIResponse<bool>> UpdateBookInfoAsync(int id, SlimBookDto bookDto, string actorId, string? reason = null);
        Task<APIResponse<List<StatusHistoryItemDto>>> GetBookHistoryAsync(int bookId);
        Task<APIResponse<List<LibraryBookDto>>> GetLibraryAsync(string? userId);
        Task<APIResponse<PagedResult<AdminBookDto>>> GetAdminCatalogueAsync(AdminCatalogueQuery q);
        Task<APIResponse<bool>> SetAdminStatusAsync(int bookId, BookStatusEnum status, string? adminUserId, string? notes);
        Task<APIResponse<bool>> ClearAdminStatusAsync(int bookId, string? adminUserId, string? notes = null);

        // A user's own books (#83). Someone else's book or a shared-library book looks missing (404).
        Task<APIResponse<List<MyBookDto>>> GetOwnBooksAsync(string ownerId);
        Task<APIResponse<MyBookDto>> GetOwnBookAsync(int id, string ownerId);
        Task<APIResponse<int>> AddOwnBookAsync(SlimBookDto bookDto, string ownerId);
        Task<APIResponse<bool>> UpdateOwnBookAsync(int id, string ownerId, SlimBookDto bookDto);
        Task<APIResponse<bool>> DeleteOwnBookAsync(int id, string ownerId, bool confirmLent);
        Task<APIResponse<List<StatusHistoryItemDto>>> GetOwnBookHistoryAsync(int id, string ownerId);
    }
    public class BookService : IBookService
    {
        private readonly IBookRepo _bookRepo;
        private readonly IMapper _mapper;
        private readonly IUserRepo _userRepo;
        private readonly IAppDbContext _dbContext;
        private readonly IFileStorage _fileStorage;
        private readonly ILogger<BookService> _logger;
        private readonly IEpubService _epubService;
        private readonly IAvailabilityService _availability;
        private readonly INotificationService _notifications;

        public BookService(IBookRepo bookRepo, IMapper mapper, IUserRepo userRepo, IAppDbContext dbContext,
            IFileStorage fileStorage, ILogger<BookService> logger, IEpubService epubService, IAvailabilityService availability,
            INotificationService notifications)
        {
            _bookRepo = bookRepo;
            _mapper = mapper;
            _userRepo = userRepo;
            _dbContext = dbContext;
            _fileStorage = fileStorage;
            _logger = logger;
            _epubService = epubService;
            _availability = availability;
            _notifications = notifications;
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

            response.Result = ToEditDto(book);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }

        private static EditBookDto ToEditDto(Book book) =>
            WithEditFields(new EditBookDto { Title = book.Title, Author = book.Author }, book);

        // My library also shows where the book is: on the shelf, lent out, or taken down by an admin
        private static MyBookDto ToMyBookDto(Book book) => WithEditFields(new MyBookDto
        {
            Title = book.Title,
            Author = book.Author,
            BookStatus = book.BookStatus,
            AdminStatus = book.AdminStatus,
            LentTo = book.CheckedOutBy == null ? null : book.CheckedOutBy.BorrowerName ?? book.CheckedOutBy.User?.UserName,
            LentOn = book.CheckedOutBy?.CheckOutDate,
            DueDate = book.CheckedOutBy?.ReturnDate,
        }, book);

        // Title and Author are required members, so the caller sets them in its initializer
        private static T WithEditFields<T>(T dto, Book book) where T : EditBookDto
        {
            dto.BookId = book.BookId;
            dto.BookDescription = book.BookDescription;
            dto.PublicationYear = book.PublicationYear;
            dto.CoverImagePath = book.CoverImagePath;
            dto.Genre = book.Genre;
            dto.BookType = book.BookType;
            dto.Language = book.Language;
            dto.HasEpub = book.HasEpub;
            dto.EpubFileName = book.EpubFileName;
            dto.EpubFileSize = book.EpubFileSize;
            dto.OwnerId = book.OwnerId;
            dto.OwnerName = book.Owner?.UserName;
            return dto;
        }

        // Returns the new book's id: the entity itself isn't sent to clients (#76)
        public Task<APIResponse<int>> AddBookAsync(SlimBookDto bookDto, string? userId = null) =>
            CreateBookAsync(bookDto, userId, ownerId: null);

        // ownerId null = a shared-library book
        private async Task<APIResponse<int>> CreateBookAsync(SlimBookDto bookDto, string? userId, string? ownerId)
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

            if (TypeProblem(ownerId, bookDto.BookType) is { } typeProblem)
            {
                response.ErrorMessages.Add(typeProblem);
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
                    OwnerId = ownerId,
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
        public async Task<APIResponse<bool>> DeleteBookAsync(int id, string actorId, string? reason = null, bool confirmLent = false)
        {
            var bookToDelete = await _bookRepo.GetByIdAsync(id);
            if (bookToDelete == null)
                return Failure<bool>(HttpStatusCode.NotFound, "Book not found.");

            return await DeleteAsActorAsync(bookToDelete, actorId, reason, confirmLent);
        }

        // The rule goes by actor, not role (#83): an admin who owns the book is just its owner
        private static bool IsSomeoneElses(Book book, string actorId) =>
            book.OwnerId != null && book.OwnerId != actorId;

        private const string ReasonMessage = "Give a reason: this book belongs to someone else.";

        /// <summary>The checks every delete goes through, whoever asks, then the delete itself.</summary>
        private async Task<APIResponse<bool>> DeleteAsActorAsync(Book book, string actorId, string? reason, bool confirmLent)
        {
            var someoneElses = IsSomeoneElses(book, actorId);
            if (someoneElses && string.IsNullOrWhiteSpace(reason))
                return Failure<bool>(HttpStatusCode.BadRequest, ReasonMessage);

            var loan = book.CheckedOutBy;
            if (loan != null)
            {
                // Lent to a member: deleting would wipe out their loan (and the loan's FK is
                // Restrict, so the delete would fail anyway). Friend loans decide more in #85.
                if (loan.BorrowerName == null)
                    return Failure<bool>(HttpStatusCode.Conflict, "Return this book before deleting it.");

                // Lent to a name: it may be deleted anyway (it came back unmarked,
                // or it's lost or given away), so the client asks and sends confirmLent
                if (!confirmLent)
                    return Failure<bool>(HttpStatusCode.Conflict,
                        $"This book is marked as lent to {loan.BorrowerName}. Delete it anyway? The loan is removed too.");

                _dbContext.CheckOutItems.Remove(loan);
            }

            var removedReason = book.OwnerId == null ? BookRemovedReason.RemovedFromSharedLibrary
                : someoneElses ? BookRemovedReason.DeletedByAdminFromOwner
                : BookRemovedReason.DeletedByOwner;

            // No history row: the history goes with the book. The owner's notification is the record.
            return await RemoveBookAsync(book, removedReason, someoneElses ? reason!.Trim() : null);
        }

        /// <summary>
        /// Deletes a book that has passed its caller's checks, telling whoever the reason says.
        /// A loan still on the book has to be removed by the caller first, in the same save.
        /// </summary>
        private async Task<APIResponse<bool>> RemoveBookAsync(Book bookToDelete, BookRemovedReason reason, string? details)
        {
            var response = new APIResponse<bool>();

            try
            {
                // Reservations go with the book (decided in #39). Their FK is Restrict,
                // so they have to be removed explicitly, in the same save as the book.
                // The readers are told in the same save (#75).
                await _notifications.NotifyBookRemovedAsync(bookToDelete, reason, details);
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
                        _logger.LogWarning(ex, "Could not delete EPUB {Key} for deleted book {BookId}.", bookToDelete.EpubFileKey, bookToDelete.BookId);
                    }
                }

                // No entity in the response: it's deleted, and its navigations would loop
                response.Result = true;
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                // Log the real error; the client only gets a plain message, never
                // EF's internal text about the data model
                _logger.LogError(ex, "Could not delete book {BookId}.", bookToDelete.BookId);
                response.ErrorMessages.Add("The book couldn't be deleted.");
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        private static APIResponse<T> Failure<T>(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = { message },
        };

        // ---- A user's own books (#83) ----

        // Own books are physical for now (ADR 0004). E-books come later: add EBook here then.
        private static bool OwnersCanHave(BookTypeEnums type) =>
            type is BookTypeEnums.Paperback or BookTypeEnums.Hardcover;

        private const string OwnTypeMessage = "Your own books can be paperbacks or hardcovers.";
        private const string SharedLibraryTypeMessage = "The shared library has e-books only.";

        // Which types a book may have depends on the book, not on who adds or edits it:
        // the shared library has e-books only (ADR 0004), an owned book what owners can have.
        // Null when the type is fine.
        private static string? TypeProblem(string? ownerId, BookTypeEnums type) => ownerId == null
            ? (type == BookTypeEnums.EBook ? null : SharedLibraryTypeMessage)
            : (OwnersCanHave(type) ? null : OwnTypeMessage);

        // Someone else's book and a shared-library book both look missing
        private async Task<Book?> GetOwnAsync(int id, string ownerId)
        {
            var book = await _bookRepo.GetByIdAsync(id);
            return book?.OwnerId == ownerId ? book : null;
        }

        public async Task<APIResponse<List<MyBookDto>>> GetOwnBooksAsync(string ownerId)
        {
            var books = await _bookRepo.GetOwnedByAsync(ownerId);
            return new APIResponse<List<MyBookDto>>
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = books.Select(ToMyBookDto).ToList(),
            };
        }

        // The owner sees the whole history, admins' changes included (that's what their reason is for)
        public async Task<APIResponse<List<StatusHistoryItemDto>>> GetOwnBookHistoryAsync(int id, string ownerId)
        {
            if (await GetOwnAsync(id, ownerId) == null)
                return Failure<List<StatusHistoryItemDto>>(HttpStatusCode.NotFound, "Book not found.");

            return await GetBookHistoryAsync(id);
        }

        public async Task<APIResponse<MyBookDto>> GetOwnBookAsync(int id, string ownerId)
        {
            var book = await GetOwnAsync(id, ownerId);
            if (book == null)
                return Failure<MyBookDto>(HttpStatusCode.NotFound, "Book not found.");

            return new APIResponse<MyBookDto>
            {
                IsSuccess = true,
                StatusCode = HttpStatusCode.OK,
                Result = ToMyBookDto(book),
            };
        }

        // The type is checked in CreateBookAsync / UpdateBookInfoAsync (TypeProblem)
        public Task<APIResponse<int>> AddOwnBookAsync(SlimBookDto bookDto, string ownerId) =>
            // The owner is also the one who added it, in the history
            CreateBookAsync(bookDto, userId: ownerId, ownerId: ownerId);

        public async Task<APIResponse<bool>> UpdateOwnBookAsync(int id, string ownerId, SlimBookDto bookDto)
        {
            if (await GetOwnAsync(id, ownerId) == null)
                return Failure<bool>(HttpStatusCode.NotFound, "Book not found.");

            return await UpdateBookInfoAsync(id, bookDto, actorId: ownerId);
        }

        public async Task<APIResponse<bool>> DeleteOwnBookAsync(int id, string ownerId, bool confirmLent)
        {
            var book = await GetOwnAsync(id, ownerId);
            if (book == null)
                return Failure<bool>(HttpStatusCode.NotFound, "Book not found.");

            return await DeleteAsActorAsync(book, ownerId, reason: null, confirmLent);
        }
        public async Task<APIResponse<bool>> UpdateBookInfoAsync(int id, SlimBookDto updatedBook, string actorId, string? reason = null)
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
                    if (TypeProblem(existingBook.OwnerId, updatedBook.BookType) is { } typeProblem)
                    {
                        response.ErrorMessages.Add(typeProblem);
                        return response;
                    }

                    var someoneElses = IsSomeoneElses(existingBook, actorId);
                    if (someoneElses && string.IsNullOrWhiteSpace(reason))
                    {
                        response.ErrorMessages.Add(ReasonMessage);
                        return response;
                    }

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

                    // Someone else changing a book is recorded; the owner's own edits aren't (#83)
                    if (someoneElses)
                    {
                        _dbContext.StatusHistoryItems.Add(new StatusHistoryItem
                        {
                            UserId = actorId,
                            BookId = existingBook.BookId,
                            BookStatus = existingBook.BookStatus,
                            Timestamp = DateTime.UtcNow,
                            Notes = $"Edited by an admin: {reason!.Trim()}",
                        });
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
            // The shared library is e-books only (ADR 0004), and an e-book can't go missing
            if (status == BookStatusEnum.Lost && book.OwnerId == null)
            {
                response.ErrorMessages.Add("E-books can't be lost.");
                return response;
            }
            // Someone else's book: the notes are the reason, and the owner is told (#83)
            var someoneElses = IsSomeoneElses(book, adminUserId ?? "");
            if (someoneElses && string.IsNullOrWhiteSpace(notes))
            {
                response.ErrorMessages.Add(ReasonMessage);
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
            _availability.GetBookStatus(book, status);

            await AddHistoryIfAdminAsync(adminUserId, book, status, notes);

            // Lost or Removed counts as removed for readers (#83): they're told what a delete would
            // tell them, then their reservations and favorites go, all in the same save.
            // A restore doesn't bring them back.
            await _notifications.NotifyBookTakenDownAsync(book, status, someoneElses ? notes!.Trim() : null);
            _dbContext.ReservationItems.RemoveRange(book.Reservations);
            _dbContext.FavoriteItems.RemoveRange(
                await _dbContext.FavoriteItems.Where(f => f.BookId == book.BookId).ToListAsync());

            await _bookRepo.UpdateAsync(book);
            await _bookRepo.SaveAsync();

            response.Result = true; // not the entity: it loops for books with reservations (#76)
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }

        public async Task<APIResponse<bool>> ClearAdminStatusAsync(int bookId, string? adminUserId, string? notes = null)
        {
            var response = new APIResponse<bool> { IsSuccess = false, StatusCode = HttpStatusCode.NotFound };
            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book == null) { response.ErrorMessages.Add("Book not found."); return response; }

            // Someone else's book: the notes are the reason, and the owner is told (#83)
            var someoneElses = IsSomeoneElses(book, adminUserId ?? "");
            if (someoneElses && string.IsNullOrWhiteSpace(notes))
                return Failure<bool>(HttpStatusCode.BadRequest, ReasonMessage);
            var reason = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            book.AdminStatus = null;
            book.AdminStatusSetAt = null;
            book.AdminStatusNotes = null;
            // "Returned" makes GetBookStatus recompute: Reserved if there are reservations, else Available
            _availability.GetBookStatus(book, BookStatusEnum.Returned);

            await AddHistoryIfAdminAsync(adminUserId, book, book.BookStatus,
                reason is null ? "Restored by admin" : $"Restored by admin: {reason}");
            if (someoneElses)
                _notifications.NotifyOwnerOfAdminRestore(book, reason!);
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
            if (admin != null) _availability.AddStatusHistoryItem(admin, book, status, notes);
        }

    }
}
