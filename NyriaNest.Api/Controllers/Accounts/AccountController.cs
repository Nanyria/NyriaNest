using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.Accounts;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Users;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Api.Controllers.Accounts
{
    // Everything here needs a login, except registering. The user an action applies to
    // is always the logged-in one, taken from the token – never from the URL or the body.
    [Authorize]
    [ApiController]
    [Route("api/UserActions")]
    public class AccountController : ControllerBase
    {
        private readonly IUserAdminService _users;
        private readonly IAccountService _accounts;
        private readonly IProfilePictureService _profilePictures;
        private readonly UserManager<User> _userManager;
        private readonly ITokenService _tokens;

        public AccountController(IUserAdminService users, IAccountService accounts, IProfilePictureService profilePictures,
            UserManager<User> userManager, ITokenService tokens)
        {
            _users = users;
            _accounts = accounts;
            _profilePictures = profilePictures;
            _userManager = userManager;
            _tokens = tokens;
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
    }
}
