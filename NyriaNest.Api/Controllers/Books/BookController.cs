using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NyriaNest.Api.Auth;
using NyriaNest.Api.Controllers;
using NyriaNest.Application.Books;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.Loans;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Security.Claims;

namespace NyriaNest.Api.Controllers.Books;

[Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
[Route("api/[controller]")]
[ApiController]
public class BookController : ControllerBase
{
    private readonly IBookService _bookService;
    private readonly ILoanService _loanService;
    public BookController(IBookService bookService, ILoanService loanService)
    {
        _bookService = bookService;
        _loanService = loanService;
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
        var response = await _bookService.GetBookForEditAsync(id);
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
        var response = await _bookService.AddEbookAsync(book, file.ToUploadedFile(), ct);
        return StatusCode((int)response.StatusCode, response);
    }

    // Someone else's book needs { reason }; a book lent to a name needs { confirmLent: true } (#83)
    [HttpDelete("{bookId:int}")]
    public async Task<IActionResult> DeleteBook(int bookId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookDeleteRequest? body)
    {
        var response = await _bookService.DeleteBookAsync(bookId, User.GetUserId(), body?.Reason, body?.ConfirmLent ?? false);
        return StatusCode((int)response.StatusCode, response);
    }

    // Someone else's book needs a reason, which goes into its history (#83)
    [HttpPut("{bookId:int}")]
    public async Task<IActionResult> UpdateBookInfo(int bookId, [FromBody] BookEditRequest bookInfo)
    {
        var response = await _bookService.UpdateBookInfoAsync(bookId, bookInfo, User.GetUserId(), bookInfo.Reason);
        return StatusCode((int)response.StatusCode, response);
    }

    // Putting right a loan on someone's book (#83): needs a reason, which goes into the
    // history and a notification to the owner. Shared-library books aren't lent by name.
    [HttpPost("{bookId:int}/lend")]
    public async Task<IActionResult> LendBook(int bookId, [FromBody] AdminLendBookRequest body)
    {
        var response = await _loanService.LendAsAdminAsync(bookId, User.GetUserId(), body);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpPost("{bookId:int}/return")]
    public async Task<IActionResult> ReturnBook(int bookId, [FromBody] AdminReturnBookRequest body)
    {
        var response = await _loanService.ReturnAsAdminAsync(bookId, User.GetUserId(), body);
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
    public async Task<IActionResult> ClearAdminStatus(int bookId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AdminStatusClearRequest? body)
    {
        var adminId = User.FindFirstValue("userId");
        var response = await _bookService.ClearAdminStatusAsync(bookId, adminId, body?.Notes);
        return StatusCode((int)response.StatusCode, response);
    }
    [HttpGet("history/{bookId:int}")]
    public async Task<IActionResult> GetBookHistory(int bookId)
    {
        var response = await _bookService.GetBookHistoryAsync(bookId);
        return StatusCode((int)response.StatusCode, response);
    }

    public record AdminStatusRequest(BookStatusEnum Status, string? Notes);

    // Notes are the reason, required when the book is someone else's (#83)
    public record AdminStatusClearRequest(string? Notes);


}
