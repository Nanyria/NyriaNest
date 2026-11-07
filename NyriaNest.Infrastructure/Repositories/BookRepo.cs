using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using NyriaNest.Infrastructure.Data;

namespace NyriaNest.Infrastructure.Repositories
{
    public class BookRepo : IBookRepo
    {

        private readonly AppDbContext _db;
        public BookRepo(AppDbContext db)
        {
            _db = db;
        }

        public async Task CreateBookAsync(Book book)
        {
            await _db.Books.AddAsync(book);
        }

        // Only marks the book for deletion; SaveAsync writes it to the database
        public Task DeleteAsync(Book book)
        {
            _db.Books.Remove(book);
            return Task.CompletedTask;
        }


        public async Task<IEnumerable<Book>> GetAllAsync()
        {
            return await _db.Books
                .Include(b => b.CheckedOutBy)
                .Include(b => b.Reviews)// to access current status
                .ToListAsync();
        }

        public async Task<Book?> GetByIdAsync(int id)
        {
            return await _db.Books
                .Include(b => b.Reviews)
                    .ThenInclude(r => r.User)
                .Include(b => b.Reviews)
                    .ThenInclude(r => r.RatingItem)
                .Include(b => b.CheckedOutBy)
                .Include(b => b.Reservations)
                .FirstOrDefaultAsync(b => b.BookId == id);
        }

        // Queried directly: GetByIdAsync doesn't load StatusHistory (#32), and the
        // history can grow long, so it shouldn't come along with every book load
        public async Task<List<StatusHistoryItem>> GetStatusHistoryAsync(int bookId)
        {
            return await _db.StatusHistoryItems
                .AsNoTracking()
                .Include(h => h.Book)
                .Where(h => h.BookId == bookId)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync();
        }

        public async Task<List<BookReservationDto>> GetReservationQueueAsync(int bookId)
        {
            var queue = await _db.ReservationItems
                .AsNoTracking()
                .Where(r => r.BookId == bookId)
                .OrderBy(r => r.ReservationDate)
                .Select(r => new BookReservationDto
                {
                    ReservationId = r.Id,
                    UserId = r.UserId,
                    UserName = r.User.UserName ?? "",
                    FullName = r.User.FirstName + " " + r.User.LastName,
                    ReservationDate = r.ReservationDate,
                    AvailabilityDate = r.AvailabilityDate,
                })
                .ToListAsync();

            for (var i = 0; i < queue.Count; i++)
                queue[i].QueuePosition = i + 1;
            return queue;
        }

        public async Task<List<LibraryBookDto>> GetLibraryListAsync(string? userId)
        {
            return await _db.Books
                .AsNoTracking()
                // Books an admin has marked Lost or Removed aren't part of the public catalogue
                .Where(b => b.AdminStatus == null)
                .OrderBy(b => b.Title)
                .Select(b => new LibraryBookDto
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Author = b.Author,
                    Genre = b.Genre,
                    Language = b.Language,
                    PublicationYear = b.PublicationYear,
                    BookDescription = b.BookDescription,
                    CoverImagePath = b.CoverImagePath,
                    HasEpub = b.EpubFileKey != null,
                    BookStatus = b.BookStatus,
                    BookType = b.BookType,
                    AvailabilityDate = b.AvailabilityDate,
                    DateAdded = b.DateAdded,

                    ReviewCount = b.Reviews.Count,
                    AverageRating = b.Reviews
                        .Where(r => r.RatingItem != null)
                        .Average(r => (double?)r.RatingItem!.Rating),
                    ReservationCount = b.Reservations.Count,

                    IsCheckedOutByMe = userId != null && b.CheckedOutBy != null && b.CheckedOutBy.UserId == userId,
                    IsReservedByMe = userId != null && b.Reservations.Any(r => r.UserId == userId),
                    MyReservationAvailableDate = b.Reservations
                        .Where(r => r.UserId == userId)
                        .Select(r => r.AvailabilityDate)
                        .FirstOrDefault(),
                    // The queue is oldest reservation first: my place is the number
                    // of reservations made before mine, plus one
                    MyQueuePosition = b.Reservations
                        .Where(r => r.UserId == userId)
                        .Select(mine => (int?)(b.Reservations.Count(other => other.ReservationDate < mine.ReservationDate) + 1))
                        .FirstOrDefault()
                })
                .ToListAsync();
        }

