using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.Accounts;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Loans;
using NyriaNest.Application.ReadingList;
using NyriaNest.Application.Reviews;
using NyriaNest.Application.Users;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Api.Controllers
{
    // Everything here needs a login, except registering. The user an action applies to
    // is always the logged-in one, taken from the token – never from the URL or the body.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UserActionsController : ControllerBase
    {
        private readonly IUserAdminService _users;
        private readonly IAccountService _accounts;
        private readonly IProfilePictureService _profilePictures;
        private readonly UserManager<User> _userManager;
        private readonly ITokenService _tokens;
        private readonly IReadingListService _readingList;
        private readonly IReviewService _reviews;
        private readonly IReservationService _reservations;
        private readonly ILoanService _loans;

        public UserActionsController(IUserAdminService users, IAccountService accounts, IProfilePictureService profilePictures,
            UserManager<User> userManager, ITokenService tokens, IReadingListService readingList, IReviewService reviews, IReservationService reservations, ILoanService loans)
        {
            _reviews = reviews;
            _reservations = reservations;
            _loans = loans;
            _users = users;
            _accounts = accounts;
            _profilePictures = profilePictures;
            _userManager = userManager;
            _tokens = tokens;
            _readingList = readingList;
        }

        // Own things only, for every role. What admins do for or to other users
        // (e.g. removing a review) lives in admin endpoints instead (#50).

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> AddUser([FromBody] CreateUserDto createUserDto)
        {
            var response = await _accounts.AddUserAsync(createUserDto);
            return StatusCode((int)response.StatusCode, response);
        }

        // The logged-in user's own profile, for every role. GET api/User/{id} is admin-only,
        // so this is what the frontend loads the current user with.
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var response = await _users.GetUserByIdAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("update")]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserDto updateUserDto)
        {
            updateUserDto.Token = null; // never taken from the request
            var response = await _accounts.UpdateUserAsync(User.GetUserId(), updateUserDto);

            // A new password or email changes the security stamp, which ends every session,
            // this one too (#113). The caller proved who they are (current password), so
            // they get a new token and stay logged in here.
            if (response.IsSuccess && response.Result is not null)
            {
                var user = await _userManager.FindByIdAsync(User.GetUserId());
                if (user is not null
                    && await _userManager.GetSecurityStampAsync(user) != User.FindFirst(TokenService.SecurityStampClaim)?.Value)
                {
                    response.Result.Token = (await _tokens.CreateTokenAsync(user)).Token;
                }
            }
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
            var response = await _profilePictures.UploadAsync(User.GetUserId(), file?.ToUploadedFile(), ct);
            return StatusCode((int)response.StatusCode, response);
        }

        // Borrow methods

        [HttpGet("checkedout")]
        public async Task<IActionResult> GetCheckedOutBooks()
        {
            var response = await _loans.GetCheckedOutBooksAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("borrow/{bookId}")]
        public async Task<IActionResult> BorrowBook([FromRoute] int bookId)
        {
            var response = await _loans.CheckOutBookAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("return/{bookId}")]
        public async Task<IActionResult> ReturnBook([FromRoute] int bookId)
        {
            var response = await _loans.ReturnBookAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }


        // Reserve methods

        [HttpGet("reserved")]
        public async Task<IActionResult> GetReservedBooks()
        {
            var response = await _reservations.GetReservedBooksAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("reserve/{bookId}")]
        public async Task<IActionResult> ReserveBook([FromRoute] int bookId)
        {
            var response = await _reservations.ReserveBookAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("unreserve/{bookId}")]
        public async Task<IActionResult> UnreserveBook([FromRoute] int bookId)
        {
            var response = await _reservations.CancelReservationAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
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

        // Favorite methods

        [HttpGet("favorites")]
        public async Task<IActionResult> GetFavoriteBooks()
        {
            var response = await _readingList.GetFavoriteItemsAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("favorite/{bookId}")]
        public async Task<IActionResult> AddToFavorites([FromRoute] int bookId)
        {
            var response = await _readingList.AddToFavoritesAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("unfavorite/{bookId}")]
        public async Task<IActionResult> RemoveFromFavorites([FromRoute] int bookId)
        {
            var response = await _readingList.RemoveFromFavoritesAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }

    }
}
