using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Application.DTOs.Users
{
    /// <summary>"I forgot my password": the email to send a reset link to.</summary>
    public class ForgotPasswordDto
    {
        [Required]
        public string Email { get; set; } = "";
    }

    /// <summary>From the reset page: the email and token out of the emailed link, and the new password.</summary>
    public class ResetPasswordDto
    {
        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string Token { get; set; } = "";

        [Required]
        public string NewPassword { get; set; } = "";
    }
}
