using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using System.Buffers.Text;
using System.Net;
using System.Text;

namespace NyriaNest.Application.Accounts
{
    public interface IPasswordResetService
    {
        Task<APIResponse<string>> SendResetLinkAsync(ForgotPasswordDto request);
        Task<APIResponse<string>> ResetPasswordAsync(ResetPasswordDto request);
    }

    /// <summary>
    /// A forgotten password is reset through an emailed link, never by emailing a password.
    /// Identity makes the one-time token (valid for an hour, see Program.cs) and checks it.
    /// Neither step tells anyone whether an account exists: the answer to a reset request
    /// is always the same, and every bad link gets the same error.
    /// </summary>
    public class PasswordResetService : IPasswordResetService
    {
        public const string SentMessage = "If an account exists for that email, we've sent a link to reset your password.";
        public const string InvalidLinkMessage = "This reset link is invalid or has expired.";

        private readonly UserManager<User> _userManager;
        private readonly IEmailService _emailService;
        private readonly ILogger<PasswordResetService> _logger;
        private readonly string _frontendBaseUrl;

        public PasswordResetService(
            UserManager<User> userManager,
            IEmailService emailService,
            ILogger<PasswordResetService> logger,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _emailService = emailService;
            _logger = logger;
            // Checked at startup (Program.cs), so it's always set here
            _frontendBaseUrl = configuration["Frontend:BaseUrl"]!.TrimEnd('/');
        }

        public async Task<APIResponse<string>> SendResetLinkAsync(ForgotPasswordDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email.Trim());
            if (user?.Email is not null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                // The token may contain + / =, so it's made URL-safe for the link
                var urlToken = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
                var link = $"{_frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={urlToken}";
                try
                {
                    await _emailService.SendPasswordResetEmailAsync(user.Email, user.FirstName, link);
                }
                catch (Exception ex)
                {
                    // Answer the same as always: an error here would tell that the account exists.
                    // The link is not logged, since it would let anyone reading the log reset the password.
                    _logger.LogError(ex, "Couldn't send a password reset email to user {UserId}", user.Id);
                }
            }

            return new APIResponse<string> { IsSuccess = true, StatusCode = HttpStatusCode.OK, Result = SentMessage };
        }

        public async Task<APIResponse<string>> ResetPasswordAsync(ResetPasswordDto request)
        {
            var response = new APIResponse<string> { IsSuccess = false, StatusCode = HttpStatusCode.BadRequest };

            var user = await _userManager.FindByEmailAsync(request.Email.Trim());
            var token = Decode(request.Token);
            if (user is null || token is null)
            {
                response.ErrorMessages.Add(InvalidLinkMessage);
                return response;
            }

            // Identity checks the token, then the new password against the rules. Changing the
            // password changes the user's security stamp, which makes the token single-use.
            var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                    response.ErrorMessages.Add(InvalidLinkMessage);
                else
                    response.ErrorMessages.AddRange(result.Errors.Select(e => e.Description));
                return response;
            }

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = "Your password has been changed. You can log in with it now.";
            return response;
        }

        private static string? Decode(string urlToken)
        {
            try
            {
                return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(urlToken));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
