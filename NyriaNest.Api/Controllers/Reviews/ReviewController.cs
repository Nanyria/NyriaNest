using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.Reviews;

namespace NyriaNest.Api.Controllers.Reviews
{
    // The logged-in user's own reviews and ratings, taken from the token – never from the
    // URL or the body. Admins removing other users' reviews is a separate thing (#50).
    [Authorize]
    [ApiController]
    [Route("api/UserActions")]
    public class ReviewController : ControllerBase
    {
        private readonly IReviewService _reviews;

        public ReviewController(IReviewService reviews)
        {
            _reviews = reviews;
        }

        // Review methods
        [HttpGet("reviews")]
        public async Task<IActionResult> GetUserReviews()
        {
            var response = await _reviews.GetUserReviewsAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPost("newReview")]
        public async Task<IActionResult> CreateReview([FromBody] CreateReviewItemDto createReviewDto)
        {
            // Whatever userId the body says, the review is written by the logged-in user
            createReviewDto.UserId = User.GetUserId();
            var response = await _reviews.CreateReviewAsync(createReviewDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("editReview/{reviewId}")]
        public async Task<IActionResult> EditReview([FromRoute] int reviewId, [FromBody] ReviewItemDto updateReviewDto)
        {
            var response = await _reviews.EditReviewAsync(User.GetUserId(), reviewId, updateReviewDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("deleteReview/{reviewId}")]
        public async Task<IActionResult> DeleteReview([FromRoute] int reviewId)
        {
            var response = await _reviews.RemoveReviewItemAsync(User.GetUserId(), reviewId);
            return StatusCode((int)response.StatusCode, response);
        }

        // Rating methods
        [HttpPut("editRating/{reviewId}")]
        public async Task<IActionResult> EditRating([FromRoute] int reviewId, [FromBody] RatingItemDto ratingDto)
        {
            var response = await _reviews.EditRatingAsync(User.GetUserId(), reviewId, ratingDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("deleteRating/{reviewId}")]
        public async Task<IActionResult> DeleteRating([FromRoute] int reviewId)
        {
            var response = await _reviews.RemoveRatingAsync(User.GetUserId(), reviewId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
