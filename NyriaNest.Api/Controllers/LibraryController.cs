using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.Reviews;
using NyriaNest.Application.Services;
using System.Security.Claims;


[Route("api/[controller]")]
[ApiController]
public class LibraryController : ControllerBase
{
    private readonly IBookService _bookService;
    private readonly IReviewService _reviews;
    public LibraryController(IBookService bookService, IReviewService reviews)
    {
        _bookService = bookService;
        _reviews = reviews;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllBooks()
    {
        var userId = User.FindFirstValue("userId"); // null when not logged in
        var response = await _bookService.GetLibraryAsync(userId);
        return StatusCode((int)response.StatusCode, response);
    }

    // No reservations endpoint here: readers see their own place in the queue in the
    // list above (MyQueuePosition), and the whole queue is admin-only (#71)

    [HttpGet("review/{bookId:int}")]
    public async Task<IActionResult> GetBookReview(int bookId)
    {
        var response = await _reviews.GetBookReviewsAsync(bookId);
        return StatusCode((int)response.StatusCode, response);
    }


}