        // Admin catalogue: search, filter, sort and page in the database, then
        // project straight to the DTO so only the needed columns are loaded
        public async Task<PagedResult<AdminBookDto>> GetAdminCatalogueAsync(AdminCatalogueQuery q)
        {
            var now = DateTime.UtcNow;
            var books = _db.Books.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q.Q))
            {
                var term = q.Q.Trim().ToLower();
                books = books.Where(b => b.Title.ToLower().Contains(term) || b.Author.ToLower().Contains(term));
            }
            if (q.Genre.HasValue) books = books.Where(b => b.Genre == q.Genre.Value);
            if (q.BookType.HasValue) books = books.Where(b => b.BookType == q.BookType.Value);
            if (q.NeedsAttention) books = books.Where(b => b.BookType == BookTypeEnums.EBook && b.EpubFileKey == null);

            // Status is worked out from loans, reservations and the admin status,
            // the same way GetBookStatus does, rather than trusting the stored BookStatus
            // (Returned and Added are rejected by the service before we get here)
            books = q.Status switch
            {
                BookStatusEnum.Available => books.Where(b => b.AdminStatus == null && b.CheckedOutBy == null && !b.Reservations.Any()),
                BookStatusEnum.CheckedOut => books.Where(b => b.CheckedOutBy != null),
                BookStatusEnum.Overdue => books.Where(b => b.CheckedOutBy != null && b.CheckedOutBy.ReturnDate < now),
                BookStatusEnum.Reserved => books.Where(b => b.AdminStatus == null && b.CheckedOutBy == null && b.Reservations.Any()),
                BookStatusEnum.Lost => books.Where(b => b.AdminStatus == BookStatusEnum.Lost),
                BookStatusEnum.Removed => books.Where(b => b.AdminStatus == BookStatusEnum.Removed),
                _ => books // no status filter
            };

            // Each sort ends with a unique tie-breaker. Without one, books with the same
            // title can swap places between pages, so one shows up twice and another never.
            books = q.Sort switch
            {
                CatalogueSort.TitleDesc => books.OrderByDescending(b => b.Title).ThenBy(b => b.BookId),
                CatalogueSort.AuthorAsc => books.OrderBy(b => b.Author).ThenBy(b => b.Title).ThenBy(b => b.BookId),
                CatalogueSort.YearDesc => books.OrderByDescending(b => b.PublicationYear).ThenBy(b => b.Title).ThenBy(b => b.BookId),
                CatalogueSort.YearAsc => books.OrderBy(b => b.PublicationYear).ThenBy(b => b.Title).ThenBy(b => b.BookId),
                _ => books.OrderBy(b => b.Title).ThenBy(b => b.BookId), // TitleAsc
            };

            var total = await books.CountAsync();
            var pageSize = Math.Clamp(q.PageSize, 1, 50);
            var page = Math.Max(1, q.Page);

            var items = await books
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new AdminBookDto
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Author = b.Author,
                    CoverImagePath = b.CoverImagePath,
                    Genre = b.Genre,
                    Language = b.Language,
                    BookType = b.BookType,
                    PublicationYear = b.PublicationYear,
                    HasEpub = b.EpubFileKey != null,
                    BookStatus = b.BookStatus,
                    IsOverdue = b.CheckedOutBy != null && b.CheckedOutBy.ReturnDate < now,
                    BorrowerName = b.CheckedOutBy != null ? b.CheckedOutBy.User!.UserName : null,
                    DueDate = b.CheckedOutBy != null ? b.CheckedOutBy.ReturnDate : null,
                    NextInLineName = b.Reservations.OrderBy(r => r.ReservationDate).Select(r => r.User.UserName).FirstOrDefault(),
                    ExpectedDate = b.Reservations.OrderBy(r => r.ReservationDate).Select(r => r.AvailabilityDate).FirstOrDefault(),
                    AdminStatusNotes = b.AdminStatusNotes,
                })
                .ToListAsync();

            return new PagedResult<AdminBookDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
        }

        public async Task SaveAsync()
        {
            await _db.SaveChangesAsync();
        }

        // Only marks the book as changed; SaveAsync writes it to the database
        public Task UpdateAsync(Book book)
        {
            _db.Books.Update(book);
            return Task.CompletedTask;
        }
    }
}
