using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.Reviews;

namespace NyriaNest.Api.Controllers.Reviews
{
    // A book's reviews, public, beside the library list in api/Library.
    [Route("api/Library")]
    [ApiController]
    public class BookReviewsController : ControllerBase
    {
        private readonly IReviewService _reviews;

        public BookReviewsController(IReviewService reviews)
        {
            _reviews = reviews;
        }

        [HttpGet("review/{bookId:int}")]
        public async Task<IActionResult> GetBookReview(int bookId)
        {
            var response = await _reviews.GetBookReviewsAsync(bookId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
