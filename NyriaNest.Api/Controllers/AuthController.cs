using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Services;
using NyriaNest.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NyriaNest.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        public const string PasswordResetRateLimit = "password-reset";

        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        private readonly IPasswordResetService _passwordReset;

        public AuthController(UserManager<User> userManager, IConfiguration configuration, IPasswordResetService passwordReset)
        {
            _userManager = userManager;
            _configuration = configuration;
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
                var userRoles = await _userManager.GetRolesAsync(user);

                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Jti, user.Id), // Use user.Id here
	                new Claim("userId", user.Id), // Custom claim for clarity
                };

                // Add role claims
                authClaims.AddRange(userRoles.Select(role => new Claim(ClaimTypes.Role, role)));

                var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));

                var token = new JwtSecurityToken(
                    issuer: _configuration["Jwt:Issuer"],
                    audience: _configuration["Jwt:Audience"],
                    expires: DateTime.Now.AddHours(3),
                    claims: authClaims,
                    signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
                );

                return Ok(new
                {
                    token = new JwtSecurityTokenHandler().WriteToken(token),
                    expiration = token.ValidTo
                });
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
