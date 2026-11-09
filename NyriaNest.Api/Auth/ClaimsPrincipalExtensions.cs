using System.Security.Claims;

namespace NyriaNest.Api.Auth
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// The logged-in user's id, from the token. Set by AuthController when it issues the
        /// token. Use it behind [Authorize], which guarantees a valid token, and every token
        /// we issue has this claim.
        /// </summary>
        public static string GetUserId(this ClaimsPrincipal user) =>
            user.FindFirst(TokenService.UserIdClaim)?.Value
            ?? throw new InvalidOperationException("The token has no userId claim.");
    }
}
