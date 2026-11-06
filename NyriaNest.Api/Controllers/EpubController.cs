using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Services;
using NyriaNest.Domain.Constants;
using System.Security.Claims;

namespace NyriaNest.Api.Controllers
{
    [Route("api/Book/{bookId:int}/epub")]
    [ApiController]
    public class EpubController : ControllerBase
    {
        private readonly IEpubService _epubService;

        public EpubController(IEpubService epubService)
        {
            _epubService = epubService;
        }

        /// <summary>Upload or replace the EPUB file for a book (admins only).</summary>
        [HttpPost]
        [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
        [RequestSizeLimit(EpubService.MaxFileSize)]
        [RequestFormLimits(MultipartBodyLengthLimit = EpubService.MaxFileSize)]
        public async Task<IActionResult> Upload(int bookId, IFormFile file, CancellationToken ct)
        {
            var response = await _epubService.UploadAsync(bookId, file.ToUploadedFile(), ct);
            return StatusCode((int)response.StatusCode, response);
        }

        /// <summary>Download the EPUB. Users must have the book checked out; admins can always download.</summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Download(int bookId, CancellationToken ct)
        {
            var userId = User.FindFirstValue("userId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isAdmin = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Admin);
            var (status, download) = await _epubService.GetForUserAsync(bookId, userId, isAdmin, ct);

            if (download is null)
                return StatusCode((int)status);

            return File(download.Content, "application/epub+zip", download.FileName);
        }

        /// <summary>Remove the EPUB file from a book (admins only).</summary>
        [HttpDelete]
        [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
        public async Task<IActionResult> Delete(int bookId, CancellationToken ct)
        {
            var response = await _epubService.DeleteAsync(bookId, ct);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}