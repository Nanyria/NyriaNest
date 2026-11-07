using NyriaNest.Application.DTOs.Books;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Application.Interfaces
{
    public interface IBookRepo
    {
        Task<IEnumerable<Book>> GetAllAsync();
        Task<Book?> GetByIdAsync(int bookId);
        /// <summary>The book's status history, newest first, with the book loaded. Read-only.</summary>
        Task<List<StatusHistoryItem>> GetStatusHistoryAsync(int bookId);
        Task<List<LibraryBookDto>> GetLibraryListAsync(string? userId);
        Task<PagedResult<AdminBookDto>> GetAdminCatalogueAsync(AdminCatalogueQuery q);
        Task CreateBookAsync(Book book);
        Task UpdateAsync(Book book);
        Task DeleteAsync(Book book);
        Task SaveAsync();
    }
}
