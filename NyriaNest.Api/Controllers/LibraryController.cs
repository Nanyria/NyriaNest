using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.Services;
using System.Security.Claims;


[Route("api/[controller]")]
[ApiController]
public class LibraryController : ControllerBase
{
    private readonly IBookService _bookService;
    private readonly IUserService _userService;
    public LibraryController(IBookService bookService, IUserService userService)
    {
        _bookService = bookService;
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllBooks()
    {
        var userId = User.FindFirstValue("userId"); // null when not logged in
        var response = await _bookService.GetLibraryAsync(userId);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpGet("reservations/{bookId:int}")]
    public async Task<IActionResult> GetBookReservations(int bookId)
    {
        var response = await _bookService.GetBookReservationsAsync(bookId);
        return StatusCode((int)response.StatusCode, response);
    }

    [HttpGet("review/{bookId:int}")]
    public async Task<IActionResult> GetBookReview(int bookId)
    {
        var response = await _bookService.GetBookReviewsAsync(bookId);
        return StatusCode((int)response.StatusCode, response);
    }


}
