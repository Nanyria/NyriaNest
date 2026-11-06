using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Data;
using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models;
using NyriaNest.Api.Storage;

namespace NyriaNest.Api.Services
{
    public record EpubDownload(Stream Content, string FileName);

    public interface IEpubService
    {
        Task<string?> ValidateAsync(UploadedFile? file, CancellationToken ct = default);
        Task<APIResponse<string>> UploadAsync(int bookId, UploadedFile file, CancellationToken ct = default);
        Task<APIResponse<bool>> DeleteAsync(int bookId, CancellationToken ct = default);
        Task<(HttpStatusCode Status, EpubDownload? Download)> GetForUserAsync(int bookId, string userId, bool isAdmin, CancellationToken ct = default);
    }

    public class EpubService : IEpubService
    {
        public const long MaxFileSize = 100L * 1024 * 1024; // 100 MB

        // An EPUB is a ZIP whose first entry must be an uncompressed file called "mimetype"
        // containing "application/epub+zip". Checking this stops renamed non-EPUB files.
        private static readonly byte[] EpubSignature = Encoding.ASCII.GetBytes("mimetypeapplication/epub+zip");

        private readonly IAppDbContext _db;
        private readonly IFileStorage _storage;
        private readonly ILogger<EpubService> _logger;

        public EpubService(IAppDbContext db, IFileStorage storage, ILogger<EpubService> logger)
        {
            _db = db;
            _storage = storage;
            _logger = logger;
        }

        /// <summary>Returns an error message, or null if the file is a valid EPUB.</summary>
        public async Task<string?> ValidateAsync(UploadedFile? file, CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                return "No file was uploaded.";
            if (file.Length > MaxFileSize)
                return "The file is larger than 100 MB.";
            if (!Path.GetExtension(file.FileName).Equals(".epub", StringComparison.OrdinalIgnoreCase))
                return "Only .epub files are allowed.";
            if (!await LooksLikeEpubAsync(file, ct))
                return "The file is not a valid EPUB.";
            return null;
        }

        public async Task<APIResponse<string>> UploadAsync(int bookId, UploadedFile file, CancellationToken ct = default)
        {
            var response = new APIResponse<string>();

            var fileError = await ValidateAsync(file, ct);
            if (fileError is not null)
                return Fail(response, HttpStatusCode.BadRequest, fileError);

            var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == bookId, ct);
            if (book is null)
                return Fail(response, HttpStatusCode.NotFound, $"Book {bookId} was not found.");
            if (book.BookType != BookTypeEnums.EBook)
                return Fail(response, HttpStatusCode.BadRequest, "Only e-books can have an EPUB file.");

            var newKey = $"{bookId}/{Guid.NewGuid():N}.epub";
            await using (var stream = file.OpenReadStream())
            {
                await _storage.SaveAsync(stream, newKey, ct);
            }

            var oldKey = book.EpubFileKey;
            book.EpubFileKey = newKey;
            book.EpubFileName = TrimFileName(Path.GetFileName(file.FileName));
            book.EpubFileSize = file.Length;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch
            {
                // Don't leave an orphaned file behind if the database update fails
                await _storage.DeleteAsync(newKey, CancellationToken.None);
                throw;
            }

            if (oldKey is not null)
                await _storage.DeleteAsync(oldKey, ct);

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = newKey;
            return response;
        }

        public async Task<APIResponse<bool>> DeleteAsync(int bookId, CancellationToken ct = default)
        {
            var response = new APIResponse<bool>();

            var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == bookId, ct);
            if (book is null)
                return Fail(response, HttpStatusCode.NotFound, $"Book {bookId} was not found.");
            if (book.EpubFileKey is null)
                return Fail(response, HttpStatusCode.NotFound, "This book has no EPUB file.");

            await _storage.DeleteAsync(book.EpubFileKey, ct);
            book.EpubFileKey = null;
            book.EpubFileName = null;
            book.EpubFileSize = null;
            await _db.SaveChangesAsync(ct);

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = true;
            return response;
        }

        public async Task<(HttpStatusCode Status, EpubDownload? Download)> GetForUserAsync(
            int bookId, string userId, bool isAdmin, CancellationToken ct = default)
        {
            var book = await _db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == bookId, ct);
            if (book?.EpubFileKey is null)
                return (HttpStatusCode.NotFound, null);

            if (!isAdmin)
            {
                // A CheckedOutItem exists only while the book is checked out (it's removed on return)
                var hasCheckedOut = await _db.CheckOutItems
                    .AnyAsync(c => c.BookId == bookId && c.UserId == userId, ct);
                if (!hasCheckedOut)
                    return (HttpStatusCode.Forbidden, null);
            }

            var stream = await _storage.OpenReadAsync(book.EpubFileKey, ct);
            if (stream is null)
            {
                _logger.LogWarning("EPUB file {Key} for book {BookId} is missing from storage.", book.EpubFileKey, bookId);
                return (HttpStatusCode.NotFound, null);
            }

            return (HttpStatusCode.OK, new EpubDownload(stream, ToFileName(book.Title)));
        }

        private static async Task<bool> LooksLikeEpubAsync(UploadedFile file, CancellationToken ct)
        {
            // ZIP local file header is 30 bytes; the "mimetype" name and its content follow directly
            var header = new byte[30 + EpubSignature.Length];
            await using var stream = file.OpenReadStream();
            var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            if (read < header.Length)
                return false;

            var isZip = header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04; // "PK\3\4"
            return isZip && header.AsSpan(30).SequenceEqual(EpubSignature);
        }

        private static string ToFileName(string title)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return (string.IsNullOrWhiteSpace(cleaned) ? "book" : cleaned) + ".epub";
        }

        // Fits the 255-character column, keeping the ".epub" ending
        private static string TrimFileName(string name)
        {
            const int max = 255;
            if (name.Length <= max) return name;
            var ext = Path.GetExtension(name);
            return name[..(max - ext.Length)] + ext;
        }

        private static APIResponse<T> Fail<T>(APIResponse<T> response, HttpStatusCode status, string message)
        {
            response.IsSuccess = false;
            response.StatusCode = status;
            response.ErrorMessages.Add(message);
            return response;
        }
    }
}