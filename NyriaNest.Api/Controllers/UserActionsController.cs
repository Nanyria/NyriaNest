using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Services;

namespace NyriaNest.Api.Controllers
{
    // Everything here needs a login, except registering. The user an action applies to
    // is always the logged-in one, taken from the token – never from the URL or the body.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UserActionsController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IUserActionsService _userActionsService;
        private readonly IProfilePictureService _profilePictures;

        public UserActionsController(IUserService userService, IUserActionsService userActionsService, IProfilePictureService profilePictures)
        {
            _userService = userService;
            _userActionsService = userActionsService;
            _profilePictures = profilePictures;
        }

        // Set by AuthController when it issues the token. [Authorize] guarantees a valid
        // token, and every token we issue has this claim.
        private string CurrentUserId =>
            User.FindFirst("userId")?.Value
            ?? throw new InvalidOperationException("The token has no userId claim.");

        // Own things only, for every role. What admins do for or to other users
        // (e.g. removing a review) lives in admin endpoints instead (#50).

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> AddUser([FromBody] CreateUserDto createUserDto)
        {
            var response = await _userActionsService.AddUserAsync(createUserDto);
            return StatusCode((int)response.StatusCode, response);
        }

        // The logged-in user's own profile, for every role. GET api/User/{id} is admin-only,
        // so this is what the frontend loads the current user with.
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var response = await _userService.GetUserByIdAsync(CurrentUserId);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("update")]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserDto updateUserDto)
        {
            var response = await _userActionsService.UpdateUserAsync(CurrentUserId, updateUserDto);
            return StatusCode((int)response.StatusCode, response);
        }

        // Upload my own profile picture (PNG, JPEG or WebP). Returns its URL, which is
        // now my picture. The request limit leaves room above the service's 512 KB, so a
        // slightly too large file gets the service's clear message instead of a bare 413.
        [HttpPost("avatar")]
        [RequestSizeLimit(1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 1024 * 1024)]
        public async Task<IActionResult> UploadProfilePicture(IFormFile? file, CancellationToken ct)
        {
            var response = await _profilePictures.UploadAsync(CurrentUserId, file?.ToUploadedFile(), ct);
            return StatusCode((int)response.StatusCode, response);
        }

        // Borrow methods

        [HttpGet("checkedout")]
        public async Task<IActionResult> GetCheckedOutBooks()
        {
            var response = await _userActionsService.GetCheckedOutBooksAsync(CurrentUserId);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("borrow/{bookId}")]
        public async Task<IActionResult> BorrowBook([FromRoute] int bookId)
        {
            var response = await _userActionsService.CheckOutBookAsync(CurrentUserId, bookId);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("return/{bookId}")]
        public async Task<IActionResult> ReturnBook([FromRoute] int bookId)
        {
            var response = await _userActionsService.ReturnBookAsync(CurrentUserId, bookId);
            return StatusCode((int)response.StatusCode, response);
        }


        // Reserve methods

        [HttpGet("reserved")]
        public async Task<IActionResult> GetReservedBooks()
        {
            var response = await _userActionsService.GetReservedBooksAsync(CurrentUserId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("reserve/{bookId}")]
        public async Task<IActionResult> ReserveBook([FromRoute] int bookId)
        {
            var response = await _userActionsService.ReserveBookAsync(CurrentUserId, bookId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("unreserve/{bookId}")]
        public async Task<IActionResult> UnreserveBook([FromRoute] int bookId)
        {
            var response = await _userActionsService.CancelReservationAsync(CurrentUserId, bookId);
            return StatusCode((int)response.StatusCode, response);
        }

        // Review methods
        [HttpGet("reviews")]
        public async Task<IActionResult> GetUserReviews()
        {
            var response = await _userActionsService.GetUserReviewsAsync(CurrentUserId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPost("newReview")]
        public async Task<IActionResult> CreateReview([FromBody] CreateReviewItemDto createReviewDto)
        {
            // Whatever userId the body says, the review is written by the logged-in user
            createReviewDto.UserId = CurrentUserId;
            var response = await _userActionsService.CreateReviewAsync(createReviewDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("editReview/{reviewId}")]
        public async Task<IActionResult> EditReview([FromRoute] int reviewId, [FromBody] ReviewItemDto updateReviewDto)
        {
            var response = await _userActionsService.EditReviewAsync(CurrentUserId, reviewId, updateReviewDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("deleteReview/{reviewId}")]
        public async Task<IActionResult> DeleteReview([FromRoute] int reviewId)
        {
            var response = await _userActionsService.RemoveReviewItemAsync(CurrentUserId, reviewId);
            return StatusCode((int)response.StatusCode, response);
        }

        // Rating methods
        [HttpPut("editRating/{reviewId}")]
        public async Task<IActionResult> EditRating([FromRoute] int reviewId, [FromBody] RatingItemDto ratingDto)
        {
            var response = await _userActionsService.EditRatingAsync(CurrentUserId, reviewId, ratingDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("deleteRating/{reviewId}")]
        public async Task<IActionResult> DeleteRating([FromRoute] int reviewId)
        {
            var response = await _userActionsService.RemoveRatingAsync(CurrentUserId, reviewId);
            return StatusCode((int)response.StatusCode, response);
        }

        // Favorite methods

        [HttpGet("favorites")]
        public async Task<IActionResult> GetFavoriteBooks()
        {
            var response = await _userActionsService.GetFavoriteItemsAsync(CurrentUserId);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("favorite/{bookId}")]
        public async Task<IActionResult> AddToFavorites([FromRoute] int bookId)
        {
            var response = await _userActionsService.AddToFavoritesAsync(CurrentUserId, bookId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("unfavorite/{bookId}")]
        public async Task<IActionResult> RemoveFromFavorites([FromRoute] int bookId)
        {
            var response = await _userActionsService.RemoveFromFavoritesAsync(CurrentUserId, bookId);
            return StatusCode((int)response.StatusCode, response);
        }

    }
}
