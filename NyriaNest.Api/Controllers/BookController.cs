using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models.Books;
using NyriaNest.Api.Models.Books.BookDTOs;
using NyriaNest.Api.Models.Users;
using NyriaNest.Api.Services;
using System.Security.Claims;


[Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
[Route("api/[controller]")]
[ApiController]
public class BookController : ControllerBase
{
    private readonly IBookService _bookService;
    public BookController(IBookService bookService)
    {
        _bookService = bookService;
    }

    /// <summary>Admin catalogue with search, filters, sorting and paging.</summary>
    /// <remarks>Example: GET /api/Book/catalogue?q=sand&amp;bookType=3&amp;status=available&amp;page=1</remarks>
    [HttpGet("catalogue")]
    public async Task<IActionResult> GetCatalogue([FromQuery] AdminCatalogueQuery query)
    {
        var response = await _bookService.GetAdminCatalogueAsync(query);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBookByID(int id)
    {
        var response = await _bookService.GetBookByIdAsync(id);
        return StatusCode((int)response.StatusCode, response);
    }
    [HttpPost]
    public async Task<IActionResult> AddBook([FromBody] SlimBookDto book)
    {
        var response = await _bookService.AddBookAsync(book);
        return StatusCode((int)response.StatusCode, response);
    }

    /// <summary>Create an e-book together with its EPUB file in one request.</summary>
    [HttpPost("ebook")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(EpubService.MaxFileSize + 1024 * 1024)] // file + room for the form fields
    [RequestFormLimits(MultipartBodyLengthLimit = EpubService.MaxFileSize + 1024 * 1024)]
    public async Task<IActionResult> AddEbook([FromForm] SlimBookDto book, IFormFile file, CancellationToken ct)
    {
        var response = await _bookService.AddEbookAsync(book, file, ct);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpDelete("{bookId:int}")]
    public async Task<IActionResult> DeleteBook(int bookId)
    {
        var response = await _bookService.DeleteBookAsync(bookId);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpPut("{bookId:int}")]
    public async Task<IActionResult> UpdateBookInfo(int bookId, [FromBody] SlimBookDto bookInfo)
    {
        var response = await _bookService.UpdateBookInfoAsync(bookId, bookInfo);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpPut("{bookId:int}/admin-status")]
    public async Task<IActionResult> SetAdminStatus(int bookId, [FromBody] AdminStatusRequest body)
    {
        var adminId = User.FindFirstValue("userId");
        var response = await _bookService.SetAdminStatusAsync(bookId, body.Status, adminId, body.Notes);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpDelete("{bookId:int}/admin-status")]
    public async Task<IActionResult> ClearAdminStatus(int bookId)
    {
        var adminId = User.FindFirstValue("userId");
        var response = await _bookService.ClearAdminStatusAsync(bookId, adminId);
        return StatusCode((int)response.StatusCode, response);
    }
    [HttpGet("history/{bookId:int}")]
    public async Task<IActionResult> GetBookHistory(int bookId)
    {
        var response = await _bookService.GetBookHistoryAsync(bookId);
        return StatusCode((int)response.StatusCode, response);
    }

    public record AdminStatusRequest(BookStatusEnum Status, string? Notes);


}
