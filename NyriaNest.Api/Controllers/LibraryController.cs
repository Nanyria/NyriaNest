using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.Books;
using System.Security.Claims;


[Route("api/[controller]")]
[ApiController]
public class LibraryController : ControllerBase
{
    private readonly IBookService _bookService;
    public LibraryController(IBookService bookService)
    {
        _bookService = bookService;
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


}
