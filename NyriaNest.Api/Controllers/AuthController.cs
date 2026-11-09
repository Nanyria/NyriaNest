using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Accounts;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        public const string PasswordResetRateLimit = "password-reset";

        private readonly UserManager<User> _userManager;
        private readonly ITokenService _tokens;
        private readonly IPasswordResetService _passwordReset;

        public AuthController(UserManager<User> userManager, ITokenService tokens, IPasswordResetService passwordReset)
        {
            _userManager = userManager;
            _tokens = tokens;
            _passwordReset = passwordReset;
        }

        // "I forgot my password": emails a reset link if the account exists. Always the
        // same answer, so it can't be used to find out who has an account.
        [HttpPost("forgot-password")]
        [EnableRateLimiting(PasswordResetRateLimit)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
        {
            var response = await _passwordReset.SendResetLinkAsync(request);
            return StatusCode((int)response.StatusCode, response);
        }

        // From the reset page: sets the new password with the link's one-time token
        [HttpPost("reset-password")]
        [EnableRateLimiting(PasswordResetRateLimit)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            var response = await _passwordReset.ResetPasswordAsync(request);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var user = await _userManager.FindByNameAsync(loginDto.Username);
            if (user != null && await _userManager.CheckPasswordAsync(user, loginDto.Password))
            {
                var issued = await _tokens.CreateTokenAsync(user);
                return Ok(new { token = issued.Token, expiration = issued.Expires });
            }
            return Unauthorized();
        }
    }

    public class LoginDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
