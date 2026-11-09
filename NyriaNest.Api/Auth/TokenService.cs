using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using NyriaNest.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NyriaNest.Api.Auth
{
    public record IssuedToken(string Token, DateTime Expires);

    public interface ITokenService
    {
        Task<IssuedToken> CreateTokenAsync(User user);
    }

    /// <summary>
    /// Makes the login token (JWT). It carries the user's security stamp, which Identity
    /// changes on every password and email change: Program.cs refuses a token whose
    /// stamp is out of date, so such a change ends every other session (#113).
    /// </summary>
    public class TokenService : ITokenService
    {
        public const string SecurityStampClaim = "securityStamp";
        public const string UserIdClaim = "userId";

        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;

        public TokenService(UserManager<User> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
        }

        public async Task<IssuedToken> CreateTokenAsync(User user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.UserName!),
                new(JwtRegisteredClaimNames.Jti, user.Id),
                new(UserIdClaim, user.Id),
                new(SecurityStampClaim, await _userManager.GetSecurityStampAsync(user)),
            };
            claims.AddRange((await _userManager.GetRolesAsync(user)).Select(role => new Claim(ClaimTypes.Role, role)));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                expires: DateTime.UtcNow.AddHours(3),
                claims: claims,
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);
        }
    }
}
