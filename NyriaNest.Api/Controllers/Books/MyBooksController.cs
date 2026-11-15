using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NyriaNest.Api.Auth;
using NyriaNest.Application.Books;
using NyriaNest.Application.DTOs.Books;

namespace NyriaNest.Api.Controllers.Books;

// The logged-in user's own books (#83), the owner taken from the token. Someone else's
// book or a shared-library book looks missing (404); admins change those through api/Book.
[Authorize]
[ApiController]
[Route("api/my/books")]
public class MyBooksController : ControllerBase
{
    private readonly IBookService _bookService;

    public MyBooksController(IBookService bookService)
    {
        _bookService = bookService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyBooks()
    {
        var response = await _bookService.GetOwnBooksAsync(User.GetUserId());
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpGet("{bookId:int}")]
    public async Task<IActionResult> GetMyBook(int bookId)
    {
        var response = await _bookService.GetOwnBookAsync(bookId, User.GetUserId());
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpPost]
    public async Task<IActionResult> AddMyBook([FromBody] SlimBookDto book)
    {
        var response = await _bookService.AddOwnBookAsync(book, User.GetUserId());
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpPut("{bookId:int}")]
    public async Task<IActionResult> UpdateMyBook(int bookId, [FromBody] SlimBookDto book)
    {
        var response = await _bookService.UpdateOwnBookAsync(bookId, User.GetUserId(), book);
        return StatusCode((int)response.StatusCode, response);
    }

    // A book lent to a name gets 409 with a question; the client asks and sends { confirmLent: true }
    [HttpDelete("{bookId:int}")]
    public async Task<IActionResult> DeleteMyBook(int bookId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteMyBookRequest? body)
    {
        var response = await _bookService.DeleteOwnBookAsync(bookId, User.GetUserId(), body?.ConfirmLent ?? false);
        return StatusCode((int)response.StatusCode, response);
    }

    public record DeleteMyBookRequest(bool ConfirmLent);
}
