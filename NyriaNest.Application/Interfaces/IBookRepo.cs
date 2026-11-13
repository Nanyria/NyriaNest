using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Application.Interfaces
{
    public interface IBookRepo
    {
        Task<Book?> GetByIdAsync(int bookId);
        /// <summary>The book's status history, newest first, with the book loaded. Read-only.</summary>
        Task<List<StatusHistoryItem>> GetStatusHistoryAsync(int bookId);
        /// <summary>The book's reservation queue, oldest first, with each reader's name. Read-only.</summary>
        Task<List<BookReservationDto>> GetReservationQueueAsync(int bookId);
        Task<List<LibraryBookDto>> GetLibraryListAsync(string? userId);
        Task<PagedResult<AdminBookDto>> GetAdminCatalogueAsync(AdminCatalogueQuery q);
        Task CreateBookAsync(Book book);
        Task UpdateAsync(Book book);
        Task DeleteAsync(Book book);
        Task SaveAsync();
    }
}
